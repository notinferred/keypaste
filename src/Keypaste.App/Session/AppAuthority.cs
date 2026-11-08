using Keypaste.Core.Approval;
using Keypaste.Core.Editing;

namespace Keypaste.App.Session;

/// <summary>
/// The app's session authority: the unlocked session and the endpoints it is served on, started,
/// held and ended together.
/// </summary>
/// <remarks>
/// <para>
/// Launch composes exactly this, and so does <c>Keypaste.AppDriver hold</c>, so the gates that drive
/// real agents against the app run what a person runs.
/// </para>
/// <para>
/// <see cref="Status"/> is read from the authority answering agents, never from whether a pipe of
/// the vault's name accepts a connection: another process can listen on a name, and a listener can
/// outlive the lifetime it answered for.
/// </para>
/// <para>
/// On macOS each unlock is also served on a launch endpoint of its own, which only the run Terminal
/// starts for a Run asks, so <see cref="LaunchFailure"/> and <see cref="IssueLaunch"/> do not depend on
/// what agents meet. On Windows and Linux the app starts a Run's terminal itself, and
/// <see cref="RecordRun"/> writes its History line in the same log first.
/// </para>
/// </remarks>
internal sealed class AppAuthority : IDisposable
{
    private readonly SessionHost _host;

    /// <param name="session">The session the app unlocks, which this now owns.</param>
    /// <param name="approverOverride">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="approvals">Where a person is asked, per unlock: launch passes <see cref="WindowApprovalChannel"/>.</param>
    /// <param name="requestLock">What <c>keypaste lock</c> runs: launch passes <see cref="RequestLock"/>; null refuses it.</param>
    /// <param name="unexpected">Keeps a report of an exception a catch-everything block caught at a site it names, or null.</param>
    internal AppAuthority(
        AppVaultSession session,
        string? approverOverride,
        Func<IApprovalChannel> approvals,
        Action? requestLock = null,
        Action<Exception, string>? unexpected = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        _host = new SessionHost(
            session,
            approverOverride,
            approvals,
            requestLock,
            servesLaunches: OperatingSystem.IsMacOS(),
            unexpected: unexpected is null ? null : exception => unexpected(exception, CrashReporting.OwnerSite));
    }

    /// <summary>What <c>keypaste lock</c> does to the app: locks the session as requested, started from the thread <paramref name="post"/> runs it on and run off it.</summary>
    /// <param name="session">The session to lock.</param>
    /// <param name="post">Runs an action on the UI thread; launch posts through the dispatcher.</param>
    /// <returns>The action the authority runs, answering once it has ended the session.</returns>
    internal static Action RequestLock(AppVaultSession session, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(post);

        return () => post(() => _ = session.LockAsync(VaultLockReason.Requested));
    }

    /// <summary>The session agents are answered from.</summary>
    internal AppVaultSession Session { get; }

    /// <summary>What agents configured for the vault meet here now.</summary>
    /// <remarks>
    /// Reading it is not activity. A session past its idle deadline reads as locked and locks, as an
    /// agent's request would find it.
    /// </remarks>
    internal AuthorityStatus Status
    {
        get
        {
            if (!Session.IsUnlocked)
            {
                return Session.HeldBy is { } holder ? new AuthorityStatus.HeldBy(holder) : new AuthorityStatus.Locked();
            }

            if (Session.Lifetime?.Id is not { } current)
            {
                return new AuthorityStatus.Locked();
            }

            if (_host.Serving is { } serving
                && string.Equals(serving.Session, current, StringComparison.Ordinal)
                && Session.Owner is { } owner)
            {
                return new AuthorityStatus.Serving(serving.Session, serving.Endpoint, owner);
            }

            return new AuthorityStatus.NotServing(_host.Failure ?? "keypaste is not listening for agents on this vault");
        }
    }

    /// <summary>Why the run Terminal starts for a Run on macOS could not ask this app for its set now, or null when it could or no vault is unlocked here.</summary>
    /// <remarks>Read from the authority answering the launch endpoint, as <see cref="Status"/> is from the one answering agents.</remarks>
    internal string? LaunchFailure
    {
        get
        {
            if (!Session.IsUnlocked || Session.Lifetime?.Id is not { } current)
            {
                return null;
            }

            return string.Equals(_host.Launching, current, StringComparison.Ordinal)
                ? null
                : _host.LaunchFailure ?? "keypaste is not listening for Terminal";
        }
    }

    /// <summary>Why the audit log the owner records a Run's request in does not open now, or null when it opens or no vault is unlocked here.</summary>
    /// <remarks>Tried through the owner's own opener, as the request will be, so a log mended since the last try needs no new unlock.</remarks>
    internal string? AuditFailure => _host.AuditFailure;

    /// <summary>Why the audit log did not open when a request last tried it, so every agent request is refused until it opens; null when it opened then, nothing has tried it this unlock, or no vault is unlocked here.</summary>
    /// <remarks>Reading it opens nothing, so Agents can say it every second and an unlock alone still creates no log.</remarks>
    internal string? LastAuditFailure => _host.LastAuditFailure;

    /// <summary>What the authority answering agents has waiting for a person and has granted, never a value.</summary>
    /// <remarks>Nothing while it answers no session: a lock ends the lifetime that held both.</remarks>
    /// <summary>Raised, on whichever thread answered it, once a terminal's edit is saved in the vault.</summary>
    internal event EventHandler<EditDone>? TerminalEdited
    {
        add => _host.TerminalEdited += value;
        remove => _host.TerminalEdited -= value;
    }

