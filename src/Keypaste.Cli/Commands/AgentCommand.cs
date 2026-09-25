using System.Globalization;
using System.Runtime.InteropServices;
using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste agent</c> — holds the unlocked vault and asks you about every credential request.
/// </summary>
/// <remarks>
/// <para>
/// The process that makes docs/PRODUCT.md law 3.2 real. It is started by a human, in a terminal a human
/// opened, and the master password is typed there in response to a command they typed — so nothing
/// an agent does can raise a password prompt. That property is the whole reason the approval flow
/// is not built into <c>keypaste-mcp</c> (DECISIONS.md D-0023).
/// </para>
/// <para>
/// <b>Deliberately not a daemon.</b> No service, no launch agent, no PID file, no starting itself
/// on demand. It runs in the foreground, says what it is doing, and stops when you stop it — which
/// is also the honest answer to "is anything able to act as me right now?".
/// </para>
/// <para>
/// <b>It holds the vault's claim for as long as it runs</b>, taken before the password is asked
/// for, so a vault the desktop app or another agent already holds is refused by name and never
/// opened (D-0309).
/// </para>
/// <para>
/// <b>It writes no audit lines.</b> <c>keypaste-mcp</c> is the only process that appends to the
/// log, so there is one writer, one key order and one schema (DECISIONS.md D-0020). What this
/// process prints is for the person watching it, not the record.
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
    internal const string PolicyOption = "policy";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(ApproverOption, TakesValue: true),
        new(TimeoutOption, TakesValue: true),
        new(MaxTtlOption, TakesValue: true),
        new(PolicyOption, TakesValue: true),
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

        // Loaded before the vault is opened, so what the policy file says cannot depend on anything
        // the vault did; reported later in Announce, which runs inside VaultSession.Open. Nothing
        // here is fatal: every failure means no rules, so every request is shown to a person.
        var policy = PolicyLoader.Load(
            line.Value(PolicyOption)
            ?? KeypasteHome.PolicyPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)));

        if (!VaultClaim.TryAcquire(home, vaultPath, OwnerKind.TerminalAgent, out var claim, out var refusal))
        {
            context.Stderr.WriteLine($"keypaste: {refusal}");
            return CliApp.ExitInternalError;
        }

        using (claim)
        {
            return VaultSession.Open(vaultPath, line, context, vault => Serve(vault, claim, pipeName, limits, policy, context));
        }
    }

    private static int Serve(
        Vault vault,
        VaultClaim claim,
        string pipeName,
        ApprovalLimits limits,
        PolicyLoad policy,
        CliContext context)
    {
        using var lifetime = new SessionLifetime();
        using var grants = new GrantCache(TimeProvider.System);
        lifetime.Own(grants);
        using var gate = new ApprovalGate(
            new TerminalApprovalChannel(context.Prompt, context.Stderr),
            TimeProvider.System,
            limits);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => vault),
            new VaultEntryNameLister(() => vault),
            gate,
            grants,
            new PolicyGate(policy.Rules, TimeProvider.System),
            line => context.Stderr.WriteLine($"keypaste: {line}"));

        using var stop = new CancellationTokenSource();

        // `keypaste lock` ends the lifetime and stops the listener, the same way a signal does.
        var authority = new SessionAuthority(
            claim.Vault,
            () => lifetime,
            handler,
            new SessionEnvironments(gate, asked => ReferenceEquals(asked, lifetime) && asked.IsLive ? vault : null, TimeProvider.System),
            lockNow: () => Stop(lifetime, stop));

        ApproverListener? listener = null;

        try
        {
            try
            {
                // Binding happens here, so a name somebody else already holds is a startup failure
                // rather than a server that looks up and never accepts anything.
                listener = new ApproverListener(pipeName, authority);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Stderr.WriteLine($"keypaste: could not listen on '{pipeName}': {ex.Message}");
                context.Stderr.WriteLine("keypaste: another keypaste process may be listening on that name.");
                return CliApp.ExitInternalError;
            }

            // A signal ends the lifetime and then stops the listener, rather than ending the
            // process, so a waiting request is answered as locked, the vault is disposed and the
            // grants are zeroed on the way out instead of being abandoned mid-flight.
            var signals = LockOnSignals(lifetime, stop);

            try
            {
                Announce(claim.Vault.Path, pipeName, lifetime.Id, limits, policy, context);

                // Blocking on the listener is the command. There is no synchronization context in
                // a console app, so this is a wait rather than a deadlock waiting to happen.
                listener.RunAsync(stop.Token).GetAwaiter().GetResult();
            }
            finally
            {
                foreach (var signal in signals)
                {
                    signal.Dispose();
                }
            }
        }
        finally
        {
            listener?.Dispose();
        }

        context.Stderr.WriteLine("keypaste: the agent has stopped. The vault is locked and every grant is gone.");
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
    /// The four lines a person reads before leaving this running, one of which changes meaning
    /// entirely depending on whether a policy is in force.
    /// </summary>
    /// <remarks>
    /// All six states a policy file can be in — absent, empty, in force, malformed, unreadable, too
    /// permissive — reach an agent as the same thing, which is required: telling them apart would
    /// let a request work out whether the human has a policy at all. They are distinct <em>here</em>,
    /// because "I wrote a rule and it is not working" and "I have no rules" need different next
    /// steps, and a rejected file says so twice as loudly as the rest.
    /// </remarks>
    /// <remarks>
    /// <c>internal</c> so the six states are assertable. The command itself blocks on a pipe until
    /// Ctrl+C, so the only way to test what it tells a person is to call the part that tells them.
    /// </remarks>
    internal static void Announce(
        string vaultPath,
        string pipeName,
        string session,
        ApprovalLimits limits,
        PolicyLoad policy,
        CliContext context)
    {
        context.Stderr.WriteLine($"keypaste: watching {vaultPath}");

        if (policy.Status == PolicyStatus.Rejected)
        {
            context.Stderr.WriteLine($"keypaste: policy: {policy.Reason}");
            context.Stderr.WriteLine(
                "keypaste: policy: every request will be shown to you. Fix it and restart, or run `keypaste policy ls`.");
        }
        else if (policy.HasRules)
        {
            context.Stderr.WriteLine($"keypaste: policy: {policy.Reason}. `keypaste policy ls` shows them.");
        }
        else
        {
            context.Stderr.WriteLine($"keypaste: policy: {policy.Reason}.");
        }

        context.Stderr.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"keypaste: listening on {pipeName} for session {session}, {limits.Window.TotalSeconds:0} seconds to answer, grants last at most {limits.MaximumTtlSeconds} seconds"));

        // The claim changes when a rule is in force, because with one it is no longer true. Saying
        // "nothing is released without you saying yes" while a standing rule releases things
        // silently is the kind of small untruth this product cannot afford to print.
        context.Stderr.WriteLine(
            policy.HasRules
                ? "keypaste: nothing is released without you saying yes, unless a policy rule covers it. Press Ctrl+C to stop."
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
        writer.WriteLine("                      [--policy <path>]");
        writer.WriteLine();
        writer.WriteLine("Unlocks your vault and waits. When an AI agent asks keypaste-mcp for a");
        writer.WriteLine("credential, the request appears here and nothing is released until you say yes.");
        writer.WriteLine("Leave this running in its own terminal; Ctrl+C locks the vault again.");
        writer.WriteLine();
        writer.WriteLine($"  --vault <path>             which vault to unlock, or set {VaultLocator.EnvironmentVariable}");
        writer.WriteLine($"  --keyfile <path>           the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}");
        writer.WriteLine($"  --approver <name>          which pipe to listen on, or set {ApproverEndpoint.EnvironmentVariable}");
        writer.WriteLine($"  --approval-timeout <secs>  how long you have to answer, {ApprovalLimits.MinimumWindowSeconds}-{ApprovalLimits.MaximumWindowSeconds}, default {ApprovalLimits.DefaultWindowSeconds}");
        writer.WriteLine($"  --max-ttl <secs>           the longest grant to issue, default {ApprovalLimits.DefaultMaximumTtlSeconds}");
        writer.WriteLine($"  --policy <path>            standing rules, default ~/{KeypasteHome.DirectoryName}/{KeypasteHome.PolicyFileName}");
        writer.WriteLine();
        writer.WriteLine("A rule in the policy file releases a credential without asking. Anything wrong");
        writer.WriteLine("with that file means the whole of it is ignored and every request asks you.");
        writer.WriteLine();
        writer.WriteLine("Your master password is typed here, never in a window an agent caused to appear.");
    }
}
