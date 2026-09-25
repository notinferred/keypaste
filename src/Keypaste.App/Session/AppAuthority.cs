using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.App.Session;

/// <summary>
/// The app's session authority: the unlocked session and the endpoint it is served on, started,
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
        _host = new SessionHost(session, approverOverride, approvals, requestLock);
    }

    /// <summary>What <c>keypaste lock</c> does to the app: locks the session as requested, on the thread <paramref name="post"/> runs it on.</summary>
    /// <param name="session">The session to lock.</param>
    /// <param name="post">Runs an action on the UI thread; launch posts through the dispatcher.</param>
    /// <returns>The action the authority runs after its reply has left.</returns>
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

    /// <summary>Ends every grant given to a bridge started with this label, as a stricter policy for it does.</summary>
    /// <param name="label">The raw label.</param>
    internal void RevokeClient(string label) => _host.RevokeClient(label);

    /// <summary>Ends every grant this session has given.</summary>
    internal void RevokeAll() => _host.RevokeAll();

    /// <summary>The grants in force with the ids <c>keypaste grants</c> shows, never a value.</summary>
    internal IReadOnlyList<GrantSummary> Grants() => _host.Grants();

    /// <summary>Ends the grant with this id.</summary>
    /// <param name="id">The grant's id, from <see cref="Grants"/>.</param>
    /// <returns>Whether a grant with that id was in force.</returns>
    internal bool Revoke(string id) => _host.Revoke(id);

    /// <summary>Quits: the session locks before its endpoint stops, so what an agent has waiting is answered as locked rather than dropped with the endpoint (D-0313).</summary>
    public void Dispose()
    {
        Session.Dispose();
        _host.Dispose();
    }
}
