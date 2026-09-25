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
/// An env set for <c>keypaste run --session</c> is admitted the same way and resolved under the
/// lifetime the runner attached to, never a later one, with the person asked through the owner's
/// own gate (D-0341) unless a timed grant they gave the same run still covers the same names
/// (THREATS.md T-34).
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
    private readonly SessionEnvironments? _environments;
    private readonly ConcurrentDictionary<string, Attachment> _attached = new(StringComparer.Ordinal);

    /// <summary>Builds the authority for one owned vault.</summary>
    /// <param name="vault">The vault this process holds.</param>
    /// <param name="lifetime">The current unlocked lifetime, or null while the vault is locked.</param>
    /// <param name="inner">What decides a request once it belongs to the current session.</param>
    /// <param name="environments">What env sets are released with, or null to refuse every one.</param>
    public SessionAuthority(
        VaultIdentity vault,
        Func<SessionLifetime?> lifetime,
        ApproverHandler inner,
        SessionEnvironments? environments = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(inner);

        _vault = vault;
        _lifetime = lifetime;
        _inner = inner;
        _environments = environments;
    }

    /// <summary>The session a request attaching now would be answered under, or null while the vault is locked.</summary>
    /// <remarks>Read the way every request reads it, so a status built on it says what an agent would meet.</remarks>
    public string? Serving => Live()?.Id;

    /// <summary>What the current session has waiting for a person and has granted, or nothing while no session is live.</summary>
    public ApproverActivity Activity =>
        Live() is null
            ? ApproverActivity.None
            : _inner.Activity() with { EnvGrants = _environments?.Grants?.InForce() ?? [] };

    /// <summary>Ends one grant of the current session, so the next request it would have answered is asked again.</summary>
    /// <param name="key">The grant, as <see cref="Activity"/> listed it.</param>
    public void Revoke(GrantKey key)
    {
        if (Live() is not null)
        {
            _inner.Revoke(key);
        }
    }

    /// <summary>Ends one timed grant a person gave a repeated <c>keypaste run --session</c>.</summary>
    /// <param name="key">The grant, as <see cref="ApproverActivity.EnvGrants"/> listed it.</param>
    public void RevokeEnvGrant(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Live() is not null)
        {
            _environments?.Grants?.Revoke(key);
        }
    }

    /// <summary>Ends every grant of the current session, agents' and runs' alike.</summary>
    public void RevokeAll()
    {
        if (Live() is not null)
        {
            _inner.RevokeAll();
            _environments?.Grants?.RevokeAll();
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
    public async ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var admitted, out var refusal))
        {
            return Refused(
                request.Project,
                request.Profile,
                refusal.Method == AuditMethod.VaultLocked ? EnvOutcome.Locked : EnvOutcome.NoSession,
                refusal.Reason);
        }

        if (_environments is not { } environments)
        {
            return Refused(request.Project, request.Profile, EnvOutcome.NoSession, "the keypaste process holding this vault does not release env sets");
        }

        if (EnvReleasePrompt.Problem(request.Project, request.Command, request.Directory) is { } problem)
        {
            return Refused(request.Project, request.Profile, EnvOutcome.Invalid, problem);
        }

        if (!EnvProfileNames.IsValid(request.Profile, out var invalid))
        {
            return Refused(request.Project, request.Profile, EnvOutcome.Invalid, invalid);
        }

        foreach (var key in request.Keys ?? [])
        {
            if (!EnvConvention.IsValidKey(key, out invalid))
            {
                return Refused(request.Project, request.Profile, EnvOutcome.Invalid, invalid);
            }
        }

        // Every run is a new connection, so the cooldown names the request and not the connection:
        // a loop that asks again after a refusal is refused without a second prompt (T-11).
        var cooldownKey = string.Join('\0', [
            "env", request.Project, request.Profile, request.Directory, .. request.Command,
            "\u0001", .. (request.Keys ?? []).Order(StringComparer.Ordinal),
            "\u0002", .. request.FileLines ?? []]);
        var fileLines = (request.FileLines ?? []).Select(ShownLine).ToList();
        var answer = ApprovalAnswer.NoChannel;

        // Every release of a protected profile is asked about live: no timed grant is offered or used.
        var liveOnly = EnvProfileNames.IsProtected(request.Profile);
        var grantSeconds = liveOnly ? 0 : EnvGrantCache.GrantSeconds(environments.Gate.Limits);

        var resolver = new SessionEnvResolver(
            () => ReferenceEquals(Live(), admitted) ? admitted : null,
            environments.VaultFor,
            environments.Clock);

        var resolved = await resolver.ResolveAsync(
            request.Project,
            request.Profile,
            request.Keys,
            async (preview, withdrawn) =>
            {
                var prompt = EnvReleasePrompt.For(preview, request.Command, request.Directory) with { GrantSeconds = grantSeconds, FileLines = fileLines };

                // Names only: the resolver reads the set again after this, so a grant releases the
                // latest saved values, and a changed name list is asked about again.
                if (!liveOnly && environments.Grants?.TryUse(cooldownKey, preview.Keys, out var remaining) == true)
                {
                    environments.Narrate?.Invoke(
                        $"released {prompt.Project}/{prompt.Profile} to `{prompt.Command}` from a timed grant ({(int)remaining.TotalSeconds}s left)");
                    return true;
                }

                answer = await environments.Gate.AskAsync(cooldownKey, prompt, withdrawn).ConfigureAwait(false);

                // A withdrawn question is not a refusal: the resolver tells a lock from a hang-up.
                withdrawn.ThrowIfCancellationRequested();

                if (answer == ApprovalAnswer.Approved && grantSeconds > 0)
                {
                    environments.Grants?.Store(
                        cooldownKey, prompt.Project, prompt.Profile, prompt.Command, preview.Keys, TimeSpan.FromSeconds(grantSeconds));
                }

                return answer.Releases();
            },
            cancellationToken).ConfigureAwait(false);

        return new EnvReply(resolved, resolved.Outcome == EnvOutcome.Declined ? Declined(answer) : resolved.Refusal);
    }

    /// <inheritdoc/>
    public void Disconnected(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        _attached.TryRemove(connectionId, out _);
        _inner.Disconnected(connectionId);
    }

    private SessionLifetime? Live() => _lifetime() is { IsLive: true } lifetime ? lifetime : null;

    /// <summary>A runner's file line as a prompt shows it: one line, nothing that draws anything else.</summary>
    private static string ShownLine(string line) =>
        DisplayTextSanitizer.Sanitize(line.Replace('\n', '\0').Replace('\r', '\0').Replace('\t', '\0')).Text;

    private static EnvReply Refused(string project, string profile, EnvOutcome outcome, string reason) =>
        new(EnvResolved.Refused(project, outcome, profile: profile), reason);

    private static string Declined(ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => "the person asked said no",
        ApprovalAnswer.TimedOut => "nobody answered the prompt in time",
        ApprovalAnswer.Busy => "another request is waiting for an answer; ask again once it has one",
        ApprovalAnswer.Cooldown => "the same request was refused a moment ago; wait a minute before asking again",
        ApprovalAnswer.Cancelled => "the request was withdrawn before anybody answered",
        _ => "the prompt could not be shown, so nobody was asked",
    };

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
