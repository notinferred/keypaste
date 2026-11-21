using System.CommandLine;
using System.Globalization;
using System.Runtime.InteropServices;
using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste approve</c> — holds the unlocked vault and asks you about every credential request.
/// </summary>
/// <remarks>
/// <para>
/// The process that makes SECURITY.md law 2 real. It is started by a human, in a terminal a human
/// opened, and the master password is typed there in response to a command they typed — so nothing
/// an agent does can raise a password prompt. That property is the whole reason the approval flow
/// is not built into the bridge.
/// </para>
/// <para>
/// <b>Deliberately not a daemon.</b> No service, no launch agent, no PID file, no starting itself
/// on demand. It runs in the foreground, says what it is doing, and stops when you stop it — which
/// is also the honest answer to "is anything able to act as me right now?".
/// </para>
/// <para>
/// <b>It holds the vault's claim for as long as it runs</b>, taken before the password is asked
/// for, so a vault the desktop app or another <c>keypaste approve</c> already holds is refused by
/// name and never opened. Under that claim it applies each agent's settings from the vault
/// and saves a change to them that the person confirms in this terminal.
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
/// take, so a request waiting at the prompt is withdrawn and denied.
/// </para>
/// </remarks>
internal static class ApproveCommand
{
    /// <summary>The approver, as the line of a merge it saved records it.</summary>
    internal const string Surface = "keypaste approve";

    private static readonly Option<string> _timeout = Syntax.Value(
        "--approval-timeout",
        "seconds",
        $"how long you have to answer, {ApprovalLimits.MinimumWindowSeconds}-{ApprovalLimits.MaximumWindowSeconds}, {ApprovalLimits.DefaultWindowSeconds} by default");

