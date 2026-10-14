using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.App.Session;

/// <summary>
/// The app's session authority: the unlocked session and the endpoints it is served on, started,
/// held and ended together (D-0309, D-0313).
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
/// what agents meet (D-0451).
/// </para>
/// </remarks>
internal sealed class AppAuthority : IDisposable
{
    private readonly SessionHost _host;

    /// <param name="session">The session the app unlocks, which this now owns.</param>
    /// <param name="approverOverride">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="approvals">Where a person is asked, per unlock: launch passes <see cref="WindowApprovalChannel"/> (D-0326).</param>
    /// <param name="requestLock">What <c>keypaste lock</c> runs: launch passes <see cref="RequestLock"/>; null refuses it.</param>
    internal AppAuthority(
        AppVaultSession session,
        string? approverOverride,
        Func<IApprovalChannel> approvals,
        Action? requestLock = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        _host = new SessionHost(session, approverOverride, approvals, requestLock, servesLaunches: OperatingSystem.IsMacOS());
    }

    /// <summary>What <c>keypaste lock</c> does to the app: locks the session as requested, on the thread <paramref name="post"/> runs it on.</summary>
    /// <param name="session">The session to lock.</param>
    /// <param name="post">Runs an action on the UI thread; launch posts through the dispatcher.</param>
    /// <returns>The action the authority runs, answering once it has ended the session.</returns>
    internal static Action RequestLock(AppVaultSession session, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(post);

        return () => post(() => session.Lock(VaultLockReason.Requested));
    }

    /// <summary>The session agents are answered from.</summary>
    internal AppVaultSession Session { get; }

    /// <summary>What agents configured for the vault meet here now.</summary>
    /// <remarks>
    /// Reading it is not activity. A session past its idle deadline reads as locked and locks, as an
    /// agent's request would find it (D-0314).
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

    /// <summary>What the authority answering agents has waiting for a person and has granted, never a value.</summary>
    /// <remarks>Nothing while it answers no session: a lock ends the lifetime that held both (D-0313).</remarks>
    internal ApproverActivity Activity => _host.Activity;

    /// <summary>Ends one grant, so the next request it would have answered is asked again.</summary>
    /// <param name="key">The grant, as <see cref="Activity"/> listed it.</param>
    internal void Revoke(GrantKey key) => _host.Revoke(key);

    /// <summary>The MCP clients attached to the live session, as they describe themselves (display only).</summary>
    internal IReadOnlyList<Core.Clients.ConnectedClient> Clients => _host.Clients;

    /// <summary>What the live session has released, newest first, never a value.</summary>
    internal IReadOnlyList<Core.Ownership.ReleaseSeen> Released => _host.Released;

    /// <summary>
    /// Brings the prompt waiting for the person forward, focused on Deny with its allows disarmed for another
    /// second, for Show the prompt on Agents (D-0456). Called on the UI thread; in this process only, so no
    /// agent or pipe message can raise a prompt.
    /// </summary>
    /// <returns>Whether a prompt was waiting on screen.</returns>
    internal bool ShowPrompt() => _host.ShowPrompt();

    /// <summary>Ends every grant given to a bridge started with this label, as a stricter ceiling for it does.</summary>
    /// <param name="label">The raw label.</param>
    internal void RevokeClient(string label) => _host.RevokeClient(label);

    /// <summary>What applies to an agent's requests now, or null when nothing here answers or its settings cannot be read.</summary>
    /// <param name="label">The bridge's raw label, or null.</param>
    /// <returns>What applies, or null.</returns>
    internal Core.Agents.EffectiveAccess? Access(string? label) => _host.Access(label);

    /// <summary>One agent's settings as the person's own screen shows them: what the vault says and what this computer holds back.</summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    /// <param name="settings">Its settings, when they could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>Whether they were read; never while nothing here answers agents.</returns>
    internal bool TrySettings(string agent, [NotNullWhen(true)] out Core.Agents.AgentSettingsState? settings, out string problem) =>
        _host.TrySettings(agent, out settings, out problem);

    /// <summary>
    /// Changes an agent's settings for the person at this app: at once when it gives the agent nothing
    /// more, otherwise once they confirm it in the prompt window, as any change that gives more is.
    /// </summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="cancellationToken">Cancelled when the screen stops waiting, which withdraws a confirmation on screen.</param>
    /// <returns>What became of it, recorded in the audit log before it returns.</returns>
    internal ValueTask<AccessChangeReply> ChangeAccessAsync(string agent, Core.Agents.AccessEdit edit, CancellationToken cancellationToken) =>
        _host.ChangeAccessAsync(agent, edit, cancellationToken);

    /// <summary>Resets this computer's ceilings, which keypaste cannot read, once the person confirms what each agent then gets; recorded as keypaste app.</summary>
    /// <param name="cancellationToken">Cancelled when the screen stops waiting, which withdraws the confirmation.</param>
    /// <returns>What became of it.</returns>
    internal ValueTask<AccessChangeReply> ResetCapsAsync(CancellationToken cancellationToken) =>
        _host.ResetCapsAsync(cancellationToken);

    /// <summary>Ends every grant this session has given.</summary>
    internal void RevokeAll() => _host.RevokeAll();

    /// <summary>The grants in force with the ids <c>keypaste grants</c> shows, never a value.</summary>
    internal IReadOnlyList<GrantSummary> Grants() => _host.Grants();

    /// <summary>Ends the grant with this id.</summary>
    /// <param name="id">The grant's id, from <see cref="Grants"/>.</param>
    /// <returns>Whether a grant with that id was in force.</returns>
    internal bool Revoke(string id) => _host.Revoke(id);

    /// <summary>Issues the pass that lets the <c>keypaste run --session</c> Terminal starts through as this Run confirmation answered it (D-0452).</summary>
    /// <param name="claim">The request the confirmation named, and the set it showed.</param>
    /// <param name="lifetimeId">The session the confirmation was answered in.</param>
    /// <returns>The pass and the launch endpoint that run asks, or null when no launch endpoint listens or that session has ended.</returns>
    internal (string Pass, string Endpoint)? IssueLaunch(LaunchClaim claim, string lifetimeId) => _host.IssueLaunch(claim, lifetimeId);

    /// <summary>Forgets a pass Terminal was never handed, so nothing can present it.</summary>
    /// <param name="pass">The pass <see cref="IssueLaunch"/> returned.</param>
    internal void WithdrawLaunch(string pass) => _host.WithdrawLaunch(pass);

    /// <summary>Quits: the session locks before its endpoint stops, so what an agent has waiting is answered as locked rather than dropped with the endpoint (D-0313).</summary>
    public void Dispose()
    {
        Session.Dispose();
        _host.Dispose();
    }
}
