using System.Collections.Concurrent;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>
/// What a vault's owner answers its endpoint with: a request is considered only when it comes from
/// a connection attached to the session now holding the vault it names (D-0310).
/// </summary>
/// <remarks>
/// <para>
/// The session is read on every operation rather than remembered at attachment, so a request that
/// reaches the owner after a lock, or after a later unlock, is refused instead of being answered
/// from whatever the vault holds by then.
/// </para>
/// <para>
/// Reaching the endpoint at all is what the operating system authenticates (THREATS.md T-10). The
/// session identifier is not a secret; it says which unlocked lifetime a request belongs to.
/// </para>
/// </remarks>
public sealed class SessionAuthority : IApproverHandler
{
    private readonly VaultIdentity _vault;
    private readonly Func<string?> _session;
    private readonly ApproverHandler _inner;
    private readonly ConcurrentDictionary<string, Attachment> _attached = new(StringComparer.Ordinal);

    /// <summary>Builds the authority for one owned vault.</summary>
    /// <param name="vault">The vault this process holds.</param>
    /// <param name="session">The current session, or null while the vault is locked.</param>
    /// <param name="inner">What decides a request once it belongs to the current session.</param>
    public SessionAuthority(VaultIdentity vault, Func<string?> session, ApproverHandler inner)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(inner);

        _vault = vault;
        _session = session;
        _inner = inner;
    }

    /// <inheritdoc/>
    public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        _attached.TryRemove(connectionId, out _);

        if (!_vault.Names(request.Vault))
        {
            return ValueTask.FromResult(
                AttachReply.Refused(AuditMethod.NoSession, "the keypaste process that answered holds a different vault"));
        }

        if (_session() is not { } session)
        {
            return ValueTask.FromResult(AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked"));
        }

        _attached[connectionId] = new Attachment(request.Vault, session);

        return ValueTask.FromResult(AttachReply.To(session));
    }

    /// <inheritdoc/>
    public async ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Refusal(request.Vault, request.Session, connectionId) is { } refusal)
        {
            return new NamesReply(false, [], refusal.Reason, true);
        }

        var reply = await _inner.ListAsync(request, connectionId, cancellationToken).ConfigureAwait(false);

        return reply with { Session = request.Session };
    }

    /// <inheritdoc/>
    public async ValueTask<CredentialReply> RequestAsync(
        CredentialRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Refusal(request.Vault, request.Session, connectionId) is { } refusal)
        {
            return new CredentialReply
            {
                Decision = AuditDecision.Denied,
                Method = refusal.Method,
                Reason = refusal.Reason,
            };
        }

        var reply = await _inner.RequestAsync(request, connectionId, cancellationToken).ConfigureAwait(false);

        return reply with { Session = request.Session };
    }

    /// <inheritdoc/>
    public void Disconnected(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        _attached.TryRemove(connectionId, out _);
        _inner.Disconnected(connectionId);
    }

    private (AuditMethod Method, string Reason)? Refusal(string vault, string session, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        var current = _session();

        if (current is null)
        {
            return (AuditMethod.VaultLocked, "the vault is locked");
        }

        if (!_attached.TryGetValue(connectionId, out var attachment))
        {
            return (AuditMethod.NoSession, "the request came from a connection attached to no session");
        }

        if (!string.Equals(attachment.Vault, vault, StringComparison.Ordinal))
        {
            return (AuditMethod.NoSession, "the request named a different vault from its attachment");
        }

        if (!string.Equals(attachment.Session, session, StringComparison.Ordinal)
            || !string.Equals(session, current, StringComparison.Ordinal))
        {
            return (AuditMethod.NoSession, "the request belongs to a session that has ended");
        }

        return null;
    }

    private readonly record struct Attachment(string Vault, string Session);
}