    internal ApproverActivity Activity => _host.Activity;

    /// <summary>The rules agents are decided by here, and why the file's are ignored when they are.</summary>
    /// <param name="rules">The rules, when this app answers agents and the file could be read.</param>
    /// <param name="ignored">Why the file's rules are ignored and the defaults hold, or null.</param>
    /// <returns>Whether they were read.</returns>
    internal bool TryRules([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Core.Agents.AgentRules? rules, out string? ignored) =>
        _host.TryRules(out rules, out ignored);

    /// <summary>An item a remembered answer names, as every prompt shows it, or null when no live item has that UUID.</summary>
    /// <param name="item">The UUID as hex.</param>
    /// <returns>The item's name now.</returns>
    internal string? ItemShown(string item) => _host.ItemShown(item);

    /// <summary>Changes this computer's agent rules, sealed with the vault's key and recorded.</summary>
    /// <param name="agent">The agent the change is about.</param>
    /// <param name="change">The new rules from those that hold now.</param>
    /// <returns>Whether it was made, and keypaste's words.</returns>
    internal Core.Ipc.RulesChangeReply ChangeRules(string agent, Func<Core.Agents.AgentRules, Core.Agents.AgentRules> change) => _host.ChangeRules(agent, change);

    /// <summary>Until when pairing a browser is armed, or null when it is not or nothing here answers browsers.</summary>
    internal DateTimeOffset? PairingArmedUntil => _host.PairingArmedUntil;

    /// <summary>Arms pairing a browser for two minutes and one pairing; only Settings' Pair a browser calls it.</summary>
    /// <returns>Whether this unlock pairs browsers and is now armed.</returns>
    internal bool ArmPairing() => _host.ArmPairing();

    /// <summary>Ends an arming before its two minutes are up.</summary>
    internal void DisarmPairing() => _host.DisarmPairing();

    /// <summary>The pairings the unlocked vault holds, with when each was last proven here.</summary>
    internal IReadOnlyList<Core.Ownership.PairingRow> Pairings() => _host.Pairings();

    /// <summary>Removes a pairing for the person, saving and recording it.</summary>
    /// <param name="id">The pairing's name.</param>
    /// <returns>Whether it was removed, and why not.</returns>
    internal Core.Ownership.PairingRemoval RemovePairing(string id) => _host.RemovePairing(id);

    /// <summary>The MCP clients attached to the live session, as they describe themselves (display only).</summary>
    internal IReadOnlyList<Core.Clients.ConnectedClient> Clients => _host.Clients;

    /// <summary>What the live session has released, newest first, never a value.</summary>
    internal IReadOnlyList<Core.Ownership.ReleaseSeen> Released => _host.Released;

    /// <summary>
    /// Brings the prompt waiting for the person forward, focused on Deny with its allows disarmed for another
    /// second, for Show the prompt on Agents. Called on the UI thread; in this process only, so no
    /// agent or pipe message can raise a prompt.
    /// </summary>
    /// <returns>Whether a prompt was waiting on screen.</returns>
    internal bool ShowPrompt() => _host.ShowPrompt();

    /// <summary>Whether a prompt waits for the person's answer on screen; called on the UI thread.</summary>
    internal bool IsAsking => _host.IsAsking;

    /// <summary>Issues the pass that lets the <c>keypaste run --session</c> Terminal starts through as this Run confirmation answered it.</summary>
    /// <param name="claim">The request the confirmation named, and the set it showed.</param>
    /// <param name="lifetimeId">The session the confirmation was answered in.</param>
    /// <returns>The pass and the launch endpoint that run asks, or null when no launch endpoint listens or that session has ended.</returns>
    internal (string Pass, string Endpoint)? IssueLaunch(LaunchClaim claim, string lifetimeId) => _host.IssueLaunch(claim, lifetimeId);

    /// <summary>Forgets a pass Terminal was never handed, so nothing can present it.</summary>
    /// <param name="pass">The pass <see cref="IssueLaunch"/> returned.</param>
    internal void WithdrawLaunch(string pass) => _host.WithdrawLaunch(pass);

    /// <summary>Writes the History line of a Run the app starts itself on Windows and Linux, before the terminal starts.</summary>
    /// <param name="set">The set the confirmation released, read again after the person answered.</param>
    /// <param name="lifetimeId">The session the confirmation was answered in.</param>
    /// <returns>Whether the line was written; a Run whose line was not starts nothing (T-6).</returns>
    internal bool RecordRun(Core.EnvResolved set, string lifetimeId) => _host.RecordRun(set, lifetimeId);

    /// <summary>Quits: the session locks before its endpoint stops, so what an agent has waiting is answered as locked rather than dropped with the endpoint.</summary>
    public void Dispose()
    {
        Session.Dispose();
        _host.Dispose();
    }

    /// <summary>Quits as <see cref="Dispose"/> does, on a thread of its own, so a window awaiting it keeps painting while the endpoint stops.</summary>
    /// <returns>Completes once the endpoint has stopped.</returns>
    internal Task QuitAsync() => AppVaultSession.OnItsOwnThread(Dispose);
}
