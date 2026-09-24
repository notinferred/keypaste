using Keypaste.Core.Approval;

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
    internal AppAuthority(AppVaultSession session, string? approverOverride, Func<IApprovalChannel> approvals)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        _host = new SessionHost(session, approverOverride, approvals);
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

    /// <summary>Quits: the session locks before its endpoint stops, so what an agent has waiting is answered as locked rather than dropped with the endpoint (D-0313).</summary>
    public void Dispose()
    {
        Session.Dispose();
        _host.Dispose();
    }
}
