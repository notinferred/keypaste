using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>Where a vault's owner records each request it answers, before the reply leaves.</summary>
/// <remarks>
/// The owner writes these lines itself: whoever asked is the untrusted side, which could skip them or
/// point them elsewhere (THREATS.md T-14, T-30, T-32). A release whose line cannot be written is refused
/// and nothing in it is sent, and a listing that names anything is a release here (T-6). A refusal is
/// answered whether its line was written or not, since it releases nothing, and its reply says which.
/// </remarks>
/// <param name="audit">Opens the owner's log, asked before each request is decided and again when it is answered; a null answer refuses the request.</param>
internal sealed class RequestRecorder(Func<AuditLog?> audit)
{
    /// <summary>Why a request is refused, before anybody is asked, while the owner's log will not open.</summary>
    internal const string Unopened = "the audit log could not be opened";

    private const string _unrecorded = "the audit log could not be written";

    /// <summary>Whether the log opens, checked before anybody is asked, so nobody answers a request this owner cannot record (T-6).</summary>
    internal bool Opens => audit() is not null;

    /// <summary>A listing once its line is written; one naming anything whose line cannot be written names nothing.</summary>
    /// <param name="reply">The reply as it will leave.</param>
    /// <param name="record">Its line.</param>
    /// <returns>The reply to send, saying whether it was recorded.</returns>
    internal NamesReply Recorded(NamesReply reply, AuditRecord record) =>
        TryAppend(record) ? reply with { Recorded = true }
        : reply.Names.Count == 0 ? reply with { Recorded = false }
        : new NamesReply(false, [], _unrecorded, true) { Session = reply.Session, Refusal = AuditMethod.Failed };

    /// <summary>A credential reply once its line is written; a release whose line cannot be written becomes a refusal.</summary>
    /// <param name="reply">The reply as it will leave.</param>
    /// <param name="record">Its line.</param>
    /// <returns>The reply to send, saying whether it was recorded.</returns>
    internal CredentialReply Recorded(CredentialReply reply, AuditRecord record) =>
        TryAppend(record) ? reply with { Recorded = true }
        : reply.Decision != AuditDecision.Granted ? reply with { Recorded = false }
        : new CredentialReply
        {
            Decision = AuditDecision.Denied,
            Method = AuditMethod.Failed,
            Reason = _unrecorded,
            Entry = reply.Entry,
            Session = reply.Session,
        };

    /// <summary>An agent's run reply once its line is written; a release whose line cannot be written becomes a refusal.</summary>
    /// <param name="reply">The reply as it will leave.</param>
    /// <param name="record">Its line.</param>
    /// <returns>The reply to send, saying whether it was recorded.</returns>
    internal RunReply Recorded(RunReply reply, AuditRecord record) =>
        TryAppend(record) ? reply with { Recorded = true }
        : reply.Set.Outcome != EnvOutcome.Resolved ? reply with { Recorded = false }
        : new RunReply(EnvResolved.Refused(reply.Set.Project, EnvOutcome.Unreadable, profile: reply.Set.Profile), AuditMethod.Failed, _unrecorded)
        {
            Entries = reply.Entries,
            Session = reply.Session,
        };

    /// <summary>A run's env reply once its line is written; a release whose line cannot be written becomes a refusal.</summary>
    /// <param name="reply">The reply as it will leave.</param>
    /// <param name="record">Its line.</param>
    /// <returns>The reply to send.</returns>
    internal EnvReply Recorded(EnvReply reply, AuditRecord record) =>
        TryAppend(record) || reply.Set.Outcome != EnvOutcome.Resolved
            ? reply
            : new EnvReply(EnvResolved.Refused(reply.Set.Project, EnvOutcome.Unreadable, profile: reply.Set.Profile), _unrecorded);

    private bool TryAppend(AuditRecord record)
    {
        try
        {
            return audit() is { } log && log.TryAppend(record, out _);
        }
        catch (ObjectDisposedException)
        {
            // The owner closed its log as the session ended.
            return false;
        }
    }
}