    internal static CliVerb Command { get; } = new(
        "approve",
        "approve agents' requests in this terminal\n\n"
            + "unlocks your vault and waits. when an AI agent asks keypaste mcp for a credential, the request appears here "
            + "and nothing is released until you say yes; Ctrl+C locks the vault again. each agent's settings, kept in "
            + "the vault, decide what it can see and how it is asked, and every release is recorded; `keypaste clients` "
            + "shows and changes them. your master password is typed here, never in a window an agent caused to appear.",
        Execute)
    {
        CliOptions.Vault, CliOptions.Keyfile, CliOptions.Approver, _timeout,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

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
                line.GetValue(CliOptions.Approver),
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
            var rules = AgentRulesFile.For(home, VaultIdentity.Of(home, vaultPath));

            return VaultSession.OpenHeld(
                vaultPath,
                line,
                context,
                OwnerKind.TerminalAgent,
                (claim, vault) => Serve(vault, claim, audit, pipeName, limits, rules, File.Exists(retired) ? retired : null, context));
        }
    }

    private static int Serve(
        Vault vault,
        VaultClaim claim,
        AuditLog audit,
        string pipeName,
        ApprovalLimits limits,
        AgentRulesFile rulesFile,
        string? retiredPolicy,
        CliContext context)
    {
        var console = new AgentConsole(context.Stderr, context.Prompt.IsInteractive, context.ConsoleStyle);
        void Narrate(string line) => console.WriteLine($"keypaste: {line}");

        using var lifetime = new SessionLifetime();
        using var stop = new CancellationTokenSource();

        // The approver saves only what a person confirmed under the claim it holds, so no save here can
        // land under another owner.
        using var held = new HeldVault(vault, lifetime);

        var rules = new SealedAgentRules(rulesFile, () => held.Open, held.Save, AgentRulesFile.Shared(context.Environment.Get(KeypasteHome.EnvironmentVariable)));
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
                UnlockedFor = held.UnlockedFor,
                Channel = new TerminalApprovalChannel(context.Prompt, console, limits.Window, TimeProvider.System),
                Limits = limits,
                Rules = rules,
                Audit = () => audit,
                AuditPath = audit.Path,
                KeepLogDays = () => AppSettings.Load(KeypasteHome.SettingsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable))).KeepLogDays,

                // `keypaste lock` ends the lifetime and stops the listener, the same way a signal does.
                LockNow = () => Stop(lifetime, stop),
                Narrate = Narrate,

                // A terminal's edit, once the person allows it here, is saved under the same claim.
                WriteEdit = (asked, edit) => ReferenceEquals(asked, lifetime) ? held.Save(edit) : new AgentSave(AgentSaveOutcome.Locked, "the vault is locked"),

                // A browser pairs once the person here confirms the code it shows, for this computer.
                BrowserPairings = new OwnerPairings(BrowserPairings.In(KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable)))),
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
            held.MergeThrough(server.Authority, Surface);
            held.Merged += (_, merged) => Merged(merged, console, () => Stop(lifetime, stop));
            held.LeftBehind += (_, leftBehind) => console.WriteLine($"keypaste: warning: {leftBehind.Text}");
            held.Unflushed += (_, _) => console.WriteLine($"keypaste: warning: {Vault.UnflushedText}");
            var toldOf = new HashSet<string>(StringComparer.Ordinal);
            held.ConflictedCopies.Changed += (_, _) => ConflictedCopiesFound(held.ConflictedCopies.Found, toldOf, console);
            using var watch = new VaultFileWatch(held);

            // A signal ends the lifetime and then stops the listener, rather than ending the
            // process, so a waiting request is answered as locked, the vault is disposed and the
            // nothing is abandoned mid-flight.
            var signals = LockOnSignals(lifetime, stop);

            try
            {
                List<string> allowed = rules.TryRead(out var read, out _)
                    ? [.. FieldCategories.Ruled.Where(category => read.RuleFor(category) == CategoryRule.Allow).Select(FieldCategories.Shown)]
                    : [];

                Announce(claim.Vault.Path, pipeName, lifetime.Id, limits, retiredPolicy, allowed, console);

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

        console.WriteLine("keypaste: keypaste approve has stopped. The vault is locked.");
        return CliApp.ExitSuccess;
    }

    /// <summary>
    /// Tells the person what a merge of another program's save came to, by name and never by value, and locks once the
    /// password or key file unlocked with no longer opens the file.
    /// </summary>
    /// <param name="merged">What the merge came to.</param>
    /// <param name="console">The person's terminal.</param>
    /// <param name="lockNow">Ends the unlock and stops the listener.</param>
    internal static void Merged(VaultMergeSave merged, AgentConsole console, Action lockNow)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(lockNow);

        switch (merged.Outcome)
        {
            case VaultMergeSaveOutcome.Saved:
                console.WriteLine("keypaste: another program saved the vault; keypaste merged it with the changes made here and saved it. Agents are answered from the merged copy.");

                if (merged.LeftBehind is { } leftBehind)
                {
                    console.WriteLine($"keypaste: warning: {leftBehind.Text}");
                }

                if (merged.Unflushed)
                {
                    console.WriteLine($"keypaste: warning: {Vault.UnflushedText}");
                }

                break;
            case VaultMergeSaveOutcome.Updated:
                console.WriteLine("keypaste: another program saved the vault; agents are answered from what it saved.");
                break;
            case VaultMergeSaveOutcome.Unchanged or VaultMergeSaveOutcome.Locked:
                return;
            case VaultMergeSaveOutcome.KeyRefused:
                console.WriteLine($"keypaste: another program saved the vault, and {VaultMergeSave.Refusal(merged.Outcome, merged.Reason)}. Locking the vault; run keypaste approve again to unlock it.");
                lockNow();
                return;
            default:
                console.WriteLine($"keypaste: another program saved the vault, and keypaste could not merge it: {VaultMergeSave.Refusal(merged.Outcome, merged.Reason)}. Agents are refused until keypaste can merge it or you restart keypaste approve.");
                return;
        }

        if (merged.Report is not { } report)
        {
            return;
        }

        foreach (var conflict in report.Conflicts)
        {
            console.WriteLine(conflict.OpenCopyCurrent
                ? $"keypaste: both changed {ApprovalPrompt.Shown(conflict.Item)}; this copy's version is current and the other program's is in its history."
                : $"keypaste: both changed {ApprovalPrompt.Shown(conflict.Item)}; the other program's version is current and this copy's is in its history.");
        }

        foreach (var combined in report.Combined)
        {
            console.WriteLine($"keypaste: both changed {ApprovalPrompt.Shown(combined)}; each change is kept, and where both changed one field the newer value is current and the other in its history.");
        }

        foreach (var kept in report.Kept)
        {
            var where = kept.Recycled ? "It is in Trash with" : "keypaste kept it with";
            console.WriteLine(kept.DeletedInFile
                ? $"keypaste: the other program deleted {ApprovalPrompt.Shown(kept.Item)} after it was changed here. {where} that change."
                : $"keypaste: {ApprovalPrompt.Shown(kept.Item)} was deleted here after the other program changed it. {where} that change.");
        }
    }

    /// <summary>Ends the lifetime, then stops the listener; a stop that arrives after the approver has gone does nothing.</summary>
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
    /// when this computer's rules allow a category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim changes when a category is allowed, because with one it is no longer true. Saying
    /// "every value waits for you" while some are released silently is the kind of small untruth this
    /// product cannot afford to print, so each allowed category is named.
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
        IReadOnlyList<string> allowed,
        AgentConsole console)
    {
        console.WriteLine($"keypaste: watching {vaultPath}");

        if (retiredPolicy is not null)
        {
            console.WriteLine(
                $"keypaste: {retiredPolicy} is no longer read: agents' rules are kept on this computer. `keypaste clients` shows them.");
        }

        if (allowed.Count > 0)
        {
            console.WriteLine($"keypaste: released to every agent without asking, by this computer's rules: {string.Join(", ", allowed)}");
        }

        console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"keypaste: listening on {pipeName} for unlock {session}, {limits.Window.TotalSeconds:0} seconds to answer."));

        console.WriteLine(
            allowed.Count > 0
                ? "keypaste: every other value waits for you to say yes; agents see each item's URL, and its username unless Settings hides it. Press Ctrl+C to stop."
                : "keypaste: every value waits for you to say yes; agents see each item's URL, and its username unless Settings hides it. Press Ctrl+C to stop.");
    }

    /// <summary>Tells the person of each sync tool's conflicted copy beside the vault once, and where to merge it.</summary>
    /// <param name="found">The copies the owner found.</param>
    /// <param name="toldOf">The copies already told of, which this adds to.</param>
    /// <param name="console">The person's terminal.</param>
    /// <remarks>Merging writes the vault under its claim, which this command holds, so the person stops it before the app can take it.</remarks>
    internal static void ConflictedCopiesFound(IReadOnlyList<string> found, ISet<string> toldOf, AgentConsole console)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(toldOf);
        ArgumentNullException.ThrowIfNull(console);

        lock (toldOf)
        {
            foreach (var path in found.Where(toldOf.Add))
            {
                console.WriteLine(
                    $"keypaste: {DisplayTextSanitizer.Sanitize(Path.GetFileName(path)).Text}, beside the vault, is a sync tool's conflicted copy of it, whose changes are not in the vault. To merge it, stop keypaste approve and open the vault in the keypaste app, where Security offers it to review and merge.");
            }
        }
    }

    private static bool TryLimits(ParseResult line, CliContext context, out ApprovalLimits limits)
    {
        limits = ApprovalLimits.Default;

        if (!TrySeconds(
                line.GetValue(_timeout),
                _timeout.Name,
                ApprovalLimits.MinimumWindowSeconds,
                ApprovalLimits.MaximumWindowSeconds,
                context,
                out var window))
        {
            return false;
        }

        limits = ApprovalLimits.Default with
        {
            Window = window is null ? ApprovalLimits.Default.Window : TimeSpan.FromSeconds(window.Value),
        };

        return true;
    }

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
            context.Stderr.WriteLine($"keypaste: {option} must be a whole number of seconds between {minimum} and {maximum}");
            return false;
        }

        value = parsed;
        return true;
    }
}
