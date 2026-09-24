using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>
/// What a vault's owner answers its endpoint with: a request is considered only when it comes from
/// a connection attached to the session now holding the vault it names (D-0310), and is released
/// only while that session's lifetime is live (D-0313).
/// </summary>
/// <remarks>
/// <para>
/// The lifetime is read on every operation rather than remembered at attachment, so a request that
/// reaches the owner after a lock, or after a later unlock, is refused instead of being answered
/// from whatever the vault holds by then.
/// </para>
/// <para>
/// A request that was already being decided when the lock came is withdrawn by the lifetime's end
/// and answered as a lock denial. Its answer commits under the lock that ends the lifetime, so a
/// value either left before the lock or does not leave at all.
/// </para>
/// <para>
/// Reaching the endpoint at all is what the operating system authenticates (THREATS.md T-10). The
/// session identifier is not a secret; it says which unlocked lifetime a request belongs to.
/// </para>
/// </remarks>
public sealed class SessionAuthority : IApproverHandler
{
    private const string _lockedReason = "the vault is locked";

    private readonly VaultIdentity _vault;
    private readonly Func<SessionLifetime?> _lifetime;
    private readonly ApproverHandler _inner;
    private readonly ConcurrentDictionary<string, Attachment> _attached = new(StringComparer.Ordinal);

    /// <summary>Builds the authority for one owned vault.</summary>
    /// <param name="vault">The vault this process holds.</param>
    /// <param name="lifetime">The current unlocked lifetime, or null while the vault is locked.</param>
    /// <param name="inner">What decides a request once it belongs to the current session.</param>
    public SessionAuthority(VaultIdentity vault, Func<SessionLifetime?> lifetime, ApproverHandler inner)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(inner);

        _vault = vault;
        _lifetime = lifetime;
        _inner = inner;
    }

    /// <summary>The session a request attaching now would be answered under, or null while the vault is locked.</summary>
    /// <remarks>Read the way every request reads it, so a status built on it says what an agent would meet.</remarks>
    public string? Serving => Live()?.Id;

    /// <summary>What the current session has waiting for a person and has granted, or nothing while no session is live.</summary>
    public ApproverActivity Activity => Live() is null ? ApproverActivity.None : _inner.Activity();

    /// <summary>Ends one grant of the current session, so the next request it would have answered is asked again.</summary>
    /// <param name="key">The grant, as <see cref="Activity"/> listed it.</param>
    public void Revoke(GrantKey key)
    {
        if (Live() is not null)
        {
            _inner.Revoke(key);
        }
    }

    /// <summary>Ends every grant of the current session.</summary>
    public void RevokeAll()
    {
        if (Live() is not null)
        {
            _inner.RevokeAll();
        }
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

        if (Live() is not { } lifetime)
        {
            return ValueTask.FromResult(AttachReply.Refused(AuditMethod.VaultLocked, _lockedReason));
        }

        _attached[connectionId] = new Attachment(request.Vault, lifetime.Id);

        return ValueTask.FromResult(AttachReply.To(lifetime.Id));
    }

    /// <inheritdoc/>
    public async ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return new NamesReply(false, [], refusal.Reason, true);
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);

        var reply = await _inner.ListAsync(request, connectionId, withdrawn.Token).ConfigureAwait(false);

        if (!lifetime.TryCommit())
        {
            return new NamesReply(false, [], _lockedReason, true);
        }

        return reply with { Session = request.Session };
    }

    /// <inheritdoc/>
    public async ValueTask<CredentialReply> RequestAsync(
        CredentialRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return new CredentialReply
            {
                Decision = AuditDecision.Denied,
                Method = refusal.Method,
                Reason = refusal.Reason,
            };
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);

        var reply = await _inner.RequestAsync(request, connectionId, withdrawn.Token).ConfigureAwait(false);

        if (!lifetime.TryCommit() && (reply.Decision == AuditDecision.Granted || reply.Method == AuditMethod.Cancelled))
        {
            // The released string is dropped here, never sent: the lock came before the release.
            return new CredentialReply
            {
                Decision = AuditDecision.Denied,
                Method = AuditMethod.VaultLocked,
                Reason = reply.Decision == AuditDecision.Granted
                    ? "the vault was locked before this was released"
                    : "the vault was locked before anybody answered",
                Entry = reply.Entry,
                Session = request.Session,
            };
        }

        return reply with { Session = request.Session };
    }

    /// <inheritdoc/>
    public void Disconnected(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        _attached.TryRemove(connectionId, out _);
        _inner.Disconnected(connectionId);
    }

    private SessionLifetime? Live() => _lifetime() is { IsLive: true } lifetime ? lifetime : null;

    private bool TryAdmit(
        string vault,
        string session,
        string connectionId,
        [NotNullWhen(true)] out SessionLifetime? lifetime,
        out (AuditMethod Method, string Reason) refusal)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        lifetime = Live();
        refusal = default;

        if (lifetime is null)
        {
            refusal = (AuditMethod.VaultLocked, _lockedReason);
        }
        else if (!_attached.TryGetValue(connectionId, out var attachment))
        {
            refusal = (AuditMethod.NoSession, "the request came from a connection attached to no session");
        }
        else if (!string.Equals(attachment.Vault, vault, StringComparison.Ordinal))
        {
            refusal = (AuditMethod.NoSession, "the request named a different vault from its attachment");
        }
        else if (!string.Equals(attachment.Session, session, StringComparison.Ordinal)
            || !string.Equals(session, lifetime.Id, StringComparison.Ordinal))
        {
            refusal = (AuditMethod.NoSession, "the request belongs to a session that has ended");
        }
        else
        {
            return true;
        }

        lifetime = null;
        return false;
    }

    private readonly record struct Attachment(string Vault, string Session);
}
