using System.Globalization;
using System.Runtime.InteropServices;
using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste agent</c> — holds the unlocked vault and asks you about every credential request.
/// </summary>
/// <remarks>
/// <para>
/// The process that makes docs/PRODUCT.md law 3.2 real. It is started by a human, in a terminal a human
/// opened, and the master password is typed there in response to a command they typed — so nothing
/// an agent does can raise a password prompt. That property is the whole reason the approval flow
/// is not built into the bridge (DECISIONS.md D-0023).
/// </para>
/// <para>
/// <b>Deliberately not a daemon.</b> No service, no launch agent, no PID file, no starting itself
/// on demand. It runs in the foreground, says what it is doing, and stops when you stop it — which
/// is also the honest answer to "is anything able to act as me right now?".
/// </para>
/// <para>
/// <b>It holds the vault's claim for as long as it runs</b>, taken before the password is asked
/// for, so a vault the desktop app or another agent already holds is refused by name and never
/// opened (D-0309). Under that claim it applies each agent's settings from the vault and saves a
/// change to them that the person confirms in this terminal.
/// </para>
/// <para>
/// <b>It writes the audit line of every request it answers</b>, in its own home's log, before it
/// replies: an agent's listing, credential and run, and a <c>keypaste run --session</c> or
/// <c>--token</c>, because the side that asked is the side that could skip it or point it elsewhere.
/// A log that cannot be opened stops it before the password is asked for, since it could only refuse.
/// What this process prints is for the person watching it, not the record.
/// </para>
/// <para>
/// <b>The vault stays unlocked for as long as this runs.</b> There is no idle auto-lock — closing
/// the terminal is the lock — and that is stated in docs/approvals.md rather than left for somebody
/// to discover. Ctrl+C, SIGTERM, closing the terminal and <c>keypaste lock</c> end the unlock's
/// <see cref="SessionLifetime"/> before the listener stops, the same transition the desktop's locks
/// take, so a request waiting at the prompt is withdrawn and denied and every grant is zeroed
/// (D-0313).
/// </para>
/// </remarks>
internal static class AgentCommand
{
    internal const string ApproverOption = "approver";
    internal const string TimeoutOption = "approval-timeout";
    internal const string MaxTtlOption = "max-ttl";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(ApproverOption, TakesValue: true),
        new(TimeoutOption, TakesValue: true),
        new(MaxTtlOption, TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var parseError))
        {
            context.Stderr.WriteLine($"keypaste: {parseError}");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine($"keypaste: unexpected argument '{line.Operands[0]}'");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (!TryLimits(line, context, out var limits))
        {
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vaultPath, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste: {locateError}");
            return CliApp.ExitUsageError;
        }

        var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));
        string pipeName;

        try
        {
            pipeName = ApproverEndpoint.Resolve(
                line.Value(ApproverOption),
                context.Environment.Get(ApproverEndpoint.EnvironmentVariable),
                VaultIdentity.Of(home, vaultPath))!;
        }
        catch (ArgumentException ex)
        {
            context.Stderr.WriteLine($"keypaste: {ex.Message}");
            return CliApp.ExitUsageError;
        }

        if (!AuditLog.TryOpen(
                KeypasteHome.AuditPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)),
                TimeProvider.System,
                out var audit,
                out var auditError))
        {
            context.Stderr.WriteLine($"keypaste: {auditError}");
            return CliApp.ExitInternalError;
        }

        using (audit)
        {
            if (audit.TightenedPermissions)
            {
                context.Stderr.WriteLine($"keypaste: tightened permissions on {audit.Path} to owner-only.");
            }

            var retired = KeypasteHome.PolicyPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));
            var caps = new AccessCapsSource(KeypasteHome.ClientsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)));

            return VaultSession.OpenHeld(
                vaultPath,
                line,
                context,
                OwnerKind.TerminalAgent,
                (claim, vault) => Serve(vault, claim, audit, pipeName, limits, caps, File.Exists(retired) ? retired : null, Reach(vaultPath, pipeName, home, context), context));
        }
    }

    /// <summary>
    /// What <c>keypaste mcp policy</c> must be told to reach this agent: its vault when that is not the
    /// chosen one, and its pipe when that is not the one its vault's owner listens on unasked.
    /// </summary>
    private static (string? Vault, string? Approver) Reach(string vaultPath, string pipeName, string home, CliContext context)
    {
        var chosen = ChosenVault.Read(context.Environment.Get(KeypasteHome.EnvironmentVariable));
        var unasked = ApproverEndpoint.Resolve(null, null, VaultIdentity.Of(home, vaultPath));

        return (
            ChosenVault.Same(chosen, vaultPath) ? null : vaultPath,
            string.Equals(pipeName, unasked, StringComparison.Ordinal) ? null : pipeName);
    }

    private static int Serve(
        Vault vault,
        VaultClaim claim,
        AuditLog audit,
        string pipeName,
        ApprovalLimits limits,
        AccessCapsSource caps,
        string? retiredPolicy,
        (string? Vault, string? Approver) reach,
        CliContext context)
    {
        var console = new AgentConsole(context.Stderr, context.Prompt.IsInteractive, context.ConsoleStyle);
        void Narrate(string line) => console.WriteLine($"keypaste: {line}");

        using var lifetime = new SessionLifetime();
        using var stop = new CancellationTokenSource();

        // The agent saves only what a person confirmed, or a narrowing, under the claim it holds, so no
        // save here can land under another owner (D-0317).
        AgentSave Save(Func<Vault, bool> edit)
        {
            try
            {
                if (!lifetime.IsLive)
                {
                    return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
                }

                if (!edit(vault))
                {
                    return AgentSave.Unchanged;
                }

                vault.Save();
                return AgentSave.Saved;
            }
            catch (VaultChangedOnDiskException)
            {
                return new AgentSave(
                    AgentSaveOutcome.ChangedOnDisk,
                    "another program changed the vault file since keypaste agent opened it; restart keypaste agent to read it again");
            }
            catch (VaultException ex)
            {
                return new AgentSave(AgentSaveOutcome.Failed, ex.Message);
            }
            catch (ObjectDisposedException)
            {
                return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
            }
        }

        Vault? Unlocked() => lifetime.IsLive ? vault : null;

        SessionServer server;

        try
        {
            // Binding happens here, so a name somebody else already holds is a startup failure
            // rather than a server that looks up and never accepts anything.
            server = SessionServer.Listen(pipeName, new SessionServerOptions
            {
                Vault = claim.Vault,
                Lifetime = lifetime,
                CurrentLifetime = () => lifetime,
                UnlockedFor = asked => ReferenceEquals(asked, lifetime) && asked.IsLive ? vault : null,
                Channel = new TerminalApprovalChannel(context.Prompt, console, limits.Window, TimeProvider.System) { Vault = reach.Vault, Approver = reach.Approver },
                Limits = limits,
                Settings = new VaultAgentSettings(Unlocked, Save, caps),
                Hourly = new HourlyLimit(TimeProvider.System),
                Audit = () => audit,
                WatchEdits = edited => vault.Edited += edited,
                UnwatchEdits = edited => vault.Edited -= edited,

                // `keypaste lock` ends the lifetime and stops the listener, the same way a signal does.
                LockNow = () => Stop(lifetime, stop),
                Narrate = Narrate,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            console.WriteLine($"keypaste: could not listen on '{pipeName}': {ex.Message}");
            console.WriteLine("keypaste: another keypaste process may be listening on that name.");
            return CliApp.ExitInternalError;
        }

        using (server)
        {
            // A signal ends the lifetime and then stops the listener, rather than ending the
            // process, so a waiting request is answered as locked, the vault is disposed and the
            // grants are zeroed on the way out instead of being abandoned mid-flight.
            var signals = LockOnSignals(lifetime, stop);

            try
            {
                var automatic = AutomaticAgents.Named(
                    new AgentAccessStore(vault).List(),
                    caps.TryCurrent(out var ceilings, out _) ? ceilings : AccessCaps.Empty,
                    TimeProvider.System.GetUtcNow());

                Announce(claim.Vault.Path, pipeName, lifetime.Id, limits, retiredPolicy, automatic, console);

                // Blocking on the listener is the command. There is no synchronization context in
                // a console app, so this is a wait rather than a deadlock waiting to happen.
                server.RunAsync(stop.Token).GetAwaiter().GetResult();
            }
            finally
            {
                foreach (var signal in signals)
                {
                    signal.Dispose();
                }
            }
        }

        console.WriteLine("keypaste: the agent has stopped. The vault is locked and every grant is gone.");
        return CliApp.ExitSuccess;
    }

    /// <summary>Ends the lifetime, then stops the listener; a stop that arrives after the agent has gone does nothing.</summary>
    private static void Stop(SessionLifetime lifetime, CancellationTokenSource stop)
    {
        lifetime.End();

        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }
    }

    private static List<PosixSignalRegistration> LockOnSignals(SessionLifetime lifetime, CancellationTokenSource stop)
    {
        List<PosixSignalRegistration> registrations = [];

        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    Stop(lifetime, stop);
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // One fewer route to the same lock is not an error.
            }
        }

        return registrations;
    }

    /// <summary>
    /// The lines a person reads before leaving this running, one of which changes meaning entirely
    /// when the vault sets an agent to Automatic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim changes when an agent is Automatic, because with one it is no longer true. Saying
    /// "nothing is released without you saying yes" while an agent's requests are released silently
    /// is the kind of small untruth this product cannot afford to print, so each such agent is named,
    /// with where.
    /// </para>
    /// <para>
    /// A <c>policy.toml</c> left from before agents' settings moved into the vault is named once, so a
    /// person who wrote rules there learns they no longer apply rather than wondering why they ask.
    /// </para>
    /// <para>
    /// <c>internal</c> so what it says is assertable. The command itself blocks on a pipe until
    /// Ctrl+C, so the only way to test what it tells a person is to call the part that tells them.
    /// </para>
    /// </remarks>
    internal static void Announce(
        string vaultPath,
        string pipeName,
        string session,
        ApprovalLimits limits,
        string? retiredPolicy,
        IReadOnlyList<string> automatic,
        AgentConsole console)
    {
        console.WriteLine($"keypaste: watching {vaultPath}");

        if (retiredPolicy is not null)
        {
            console.WriteLine(
                $"keypaste: {retiredPolicy} is no longer read: each agent's settings are kept in the vault. `keypaste mcp policy` shows them.");
        }

        if (automatic.Count > 0)
        {
            console.WriteLine($"keypaste: set to Automatic in the vault: {string.Join("; ", automatic)}");
        }

        console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"keypaste: listening on {pipeName} for session {session}, {limits.Window.TotalSeconds:0} seconds to answer, grants last at most {limits.MaximumTtlSeconds} seconds"));

        console.WriteLine(
            automatic.Count > 0
                ? "keypaste: nothing is released without you saying yes, except what an agent above is Automatic for. Press Ctrl+C to stop."
                : "keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.");
    }

    private static bool TryLimits(CommandLine line, CliContext context, out ApprovalLimits limits)
    {
        limits = ApprovalLimits.Default;

        if (!TrySeconds(
                line.Value(TimeoutOption),
                TimeoutOption,
                ApprovalLimits.MinimumWindowSeconds,
                ApprovalLimits.MaximumWindowSeconds,
                context,
                out var window))
        {
            return false;
        }

        if (!TrySeconds(line.Value(MaxTtlOption), MaxTtlOption, 1, ToolTtlCeiling, context, out var maxTtl))
        {
            return false;
        }

        limits = ApprovalLimits.Default with
        {
            Window = window is null ? ApprovalLimits.Default.Window : TimeSpan.FromSeconds(window.Value),
            MaximumTtlSeconds = maxTtl ?? ApprovalLimits.Default.MaximumTtlSeconds,
        };

        return true;
    }

    /// <summary>
    /// The ceiling the tool schema advertises. Kept in step by
    /// <c>ToolSchemasMatchTheCoreTests</c> rather than by hoping.
    /// </summary>
    internal const int ToolTtlCeiling = ApprovalLimits.MaximumRequestableTtlSeconds;

    private static bool TrySeconds(
        string? raw,
        string option,
        int minimum,
        int maximum,
        CliContext context,
        out int? value)
    {
        value = null;

        if (raw is null)
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            context.Stderr.WriteLine($"keypaste: --{option} must be a whole number of seconds between {minimum} and {maximum}");
            return false;
        }

        value = parsed;
        return true;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste agent [--vault <path>] [--keyfile <path>] [--approver <name>]");
        writer.WriteLine("                      [--approval-timeout <seconds>] [--max-ttl <seconds>]");
        writer.WriteLine();
        writer.WriteLine("Unlocks your vault and waits. When an AI agent asks keypaste mcp for a");
        writer.WriteLine("credential, the request appears here and nothing is released until you say yes.");
        writer.WriteLine("Leave this running in its own terminal; Ctrl+C locks the vault again.");
        writer.WriteLine();
        writer.WriteLine($"  --vault <path>             which vault to unlock, or set {VaultLocator.EnvironmentVariable}");
        writer.WriteLine($"  --keyfile <path>           the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}");
        writer.WriteLine($"  --approver <name>          which pipe to listen on, or set {ApproverEndpoint.EnvironmentVariable}");
        writer.WriteLine($"  --approval-timeout <secs>  how long you have to answer, {ApprovalLimits.MinimumWindowSeconds}-{ApprovalLimits.MaximumWindowSeconds}, default {ApprovalLimits.DefaultWindowSeconds}");
        writer.WriteLine($"  --max-ttl <secs>           how long an [h] grant lasts, default {ApprovalLimits.DefaultMaximumTtlSeconds}");
        writer.WriteLine();
        writer.WriteLine("Each agent's settings, kept in the vault, decide what it can see and how it is");
        writer.WriteLine("asked; an agent you set to Automatic is answered without asking you, and every");
        writer.WriteLine("release is recorded. `keypaste mcp policy` shows and changes them.");
        writer.WriteLine();
        writer.WriteLine("Your master password is typed here, never in a window an agent caused to appear.");
    }
}
