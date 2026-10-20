using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Tokens;

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
/// (THREATS.md T-34). A request carrying the launch pass the app's own Run confirmation issued is
/// that person's answer: it is released unasked only when it is exactly the request the confirmation
/// named and the set still has the names, items and protection it showed (D-0452). The app's launch
/// endpoint hands this authority nothing else, and a request it reads without a pass is refused
/// unasked (D-0451).
/// </para>
/// <para>
/// An agent's <c>run</c> is refused unasked unless the agent's settings let it run commands, and is
/// otherwise resolved the same way, against what the agent can see, and asked about as the exact
/// program, command line, directory and variable names, unless a timed grant that person gave the same
/// command line on the same connection still covers the same names (D-0358). No mode releases a run.
/// The owner runs nothing: the bridge starts the command.
/// </para>
/// <para>
/// Every listing, credential, run, token and settings-change request it reads is recorded in this
/// owner's own log before it is answered, a request refused at admission, withdrawn or failed while
/// deciding included, and a release whose line cannot be written is refused (THREATS.md T-6). While
/// that log will not open, every request is refused before anybody is asked. Nothing the asker sends
/// moves that log, so a bridge configured to log elsewhere, or a program speaking the pipe directly, is
/// recorded all the same (T-14). Attaching, listing grants or settings and locking release nothing and
/// are not recorded.
/// </para>
/// <para>
/// Reaching the endpoint at all is what the operating system authenticates (THREATS.md T-10). The
/// session identifier is not a secret; it says which unlocked lifetime a request belongs to. What an
/// attaching bridge says about its client is display only (T-3).
/// </para>
/// </remarks>
public sealed class SessionAuthority : IApproverHandler
{
    private const string _lockedReason = "the vault is locked";
    private const string _unrecordedNarration = "the audit log could not be written, so nothing was sent";
    private const string _launchSpent = "the run you confirmed in the app expired or was already used";
    private const string _launchOnly = "this connection is only for a run confirmed in the keypaste app";

    private readonly VaultIdentity _vault;
    private readonly Func<SessionLifetime?> _lifetime;
    private readonly ApproverHandler _inner;
    private readonly RequestRecorder _recorder;
    private readonly SessionEnvironments? _environments;
    private readonly SessionEditor? _editor;
    private readonly Action? _lockNow;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Attachment> _attached = new(StringComparer.Ordinal);
    private readonly Lock _ledgerGate = new();
    private (SessionLifetime Lifetime, ReleaseLedger Ledger)? _ledger;

    /// <summary>Builds the authority for one owned vault.</summary>
    /// <param name="vault">The vault this process holds.</param>
    /// <param name="lifetime">The current unlocked lifetime, or null while the vault is locked.</param>
    /// <param name="inner">What decides a request once it belongs to the current session.</param>
    /// <param name="audit">
    /// Opens the log this owner records each request it answers in, asked as each one is decided and
    /// answered, so an owner that never sees one creates no log; a null answer refuses the request
    /// before anybody is asked.
    /// </param>
    /// <param name="environments">What env sets are released with, or null to refuse every one.</param>
    /// <param name="lockNow">What <c>keypaste lock</c> runs, which must end the session for the reply to say locked; null refuses it.</param>
    /// <param name="clock">What attachments and releases are timed on; null is the system clock.</param>
    /// <param name="editor">What a terminal's edit is put to the person and made with, or null to refuse every one.</param>
    public SessionAuthority(
        VaultIdentity vault,
        Func<SessionLifetime?> lifetime,
        ApproverHandler inner,
        Func<AuditLog?> audit,
        SessionEnvironments? environments = null,
        Action? lockNow = null,
        TimeProvider? clock = null,
        SessionEditor? editor = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(audit);

        _vault = vault;
        _lifetime = lifetime;
        _inner = inner;
        _recorder = new RequestRecorder(audit);
        _environments = environments;
        _editor = editor;
        _lockNow = lockNow;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The session a request attaching now would be answered under, or null while the vault is locked.</summary>
    /// <remarks>Read the way every request reads it, so a status built on it says what an agent would meet.</remarks>
    public string? Serving => Live()?.Id;

    /// <summary>What the current session has waiting for a person and has granted, or nothing while no session is live.</summary>
    public ApproverActivity Activity =>
        Live() is null
            ? ApproverActivity.None
            : _inner.Activity() with
            {
                EnvGrants = _environments?.Grants?.InForce() ?? [],
                WaitingRuns = _environments?.Gate.WaitingRun is { } run ? [run] : [],
                WaitingEnvs = _environments?.Gate.WaitingEnv is { } env ? [env] : [],
            };

    /// <summary>The bridges attached to the live session that said who their client is, the most recently active first.</summary>
    /// <remarks>Runners, <c>keypaste grants</c> and <c>keypaste lock</c> say nothing and are not listed.</remarks>
    public IReadOnlyList<ConnectedClient> Clients
    {
        get
        {
            if (Live() is not { } lifetime)
            {
                return [];
            }

            return
            [
                .. _attached
                    .Where(pair => pair.Value.Client is not null && string.Equals(pair.Value.Session, lifetime.Id, StringComparison.Ordinal))
                    .Select(pair => new ConnectedClient(
                        pair.Key,
                        pair.Value.Client!.Name,
                        pair.Value.Client.Version,
                        pair.Value.Client.Label,
                        pair.Value.AttachedAt,
                        pair.Value.LastRequestAt) { Exposure = pair.Value.Exposure })
                    .OrderByDescending(client => client.LastRequestAt)
                    .ThenBy(client => client.ConnectionId, StringComparer.Ordinal),
            ];
        }
    }

    /// <summary>What the live session has released, newest first; nothing while no session is live.</summary>
    public IReadOnlyList<ReleaseSeen> Released
    {
        get
        {
            lock (_ledgerGate)
            {
                return Live() is { } lifetime && _ledger is { } ledger && ReferenceEquals(ledger.Lifetime, lifetime)
                    ? ledger.Ledger.Recent()
                    : [];
            }
        }
    }

    /// <summary>Ends every grant given to a bridge started with this <c>--client-label</c>, credential and run alike.</summary>
    /// <param name="label">The raw label.</param>
    /// <remarks>What a stricter ceiling for that agent does to what it already holds, so the Agents list matches it.</remarks>
    public void RevokeClient(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (Live() is null)
        {
            return;
        }

        var shown = EntryNameSanitizer.Sanitize(label, ApprovalPrompt.MaximumClientLength).Text;

        foreach (var grant in _inner.Activity().Grants.Where(grant => string.Equals(grant.Approved.Label, shown, StringComparison.Ordinal)))
        {
            _inner.Revoke(grant.Key);
        }

        _environments?.Grants?.RevokeLabel(label);
    }

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

    /// <summary>Issues the pass that lets exactly one <c>keypaste run --session</c> request through as the app's Run confirmation answered it (D-0452).</summary>
    /// <param name="claim">The request the confirmation named, and the set it showed.</param>
    /// <param name="lifetimeId">The session the confirmation was answered in.</param>
    /// <returns>The pass, or null when that session is no longer the live one or this owner takes no passes.</returns>
    /// <exception cref="ArgumentException">The claim is one <see cref="TerminalLaunch.ClaimProblem"/> refuses.</exception>
    public string? IssueLaunch(LaunchClaim claim, string lifetimeId)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(lifetimeId);

        if (TerminalLaunch.ClaimProblem(claim) is { } problem)
        {
            throw new ArgumentException(problem, nameof(claim));
        }

        return Live() is { } lifetime && string.Equals(lifetime.Id, lifetimeId, StringComparison.Ordinal)
            ? _environments?.Launches?.Issue(claim, lifetime.Id)
            : null;
    }

    /// <summary>Forgets a pass the app could not hand to Terminal, so nothing can present it.</summary>
    /// <param name="pass">The pass <see cref="IssueLaunch"/> returned.</param>
    public void WithdrawLaunch(string pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        _environments?.Launches?.Withdraw(pass);
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

        static ValueTask<AttachReply> Answer(AttachReply reply) => ValueTask.FromResult(reply with { OwnerVersion = CoreInfo.Version });

        if (!_vault.Names(request.Vault))
        {
            return Answer(AttachReply.Refused(AuditMethod.NoSession, "the keypaste process that answered holds a different vault"));
        }

        if (Live() is not { } lifetime)
        {
            return Answer(AttachReply.Refused(AuditMethod.VaultLocked, _lockedReason));
        }

        var now = _clock.GetUtcNow();
        _attached[connectionId] = new Attachment(request.Vault, lifetime.Id, request.Client, now)
        {
            LastRequestAt = now,
            Exposure = request.Exposure ?? [],
        };

        return Answer(AttachReply.To(lifetime.Id));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A listing carries nothing of its own (T-4), so it is recorded under the client its connection
    /// attached as, and its line counts the names that cross the pipe.
    /// </remarks>
    public async ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        var client = _attached.TryGetValue(connectionId, out var attachment) && attachment.Client is { } attached
            ? AuditClient.Of(attached.Name, attached.Version, attached.Label)
            : AuditClient.Unknown;

        NamesReply Answer(NamesReply reply) => _recorder.Recorded(
            reply,
            reply.VaultUnlocked
                ? RequestRecords.Listed(client, reply.Names.Count, request.Exposure, reply.Session, VaultNamed(request.Vault))
                : RequestRecords.Listing(
                    client,
                    AuditDecision.Denied,
                    reply.Refusal ?? AuditMethod.VaultLocked,
                    reply.Reason,
                    request.Exposure,
                    reply.Session,
                    VaultNamed(request.Vault)));

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return Answer(new NamesReply(false, [], refusal.Reason, true) { Refusal = refusal.Method });
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);

        var reply = await Decided(
            token => _inner.ListAsync(request, connectionId, token),
            lifetime,
            (method, reason) => new NamesReply(false, [], reason, true) { Refusal = method },
            withdrawn.Token).ConfigureAwait(false);

        if (!lifetime.TryCommit())
        {
            reply = new NamesReply(false, [], _lockedReason, true) { Refusal = AuditMethod.VaultLocked };
        }

        return Answer(ApproverProtocol.Fitted(reply with { Session = request.Session, Recorded = true }));
    }

    /// <inheritdoc/>
    public async ValueTask<CredentialReply> RequestAsync(
        CredentialRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CredentialReply Answer(CredentialReply reply) => _recorder.Recorded(
            reply,
            RequestRecords.Credential(
                AuditClient.Of(request.ClientName, request.ClientVersion, request.ClientLabel),
                request.Entry,
                request.Field,
                request.TtlSeconds,
                request.Reason,
                reply.Decision,
                reply.Method,
                reply.Reason,
                reply.Entry,
                reply.TtlSeconds,
                request.Exposure,
                reply.Session,
                VaultNamed(request.Vault)));

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return Answer(CredentialRefused(refusal.Method, refusal.Reason));
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);
        Withdrawals.Add(lifetime.Ended);

        var reply = await Decided(
            token => _inner.RequestAsync(request, connectionId, token),
            lifetime,
            CredentialRefused,
            withdrawn.Token).ConfigureAwait(false);

        AlwaysForNotice? unmade = null;

        // Asked before the commit, so a lock while the person decides still withdraws the release, and
        // in the slot the release's prompt kept, which is given back whatever became of it.
        using (reply.Kept)
        {
            if (reply is { Decision: AuditDecision.Granted, AlwaysFor: { } scope } && request.ClientLabel is { } agent)
            {
                unmade = await AlwaysAsync(request, agent, scope, reply.Kept, lifetime, withdrawn.Token).ConfigureAwait(false);
            }
        }

        if (!lifetime.TryCommit() && (reply.Decision == AuditDecision.Granted || reply.Method == AuditMethod.Cancelled))
        {
            // The released string is dropped here, never sent: the lock came before the release.
            var locked = reply.Decision == AuditDecision.Granted
                ? "the vault was locked before this was released"
                : "the vault was locked before anybody answered";
            reply = CredentialRefused(AuditMethod.VaultLocked, locked) with { Entry = reply.Entry };
        }

        var delivered = Deliverable(reply with { Session = request.Session });
        var answered = Answer(delivered);
        var client = ClientName(request.ClientLabel, request.ClientName);

        if (answered is { Decision: AuditDecision.Granted, Entry: { } entry })
        {
            Record(lifetime, [entry], client, AuditLog.Wire(answered.Method));

            // Only now, so "allowed once" is said of a release that left.
            if (unmade is not null)
            {
                _inner.Tell(unmade);
            }
        }
        else if (delivered.Decision == AuditDecision.Granted)
        {
            _environments?.Narrate?.Invoke($"refused {delivered.Entry} for {client}: {_unrecordedNarration}");
        }

        return answered;
    }

    /// <summary>
    /// After a person chose Always for on a release: asks them to confirm making that project
    /// environment Automatic for the agent, as any change that gives an agent more is confirmed, and
    /// records the change on its own line.
    /// </summary>
    /// <returns>What to tell them once the release has left when the change did not take effect, otherwise null.</returns>
    /// <remarks>Whatever they answer, the release they already allowed stands as allowed once.</remarks>
    private async ValueTask<AlwaysForNotice?> AlwaysAsync(
        CredentialRequest request,
        string agent,
        ProjectScope scope,
        KeptSlot? kept,
        SessionLifetime lifetime,
        CancellationToken cancellationToken)
    {
        var client = AuditClient.Of(request.ClientName, request.ClientVersion, request.ClientLabel);

        var reply = await Decided(
            token => _inner.ChangeAccessAsync(agent, AccessEdit.Automatic(scope), AccessOrigin.Prompt, string.Empty, kept, token),
            lifetime,
            (method, reason) => new AccessChangeReply(AuditDecision.Denied, method, reason) { Unapplied = reason },
            cancellationToken).ConfigureAwait(false);

        _recorder.Recorded(
            reply,
            RequestRecords.AccessChange(client, string.Empty, reply.Decision, reply.Method, reply.Reason, request.Session, VaultNamed(request.Vault)));

        return AlwaysForNotice.For(agent, scope, reply);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reading settings releases nothing, so it is not recorded, as listing grants is not. The listing
    /// starts after the rows the request says it already has, so one too long for a frame is read in turns.
    /// </remarks>
    public ValueTask<AccessListReply> ListAccessAsync(AccessListRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out _, out var refusal))
        {
            return ValueTask.FromResult(new AccessListReply(false, [], true, refusal.Reason));
        }

        var listed = _inner.ListAccess();

        return ValueTask.FromResult(listed with { Agents = [.. listed.Agents.Skip(request.From)] });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Recorded under the surface that asked, with the agent the change is for as its label: a bridge's
    /// client when one attached, otherwise <c>keypaste mcp policy</c>, or no name for one that says an
    /// agent asked. Beyond that, the origin the request claims only chooses where a narrowing is kept
    /// and what the person is told it came from.
    /// </para>
    /// <para>
    /// A request that says an agent asked through its own tool is admitted only for the agent its
    /// connection attached as, so the person is never told an agent asked about another's settings.
    /// That is a check of the claim, not of the agent: any program can attach under any label (THREATS.md T-3).
    /// </para>
    /// <para>
    /// The log is asked for before anything is decided, so a change is never made that its line could
    /// not follow; one whose line then fails to append still says so in its reply.
    /// </para>
    /// </remarks>
    public async ValueTask<AccessChangeReply> ChangeAccessAsync(AccessChangeRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        var attached = _attached.TryGetValue(connectionId, out var attachment) ? attachment.Client : null;
        var client = attached is not null ? AuditClient.Of(attached.Name, attached.Version, request.Agent)
            : request.Origin == AccessOrigin.Agent ? AuditClient.Of(null, null, request.Agent)
            : AuditClient.Of("keypaste mcp policy", CoreInfo.Version, request.Agent);

        AccessChangeReply Answer(AccessChangeReply reply) => _recorder.Recorded(
            reply,
            RequestRecords.AccessChange(client, request.Reason, reply.Decision, reply.Method, reply.Reason, request.Session, VaultNamed(request.Vault)));

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return Answer(new AccessChangeReply(AuditDecision.Denied, refusal.Method, refusal.Reason));
        }

        if (request.Origin == AccessOrigin.Agent && !string.Equals(attached?.Label, request.Agent, StringComparison.Ordinal))
        {
            return Answer(new AccessChangeReply(
                AuditDecision.Denied,
                AuditMethod.InvalidRequest,
                "an agent may ask only about its own settings, and this connection did not attach as that agent"));
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);

        return Answer(await Decided(
            token => _inner.ChangeAccessAsync(request, token),
            lifetime,
            (method, reason) => new AccessChangeReply(AuditDecision.Denied, method, reason),
            withdrawn.Token).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Recorded under the command that asked, <c>keypaste &lt;verb&gt;</c>, naming only what the terminal
    /// typed (D-0464). The log is asked for before anything is decided, so an edit is never made that its
    /// line could not follow, and the question is withdrawn by a lock as a credential request's is.
    /// </remarks>
    public async ValueTask<EditReply> EditAsync(EditRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        var client = AuditClient.Of($"keypaste {request.Edit.Verb}", CoreInfo.Version, null);

        EditReply Answer(EditReply reply) => _recorder.Recorded(
            reply,
            RequestRecords.Edit(client, request.Edit, reply.Decision, reply.Method, request.Session, VaultNamed(request.Vault)));

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return Answer(new EditReply(AuditDecision.Denied, refusal.Method, refusal.Reason));
        }

        if (_editor is null)
        {
            return Answer(new EditReply(AuditDecision.Denied, AuditMethod.NoSession, "this keypaste process does not take changes from a terminal"));
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);
        Withdrawals.Add(lifetime.Ended);

        return Answer(await Decided(
            token => _editor.DecideAsync(request, lifetime, token),
            lifetime,
            (method, reason) => new EditReply(AuditDecision.Denied, method, reason),
            withdrawn.Token).ConfigureAwait(false));
    }

    /// <summary>
    /// Changes an agent's settings for the person at this owner's own screen: at once when the change
    /// gives the agent nothing more, otherwise only once they confirm it, as a terminal's change is.
    /// </summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="surface">What the line records the screen as.</param>
    /// <param name="cancellationToken">Cancelled when the screen stops waiting, which withdraws a confirmation on screen.</param>
    /// <returns>What became of it, recorded on its own line before it returns.</returns>
    /// <remarks>
    /// Kept to the live session as a request from a pipe is: a locked owner changes nothing, and a lock
    /// while the person decides withdraws the confirmation.
    /// </remarks>
    public async ValueTask<AccessChangeReply> ChangeAccessAsync(string agent, AccessEdit edit, string surface, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(surface);

        var lifetime = Live();
        var client = AuditClient.Of(surface, CoreInfo.Version, agent);

        AccessChangeReply Answer(AccessChangeReply reply) => _recorder.Recorded(
            reply,
            RequestRecords.AccessChange(client, string.Empty, reply.Decision, reply.Method, reply.Reason, lifetime?.Id, _vault.Key));

        if (lifetime is null)
        {
            return Answer(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _lockedReason));
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);

        return Answer(await Decided(
            token => _inner.ChangeAccessAsync(agent, edit, AccessOrigin.Settings, string.Empty, token),
            lifetime,
            (method, reason) => new AccessChangeReply(AuditDecision.Denied, method, reason),
            withdrawn.Token).ConfigureAwait(false));
    }

    /// <summary>
    /// Resets this computer's ceilings for the person at this owner's own screen, when <c>clients.toml</c>
    /// holds what keypaste cannot parse: only once they confirm what each agent then gets.
    /// </summary>
    /// <param name="surface">What the line records the screen as.</param>
    /// <param name="cancellationToken">Cancelled when the screen stops waiting, which withdraws a confirmation on screen.</param>
    /// <returns>What became of it, recorded on its own line, under <c>*</c> for every agent, before it returns.</returns>
    public async ValueTask<AccessChangeReply> ResetCapsAsync(string surface, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var lifetime = Live();
        var client = AuditClient.Of(surface, CoreInfo.Version, AgentNames.Any);

        AccessChangeReply Answer(AccessChangeReply reply) => _recorder.Recorded(
            reply,
            RequestRecords.AccessChange(client, string.Empty, reply.Decision, reply.Method, reply.Reason, lifetime?.Id, _vault.Key));

        if (lifetime is null)
        {
            return Answer(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _lockedReason));
        }

        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);

        return Answer(await Decided(
            token => _inner.ResetCapsAsync(token),
            lifetime,
            (method, reason) => new AccessChangeReply(AuditDecision.Denied, method, reason),
            withdrawn.Token).ConfigureAwait(false));
    }

    /// <summary>One agent's settings as the person's own screen shows them.</summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="settings">Its settings, when a session is live and they could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>Whether they were read.</returns>
    public bool TrySettings(string agent, [NotNullWhen(true)] out AgentSettingsState? settings, out string problem)
    {
        if (Live() is null)
        {
            settings = null;
            problem = _lockedReason;
            return false;
        }

        return _inner.TrySettings(agent, out settings, out problem);
    }

    /// <summary>What applies to an agent's requests now, or null when its settings cannot be read, which refuses them all.</summary>
    /// <param name="label">The bridge's raw <c>--client-label</c>, or null.</param>
    /// <returns>What applies, or null.</returns>
    public EffectiveAccess? Access(string? label) => _inner.TryAccess(label, out var access, out _) ? access : null;

    /// <inheritdoc/>
    public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken) =>
        ReleaseSetAsync(request, connectionId, launchOnly: false, cancellationToken);

    /// <summary>Decides one env request read on the app's launch endpoint, which only a run carrying a launch pass uses (D-0451).</summary>
    /// <param name="request">The project, and the command and directory the runner will start it in.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>For a request carrying a pass, what <see cref="ReleaseEnvAsync"/> answers it; for one without, a refusal given unasked and recorded.</returns>
    public ValueTask<EnvReply> ReleaseLaunchAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken) =>
        ReleaseSetAsync(request, connectionId, launchOnly: true, cancellationToken);

    private async ValueTask<EnvReply> ReleaseSetAsync(EnvRequest request, string connectionId, bool launchOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var admitted, out var refusal))
        {
            var unadmitted = Refused(request.Project, request.Profile, refusal);
            return _recorder.Recorded(
                unadmitted,
                RequestRecords.EnvRun(request.Project, request.Profile, refusal.Method, unadmitted, session: null, VaultNamed(request.Vault)));
        }

        var (method, decided, confirmedInApp) = await Decided(
            token => DecideEnvAsync(request, admitted, launchOnly, token),
            admitted,
            (how, reason) => (how, Refused(request.Project, request.Profile, OutcomeOf(how), reason), false),
            cancellationToken).ConfigureAwait(false);

        var reply = _recorder.Recorded(
            decided,
            RequestRecords.EnvRun(request.Project, request.Profile, method, decided, request.Session, _vault.Key, confirmedInApp));

        if (_environments is { } environments)
        {
            Settled(environments, admitted, reply.Set, GrantSummary.EnvClient, "run --session");
        }

        return reply;
    }

    private async ValueTask<(AuditMethod Method, EnvReply Reply, bool ConfirmedInApp)> DecideEnvAsync(
        EnvRequest request,
        SessionLifetime admitted,
        bool launchOnly,
        CancellationToken cancellationToken)
    {
        if (launchOnly && request.Launch is null)
        {
            return (AuditMethod.InvalidRequest, Refused(request.Project, request.Profile, EnvOutcome.Unauthorized, _launchOnly), false);
        }

        if (_environments is not { } environments)
        {
            return (
                AuditMethod.NoSession,
                Refused(request.Project, request.Profile, EnvOutcome.NoSession, "the keypaste process holding this vault does not release env sets"),
                false);
        }

        LaunchClaim? launch = null;

        // Spent on its first admitted presentation, before anything else is checked, so no pass is tried twice.
        if (request.Launch is { } pass && environments.Launches?.TryTake(pass, admitted.Id, out launch) != true)
        {
            return (AuditMethod.InvalidRequest, Refused(request.Project, request.Profile, EnvOutcome.Unauthorized, _launchSpent), false);
        }

        var confirmedInApp = launch is not null;

        // A request carrying a pass is never shown on a prompt, and Claims holds its command to the one IssueLaunch bounded.
        if (EnvReleasePrompt.Problem(request.Project, request.Command, request.Directory, boundCommandLine: launch is null) is { } problem)
        {
            return (AuditMethod.InvalidRequest, Refused(request.Project, request.Profile, EnvOutcome.Invalid, problem), confirmedInApp);
        }

        if (!EnvProfileNames.IsValid(request.Profile, out var invalid))
        {
            return (AuditMethod.InvalidRequest, Refused(request.Project, request.Profile, EnvOutcome.Invalid, invalid), confirmedInApp);
        }

        foreach (var key in request.Keys ?? [])
        {
            if (!EnvConvention.IsValidKey(key, out invalid))
            {
                return (AuditMethod.InvalidRequest, Refused(request.Project, request.Profile, EnvOutcome.Invalid, invalid), confirmedInApp);
            }
        }

        if (launch is not null && !Claims(launch, request))
        {
            return (AuditMethod.InvalidRequest, Refused(request.Project, request.Profile, EnvOutcome.Unauthorized, _launchSpent), confirmedInApp);
        }

        // Every run is a new connection, so the cooldown names the request and not the connection:
        // a loop that asks again after a refusal is refused without a second prompt (T-11).
        var cooldownKey = string.Join('\0', [
            "env", request.Project, request.Profile, request.Directory, .. request.Command,
            "\u0001", .. (request.Keys ?? []).Order(StringComparer.Ordinal),
            "\u0002", .. request.FileLines ?? []]);
        var fileLines = (request.FileLines ?? []).Select(ShownLine).ToList();
        var answer = ApprovalAnswer.NoChannel;
        var fromGrant = false;
        string? refused = null;
        var refusedOutcome = EnvOutcome.Invalid;

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
                // Checked before anybody is asked, so a person never approves a set its audit line cannot record.
                if (Unrecordable(preview) is { } unrecordable)
                {
                    refused = unrecordable;
                    return false;
                }

                // The person answered the app's card about exactly these names, items and protection, and nothing else.
                if (launch is not null)
                {
                    if (preview.SameAs(launch.Preview))
                    {
                        return true;
                    }

                    (refused, refusedOutcome) = (ChangedSince(launch), EnvOutcome.ChangedWhileAsked);
                    return false;
                }

                // A set holding a member of a protected environment is asked about live: no timed grant is offered or used.
                var grantSeconds = preview.RequiresLiveApproval ? 0 : EnvGrantCache.GrantSeconds(environments.Gate.Limits);
                var prompt = EnvReleasePrompt.For(preview, request.Command, request.Directory) with { GrantSeconds = grantSeconds, FileLines = fileLines };

                // Names only: the resolver reads the set again after this, so a grant releases the
                // latest saved values, and a changed name list is asked about again.
                if (!preview.RequiresLiveApproval && environments.Grants?.TryUse(cooldownKey, preview.Keys, out var remaining) == true)
                {
                    environments.Narrate?.Invoke(
                        $"released {prompt.Project}/{prompt.Profile} to `{prompt.Command}` under an allowance ({(int)remaining.TotalSeconds}s left)");
                    fromGrant = true;
                    return true;
                }

                answer = await environments.Gate.AskAsync(cooldownKey, prompt, withdrawn).ConfigureAwait(false);

                // A withdrawn question is not a refusal: the resolver tells a lock from a hang-up.
                withdrawn.ThrowIfCancellationRequested();

                if (answer == ApprovalAnswer.Approved && grantSeconds > 0)
                {
                    environments.Grants?.Store(
                        cooldownKey,
                        prompt.Project,
                        prompt.Profile,
                        prompt.Command,
                        preview.Keys,
                        TimeSpan.FromSeconds(grantSeconds),
                        entries: [.. preview.Sources.Select(source => source.Entry).Distinct()]);
                }

                return answer.Releases();
            },
            cancellationToken).ConfigureAwait(false);

        var reason = resolved.Outcome switch
        {
            EnvOutcome.Declined => Declined(answer),
            EnvOutcome.ChangedWhileAsked when launch is not null => ChangedSince(launch),
            _ => resolved.Refusal,
        };
        var delivered = refused is not null
            ? Refused(request.Project, request.Profile, refusedOutcome, refused)
            : Deliverable(new EnvReply(resolved, reason));

        // As the run path records them; an approval the runner hung up on before it was used is a cancellation.
        var method = delivered.Set.Outcome switch
        {
            EnvOutcome.Resolved => fromGrant ? AuditMethod.GrantCache : AuditMethod.Prompt,
            EnvOutcome.Declined => answer.Releases() ? AuditMethod.Cancelled : answer.ToAuditMethod(),
            _ => MethodOf(delivered.Set.Outcome),
        };

        return (method, delivered, confirmedInApp);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The order is the security property. Admitted, then the request's rules, then the exposure and
    /// the agent's settings, a run refused unresolved when they do not let it run commands, then
    /// resolved under the admitted lifetime; the person is asked about the names the set holds once
    /// what the agent can see, the name rule and the audit line's room are checked, unless a timed grant
    /// this connection holds for the same command line covers the same names. The set is read again
    /// after the answer, so an edit saved meanwhile is what leaves. A release whose names are not the
    /// ones the request named, or that one frame cannot carry, is refused before it is recorded.
    /// </para>
    /// <para>
    /// A missing project, a missing profile, an unusable set and one outside the exposure all come back
    /// as <see cref="AuditMethod.OutOfScope"/>, so a run cannot map the vault (THREATS.md T-4); the
    /// reason, which only the audit line keeps, says which. No mode releases a run: Automatic asks, and
    /// Ask every time offers no timed grant.
    /// </para>
    /// </remarks>
    public async ValueTask<RunReply> ReleaseRunAsync(RunRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        var asked = new RunArguments(
            request.Command,
            request.Directory,
            request.Project,
            request.Project is null ? null : request.Profile,
            request.Keys,
            request.References,
            request.Reason,
            RunRequestRules.DefaultTimeoutSeconds);

        RunReply Answer(RunReply reply) => _recorder.Recorded(
            reply,
            RequestRecords.Run(
                AuditClient.Of(request.ClientName, request.ClientVersion, request.ClientLabel),
                asked,
                request.Program,
                reply.Set.Outcome == EnvOutcome.Resolved ? AuditDecision.Granted : AuditDecision.Denied,
                reply.Method,
                reply.Reason,
                reply.GrantedSeconds,
                reply.Entries,
                request.Exposure,
                reply.Session,
                VaultNamed(request.Vault)));

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var admitted, out var refusal))
        {
            return Answer(RunRefused(request, refusal.Method == AuditMethod.VaultLocked ? EnvOutcome.Locked : EnvOutcome.NoSession, refusal.Method, refusal.Reason));
        }

        var client = ClientName(request.ClientLabel, request.ClientName);

        var decided = await Decided(
            token => DecideRunAsync(request, asked, connectionId, admitted, token),
            admitted,
            (method, reason) => RunRefused(request, OutcomeOf(method), method, reason),
            cancellationToken).ConfigureAwait(false);

        var delivered = Deliverable(asked, decided with { Session = request.Session });
        var answered = Answer(delivered);

        if (answered.Set.Outcome == EnvOutcome.Resolved)
        {
            Record(admitted, answered.Entries, client, "run");

            // Every run is narrated with its command, a grant-served one too: the grant approved a
            // command line, not what the files it runs do (T-35).
            var command = EnvReleasePrompt.CommandLine(request.Command);
            _environments?.Narrate?.Invoke(answered.Method == AuditMethod.GrantCache
                ? $"released {answered.Set.Variables.Count} variable(s) to {client} for `{command}` under an allowance ({answered.GrantedSeconds}s left)"
                : $"released {answered.Set.Variables.Count} variable(s) to {client} for `{command}` {(answered.GrantedSeconds > 0 ? $"for {answered.GrantedSeconds}s" : "once")}");
        }
        else if (delivered.Set.Outcome == EnvOutcome.Resolved)
        {
            _environments?.Narrate?.Invoke($"refused a run for {client}: {_unrecordedNarration}");
        }

        return answered;
    }

    private async ValueTask<RunReply> DecideRunAsync(
        RunRequest request,
        RunArguments arguments,
        string connectionId,
        SessionLifetime admitted,
        CancellationToken cancellationToken)
    {
        RunReply Refuse(EnvOutcome outcome, AuditMethod method, string reason, IReadOnlyList<string>? entries = null) =>
            RunRefused(request, outcome, method, reason, entries);

        if (_environments is not { } environments)
        {
            return Refuse(EnvOutcome.NoSession, AuditMethod.NoSession, "the keypaste process holding this vault does not run commands for agents");
        }

        if ((RunRequestRules.Check(arguments) ?? RunRequestRules.CheckProgram(request.Program, request.Command)) is { } problem)
        {
            return Refuse(EnvOutcome.Invalid, AuditMethod.InvalidRequest, $"the request's {problem.Argument} {problem.Rule}");
        }

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out var globError))
        {
            return Refuse(EnvOutcome.Invalid, AuditMethod.Failed, $"this bridge's --expose is not usable: {globError}");
        }

        if (!_inner.TryAccess(request.ClientLabel, out var access, out var settingsProblem))
        {
            environments.Narrate?.Invoke($"refused a run: agent settings are not usable: {settingsProblem}");
            return Refuse(EnvOutcome.Unreadable, AuditMethod.Failed, $"agent settings are not usable: {settingsProblem}");
        }

        // Before anything is resolved, so an agent that may not run commands learns nothing of the vault from a run.
        if (!access.Run)
        {
            environments.Narrate?.Invoke($"refused a run for {ClientName(request.ClientLabel, request.ClientName)}: its settings do not let it run commands");
            return Refuse(EnvOutcome.Unauthorized, AuditMethod.RunOff, "this agent's settings do not let it run commands");
        }

        EnvReferenceDocument? document = null;

        if (request.References is { } references)
        {
            List<ReferenceLine> lines = [];

            foreach (var (reference, index) in references.Select((reference, index) => (reference, index)))
            {
                if (reference.Parsed is EntryReference { Entry: var named } && ReservedGroups.IsReserved(named.GroupPath))
                {
                    return Refuse(EnvOutcome.Unauthorized, AuditMethod.OutOfScope, "a reference names a group keypaste keeps for itself");
                }

                lines.Add(new ReferenceLine(reference.Name, reference.Parsed, null, index + 1));
            }

            document = new EnvReferenceDocument(lines, []);
        }

        var command = EnvReleasePrompt.CommandLine(request.Command);
        string[] mode = document is null
            ? ["set", request.Project!, request.Profile, .. (request.Keys ?? []).Order(StringComparer.Ordinal)]
            : ["refs", .. request.References!.Select(reference => reference.Name + "=" + reference.Reference)];
        var runKey = string.Join('\0', ["run", connectionId, request.Directory, request.Program, .. request.Command, "\u0001", .. mode]);

        var answer = ApprovalAnswer.NoChannel;
        var method = AuditMethod.Prompt;
        var granted = 0;
        IReadOnlyList<string> shown = [];
        string? refused = null;
        var refusedMethod = AuditMethod.InvalidRequest;

        async ValueTask<bool> Confirm(EnvPreview preview, CancellationToken withdrawn)
        {
            var entries = EntriesOf(preview);
            var perKey = entries.Select(entry => ApprovalPrompt.Shown(entry.Name)).ToList();
            List<string> named = [.. perKey.Distinct(StringComparer.Ordinal)];
            var recordable = AuditedLength(named) <= MaximumAgentRunEntriesBytes;

            // A reply carries only entries its line can hold, so a refusal of a larger set is still recorded.
            shown = recordable ? named : [];

            // What the agent can see first, so a set outside it is refused as out of scope whatever its size (T-4).
            if (entries.Any(entry => !EntryExposure.Permits(access.Reach(exposure, entry.Name, _inner.Tags(entry.Name)), entry.Field)))
            {
                (refused, refusedMethod) = ("a variable's item or field is outside what this agent can see through this bridge", AuditMethod.OutOfScope);
                return false;
            }

            if (RunRequestRules.NameProblem(preview.Keys) is { } names)
            {
                (refused, refusedMethod) = ($"the set's variables {names}", AuditMethod.InvalidRequest);
                return false;
            }

            if (!recordable)
            {
                (refused, refusedMethod) = ("the run names more items than an agent's run can carry back in one reply", AuditMethod.InvalidRequest);
                return false;
            }

            var now = environments.Clock.GetUtcNow();
            var onceOnly = preview.RequiresLiveApproval || entries.Any(entry => _inner.RequiresLiveApproval(entry.Name))
                ? OnceOnly.ProtectedProfile
                : entries.Any(entry => access.Item(_inner.Tags(entry.Name), now).Mode == AgentMode.Ask) ? OnceOnly.AskEveryTime : OnceOnly.None;
            var grantSeconds = onceOnly == OnceOnly.None ? EnvGrantCache.GrantSeconds(environments.Gate.Limits) : 0;

            // Names only: the resolver reads again after this, so a grant releases the latest saved
            // values, and a changed name list is asked about again.
            if (onceOnly == OnceOnly.None && environments.Grants?.TryUse(runKey, preview.Keys, out var remaining) == true)
            {
                method = AuditMethod.GrantCache;
                granted = (int)remaining.TotalSeconds;
                return true;
            }

            var prompt = RunPrompt.For(
                request,
                preview.Project,
                preview.Profile,
                [.. preview.Keys.Select((name, i) => new RunPromptVariable(name, perKey[i], entries[i].Field))],
                grantSeconds,
                onceOnly);

            // A denial cools down every run of this connection and each field it asked for, whichever
            // connection asks next and whether a run or a credential request reaches it: another argument,
            // another project from the same agent, or a set sharing one of these fields is not a new question (T-11).
            string[] cooldownKeys =
            [
                "run\0" + connectionId,
                .. entries.Select(entry => ApprovalGate.FieldKey(EntryHandle.For(entry.Name), entry.Field)).Distinct(StringComparer.Ordinal),
            ];
            answer = await environments.Gate.AskAsync(cooldownKeys, prompt, withdrawn).ConfigureAwait(false);

            // A withdrawn question is not a refusal: the resolver tells a lock from a hang-up.
            withdrawn.ThrowIfCancellationRequested();

            var timed = answer == ApprovalAnswer.Approved && grantSeconds > 0;

            if (timed)
            {
                environments.Grants?.Store(
                    runKey,
                    prompt.Project,
                    prompt.Profile,
                    command,
                    preview.Keys,
                    TimeSpan.FromSeconds(grantSeconds),
                    prompt.Client,
                    request.ClientLabel,
                    [.. entries.Select(entry => entry.Name)]);
            }

            method = AuditMethod.Prompt;
            granted = timed ? grantSeconds : 0;
            return answer.Releases();
        }

        var resolver = new SessionEnvResolver(
            () => ReferenceEquals(Live(), admitted) ? admitted : null,
            environments.VaultFor,
            environments.Clock);

        var resolved = document is null
            ? await resolver.ResolveAsync(request.Project!, request.Profile, request.Keys, Confirm, cancellationToken).ConfigureAwait(false)
            : await resolver.ResolveReferencesAsync(document, Confirm, cancellationToken).ConfigureAwait(false);

        Settled(environments, resolved);

        if (resolved.Outcome == EnvOutcome.Resolved)
        {
            return new RunReply(resolved, method, method == AuditMethod.GrantCache
                ? "served from a run a person had approved on this connection"
                : granted > 0
                    ? $"a person approved this command line for {ApprovalLimits.Describe(granted)} on this connection"
                    : "a person approved this one run")
            {
                GrantedSeconds = granted,
                Entries = shown,
            };
        }

        if (refused is not null)
        {
            return Refuse(EnvOutcome.Declined, refusedMethod, refused, shown);
        }

        var reason = resolved.Outcome == EnvOutcome.Declined ? Declined(answer) : resolved.Refusal;
        var outcomeMethod = resolved.Outcome == EnvOutcome.Declined ? answer.ToAuditMethod() : MethodOf(resolved.Outcome);

        return new RunReply(resolved, outcomeMethod, reason) { Entries = shown };
    }

    /// <summary>A run reply refused before anything was released.</summary>
    private static RunReply RunRefused(RunRequest request, EnvOutcome outcome, AuditMethod method, string reason, IReadOnlyList<string>? entries = null) =>
        new(
            EnvResolved.Refused(
                request.Project ?? EnvReferenceResolution.MixedProject,
                outcome,
                profile: request.Project is null ? EnvReferenceResolution.MixedProfile : request.Profile),
            method,
            reason)
        {
            Entries = entries ?? [],
        };

    /// <summary>
    /// A run's release as it will leave, its session and <c>recorded</c> measured with it: one naming
    /// variables the request did not name, or too large for one frame, is refused, the second naming
    /// the authority it had (T-16).
    /// </summary>
    internal static RunReply Deliverable(RunArguments asked, RunReply reply)
    {
        if (reply.Set.Outcome != EnvOutcome.Resolved)
        {
            return reply;
        }

        var (outcome, method, reason) =
            !RunRequestRules.Answers(asked, [.. reply.Set.Variables.Select(variable => variable.Key)])
                ? (EnvOutcome.Unreadable, AuditMethod.Failed, "the resolved variables are not the ones the request named")
                : !ApproverProtocol.Fits(reply with { Recorded = true })
                    ? (EnvOutcome.TooLarge, AuditMethod.Undeliverable, ApproverProtocol.UndeliverableReason(reply.Method))
                    : (EnvOutcome.Resolved, reply.Method, reply.Reason);

        return outcome == EnvOutcome.Resolved
            ? reply
            : new RunReply(EnvResolved.Refused(reply.Set.Project, outcome, profile: reply.Set.Profile), method, reason)
            {
                Entries = reply.Entries,
                Session = reply.Session,
            };
    }

    /// <summary>A credential release as it will leave, its session and <c>recorded</c> measured with it; one too large for a frame is refused, naming the authority it had (T-16).</summary>
    private static CredentialReply Deliverable(CredentialReply reply) =>
        reply.Decision == AuditDecision.Granted && !ApproverProtocol.Fits(reply with { Recorded = true })
            ? CredentialRefused(AuditMethod.Undeliverable, ApproverProtocol.UndeliverableReason(reply.Method)) with
            {
                Entry = reply.Entry,
                Session = reply.Session,
            }
            : reply;

    private static CredentialReply CredentialRefused(AuditMethod method, string reason) => new()
    {
        Decision = AuditDecision.Denied,
        Method = method,
        Reason = reason,
    };

    /// <summary>
    /// Decides a request once its line can be written, turning a withdrawal or a failure while deciding
    /// into a refusal that is recorded and answered like any other (law 3.7).
    /// </summary>
    /// <remarks>
    /// A log that will not open refuses the request at once, so a person is never asked about one this
    /// owner cannot record (T-6). A request withdrawn by the lock that ended its lifetime is refused as
    /// locked, otherwise as withdrawn.
    /// </remarks>
    private async ValueTask<T> Decided<T>(
        Func<CancellationToken, ValueTask<T>> decide,
        SessionLifetime lifetime,
        Func<AuditMethod, string, T> refused,
        CancellationToken cancellationToken)
    {
        if (!_recorder.Opens)
        {
            return refused(AuditMethod.Failed, RequestRecorder.Unopened);
        }

        try
        {
            return await decide(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return lifetime.IsLive
                ? refused(AuditMethod.Cancelled, "the request was withdrawn before it was answered")
                : refused(AuditMethod.VaultLocked, "the vault was locked before this was answered");
        }
        catch (Exception)
        {
            return refused(AuditMethod.Failed, "the request could not be completed");
        }
    }

    /// <summary>The method a set refused for this outcome is recorded under, when nobody declined it.</summary>
    private static AuditMethod MethodOf(EnvOutcome outcome) => outcome switch
    {
        EnvOutcome.NoProject or EnvOutcome.NoProfile or EnvOutcome.Unusable or EnvOutcome.Unauthorized => AuditMethod.OutOfScope,
        EnvOutcome.Unsaved or EnvOutcome.ChangedOnDisk or EnvOutcome.ChangedWhileAsked => AuditMethod.VaultChanged,
        EnvOutcome.Locked => AuditMethod.VaultLocked,
        EnvOutcome.TooLarge => AuditMethod.Undeliverable,
        EnvOutcome.Invalid => AuditMethod.InvalidRequest,
        _ => AuditMethod.Failed,
    };

    /// <summary>The outcome a set refused under this method carries.</summary>
    private static EnvOutcome OutcomeOf(AuditMethod method) => method switch
    {
        AuditMethod.VaultLocked => EnvOutcome.Locked,
        AuditMethod.Cancelled => EnvOutcome.Declined,
        _ => EnvOutcome.Unreadable,
    };

    /// <summary>Whether a request asks for exactly what a Run confirmation named: no other project, environment, directory, command or subset.</summary>
    private static bool Claims(LaunchClaim claim, EnvRequest request) =>
        string.Equals(claim.Project, request.Project, StringComparison.Ordinal)
        && string.Equals(claim.Profile, request.Profile, StringComparison.Ordinal)
        && claim.Command.SequenceEqual(request.Command, StringComparer.Ordinal)
        && request.Keys is null
        && request.FileLines is null
        && PathIdentity.SameFile(claim.Directory, request.Directory);

    private static string ChangedSince(LaunchClaim claim) =>
        $"{claim.Project}'s variables changed after you confirmed in the app";

    /// <summary>Each source entry of a released set, once, as every prompt and audit line writes it.</summary>
    private static List<string> EntriesOf(EnvResolved set) =>
        [.. set.Entries.Select(ApprovalPrompt.Shown)];

    /// <summary>The most bytes a <c>keypaste run --session</c> or <c>--token</c> line's <c>entries</c> may take: the record cap less the 2 KiB the rest of the line keeps, so the line fits after the person has answered.</summary>
    public const int MaximumAuditedEntriesBytes = AuditLog.MaximumRecordBytes - 2048;

    /// <summary>The most bytes an agent's run line's <c>entries</c> may take, because its reply carries them back to the bridge in one ordinary frame.</summary>
    public const int MaximumAgentRunEntriesBytes = 2048;

    /// <summary>Why a run of this set is refused before anyone is asked, because one audit line could not name its entries; null when it could.</summary>
    /// <param name="preview">The set's names and the entries they come from, as a confirmation shows them.</param>
    /// <returns>The refusal, or null.</returns>
    public static string? Unrecordable(EnvPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return AuditedLength([.. EntriesOf(preview).Select(entry => ApprovalPrompt.Shown(entry.Name)).Distinct(StringComparer.Ordinal)]) > MaximumAuditedEntriesBytes
            ? "the run names more items than one audit line can record"
            : null;
    }

    /// <summary>Whether a released set fits the one reply that carries it to a run; a set that does not is refused whole.</summary>
    /// <param name="set">The set, as this owner would release it.</param>
    /// <returns><see langword="true"/> when it fits.</returns>
    public static bool Sendable(EnvResolved set)
    {
        ArgumentNullException.ThrowIfNull(set);

        return ApproverProtocol.Fits(new EnvReply(set, set.Refusal));
    }

    /// <summary>The entry and field behind each name a run would inject, in the preview's order.</summary>
    private static List<(EntryName Name, string Field)> EntriesOf(EnvPreview preview) =>
    [
        .. preview.Keys.Select(key => preview.Sources.FirstOrDefault(source => string.Equals(source.Key, key, StringComparison.Ordinal)) is { } source
            ? (source.Entry, source.Field)
            : (new EntryName(string.Empty, key), key)),
    ];

    private static int AuditedLength(IReadOnlyList<string> entries)
    {
        using var buffer = new MemoryStream();

        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStringValue(entry);
            }

            writer.WriteEndArray();
        }

        return (int)buffer.Length;
    }

    /// <summary>After a set was released to a runner: records it in the ledger, and ends every env and run grant when the file changed under the owner.</summary>
    private void Settled(
        SessionEnvironments environments,
        SessionLifetime admitted,
        EnvResolved resolved,
        string client,
        string how)
    {
        Settled(environments, resolved);

        if (resolved.Outcome == EnvOutcome.Resolved)
        {
            Record(admitted, EntriesOf(resolved), client, how);
        }
    }

    /// <summary>After a set was resolved: ends every env and run grant when the file changed under the owner.</summary>
    private static void Settled(SessionEnvironments environments, EnvResolved resolved)
    {
        if (resolved.Outcome == EnvOutcome.ChangedOnDisk)
        {
            environments.Grants?.RevokeEntries(VaultEdit.Everything);
        }
    }

    private void Record(SessionLifetime lifetime, IEnumerable<string> entries, string client, string how)
    {
        lock (_ledgerGate)
        {
            if (_ledger is not { } ledger || !ReferenceEquals(ledger.Lifetime, lifetime))
            {
                ledger = (lifetime, new ReleaseLedger());
                _ledger = ledger;
            }

            ledger.Ledger.Record(entries, client, how, _clock.GetUtcNow());
        }
    }

    /// <summary>How a screen and a narration name a client: its label, else its name.</summary>
    private static string ClientName(string? label, string? name) =>
        label is { Length: > 0 } ? EntryNameSanitizer.Sanitize(label, ApprovalPrompt.MaximumClientLength).Text
        : name is { Length: > 0 } ? EntryNameSanitizer.Sanitize(name, ApprovalPrompt.MaximumClientLength).Text
        : "an unnamed agent";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The order is the security property. The token is verified against the vault as its file holds
    /// it, never echoed and never distinguished between unknown and revoked; its scope must cover the
    /// set; a protected profile needs the token to allow it and then a live answer every time, with
    /// no grant ever stored. Nobody is asked otherwise.
    /// </para>
    /// <para>
    /// This owner, not the runner, writes the audit line of every request it reads before it replies,
    /// one refused at admission or as invalid included: a runner is the untrusted side that could skip
    /// it. A set naming more items than that line can is refused before anybody is asked, and a release
    /// whose line cannot be written is refused and nothing is sent (T-6).
    /// </para>
    /// </remarks>
    public async ValueTask<EnvReply> ReleaseTokenEnvAsync(TokenEnvRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var admitted, out var refusal))
        {
            var unadmitted = Refused(request.Project, request.Profile, refusal);
            return _recorder.Recorded(
                unadmitted,
                RequestRecords.TokenRun(request.Project, request.Profile, null, unadmitted, session: null, VaultNamed(request.Vault)));
        }

        var (decided, verified) = await Decided(
            token => DecideTokenAsync(request, admitted, token),
            admitted,
            (method, reason) => (Refused(request.Project, request.Profile, OutcomeOf(method), reason), (TokenInfo?)null),
            cancellationToken).ConfigureAwait(false);

        var answered = _recorder.Recorded(
            decided,
            RequestRecords.TokenRun(request.Project, request.Profile, verified, decided, request.Session, _vault.Key));

        if (answered.Set.Outcome == EnvOutcome.Resolved && verified is not null)
        {
            Record(admitted, EntriesOf(answered.Set), $"token '{EntryNameSanitizer.Sanitize(verified.Name).Text}'", "token");
        }

        return answered;
    }

    private async ValueTask<(EnvReply Reply, TokenInfo? Verified)> DecideTokenAsync(
        TokenEnvRequest request,
        SessionLifetime admitted,
        CancellationToken cancellationToken)
    {
        if (_environments is not { } environments)
        {
            return (Refused(request.Project, request.Profile, EnvOutcome.NoSession, "the keypaste process holding this vault does not release env sets"), null);
        }

        if ((EnvReleasePrompt.Problem(request.Project, request.Command, request.Directory)
            ?? (EnvProfileNames.IsValid(request.Profile, out var invalidProfile) ? null : invalidProfile)) is { } problem)
        {
            return (Refused(request.Project, request.Profile, EnvOutcome.Invalid, problem), null);
        }

        if (environments.VaultFor(admitted) is not { } vault)
        {
            return (Refused(request.Project, request.Profile, EnvOutcome.Locked, _lockedReason), null);
        }

        TokenCheck check;
        TokenInfo? info;

        try
        {
            check = new TokenStore(vault).Verify(request.Token, environments.Clock.GetUtcNow(), out info);
        }
        catch (ObjectDisposedException)
        {
            // The lock disposed the vault between handing it over and reading it.
            return (Refused(request.Project, request.Profile, EnvOutcome.Locked, _lockedReason), null);
        }

        var reply = check switch
        {
            TokenCheck.Valid => await ReleaseUnderTokenAsync(request, info!, admitted, environments, cancellationToken).ConfigureAwait(false),
            TokenCheck.Unsaved => Refused(request.Project, request.Profile, EnvOutcome.Unsaved),
            TokenCheck.ChangedOnDisk => Refused(request.Project, request.Profile, EnvOutcome.ChangedOnDisk),
            TokenCheck.Unreadable => Refused(request.Project, request.Profile, EnvOutcome.Unreadable),
            TokenCheck.Expired => Refused(request.Project, request.Profile, EnvOutcome.Unauthorized, "the token has expired"),
            _ => Refused(request.Project, request.Profile, EnvOutcome.Unauthorized, "the token is not valid for this vault"),
        };

        return (Deliverable(reply), info);
    }

    private async ValueTask<EnvReply> ReleaseUnderTokenAsync(
        TokenEnvRequest request,
        TokenInfo info,
        SessionLifetime admitted,
        SessionEnvironments environments,
        CancellationToken cancellationToken)
    {
        if (!info.Covers(request.Project, request.Profile) || (EnvProfileNames.IsProtected(request.Profile) && !info.AllowProd))
        {
            return Refused(
                request.Project,
                request.Profile,
                EnvOutcome.Unauthorized,
                $"the token's scope does not cover {request.Project}/{request.Profile}");
        }

        // Every run is a new connection, so the cooldown names the token and the request (T-11).
        var cooldownKey = string.Join('\0', ["token", info.Id, request.Project, request.Profile, request.Directory, .. request.Command]);
        var answer = ApprovalAnswer.NoChannel;

        var resolver = new SessionEnvResolver(
            () => ReferenceEquals(Live(), admitted) ? admitted : null,
            environments.VaultFor,
            environments.Clock);

        var needsProd = false;
        string? unrecordable = null;

        // A set holding a member of a protected environment, by tag, needs the token to allow it and then a live answer.
        async ValueTask<bool> AskLive(EnvPreview preview, CancellationToken withdrawn)
        {
            if (preview.RequiresLiveApproval && !info.AllowProd)
            {
                needsProd = true;
                return false;
            }

            // Checked before anybody is asked, so a person never approves a set its audit line cannot record.
            if (Unrecordable(preview) is { } refusal)
            {
                unrecordable = refusal;
                return false;
            }

            if (!preview.RequiresLiveApproval)
            {
                return true;
            }

            var prompt = EnvReleasePrompt.For(preview, request.Command, request.Directory) with
            {
                Requester = $"token '{EntryNameSanitizer.Sanitize(info.Name).Text}' ({info.Prefix})",
                GrantSeconds = 0,
            };
            answer = await environments.Gate.AskAsync(cooldownKey, prompt, withdrawn).ConfigureAwait(false);

            // A withdrawn question is not a refusal: the resolver tells a lock from a hang-up.
            withdrawn.ThrowIfCancellationRequested();
            return answer.Releases();
        }

        var resolved = await resolver.ResolveAsync(
            request.Project,
            request.Profile,
            info.KeysFor(request.Project, request.Profile),
            AskLive,
            cancellationToken).ConfigureAwait(false);

        if (needsProd)
        {
            return Refused(
                request.Project,
                request.Profile,
                EnvOutcome.Unauthorized,
                $"the token's scope does not cover {request.Project}/{request.Profile}: it holds an item of a protected environment");
        }

        if (unrecordable is not null)
        {
            return Refused(request.Project, request.Profile, EnvOutcome.Invalid, unrecordable);
        }

        return new EnvReply(resolved, resolved.Outcome == EnvOutcome.Declined ? Declined(answer) : resolved.Refusal);
    }

    /// <inheritdoc/>
    /// <remarks>Names only: <see cref="GrantSummary"/> has no member a value could travel in (D-0351).</remarks>
    public ValueTask<GrantsReply> GrantsAsync(GrantsRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out _, out var refusal))
        {
            return ValueTask.FromResult(new GrantsReply(false, [], true, refusal.Reason));
        }

        return ValueTask.FromResult(new GrantsReply(true, GrantSummary.Of(Activity), true, string.Empty));
    }

    /// <inheritdoc/>
    public ValueTask<RevokeGrantsReply> RevokeGrantsAsync(
        RevokeGrantsRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out _, out var refusal))
        {
            return ValueTask.FromResult(new RevokeGrantsReply(0, refusal.Reason));
        }

        var activity = Activity;

        if (request.All)
        {
            RevokeAll();
            return ValueTask.FromResult(new RevokeGrantsReply(activity.Grants.Count + activity.EnvGrants.Count, string.Empty));
        }

        var ids = request.Ids.Select(id => id.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        bool Named(string id, string client) =>
            ids.Contains(id) || (request.Client is { } named && string.Equals(client, named, StringComparison.Ordinal));

        var ended = activity.Grants.Where(grant => Named(GrantId.Of(grant.Key), grant.Approved.Client)).ToList();
        var endedEnv = activity.EnvGrants.Where(grant => Named(GrantId.OfEnv(grant.Key), grant.Client ?? GrantSummary.EnvClient)).ToList();

        foreach (var grant in ended)
        {
            _inner.Revoke(grant.Key);
        }

        foreach (var grant in endedEnv)
        {
            RevokeEnvGrant(grant.Key);
        }

        return ValueTask.FromResult(new RevokeGrantsReply(ended.Count + endedEnv.Count, string.Empty));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The lock runs on its own, since an owner's lock waits for its connections to end (D-0392), and the
    /// reply waits for it to end the session it was asked under. The reply is then the confirmation, and
    /// nothing more is asked on a connection the lock is closing (F.62).
    /// </remarks>
    public async ValueTask<LockReply> LockAsync(LockRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var lifetime, out var refusal))
        {
            return new LockReply(false, refusal.Reason);
        }

        if (_lockNow is not { } lockNow)
        {
            return new LockReply(false, "the keypaste process holding this vault cannot be locked from outside");
        }

        _ = Task.Run(lockNow, CancellationToken.None);
        await EndedAsync(lifetime, cancellationToken).ConfigureAwait(false);

        return lifetime.IsLive
            ? new LockReply(false, "the request ended before the vault was locked")
            : new LockReply(true, string.Empty);
    }

    /// <inheritdoc/>
    public void Disconnected(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        _attached.TryRemove(connectionId, out _);
        _inner.Disconnected(connectionId);
        _environments?.Grants?.RevokePrefix("run\0" + connectionId + "\0");
    }

    private SessionLifetime? Live() => _lifetime() is { IsLive: true } lifetime ? lifetime : null;

    /// <summary>Completes once the lifetime has ended, or once nobody is waiting for the answer.</summary>
    private static async Task EndedAsync(SessionLifetime lifetime, CancellationToken cancellationToken)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.Ended.Register(() => ended.TrySetResult());

        try
        {
            await ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The peer has gone or the listener is stopping; the caller reads the lifetime either way.
        }
    }

    /// <summary>A runner's file line as a prompt shows it: one line, nothing that draws anything else.</summary>
    private static string ShownLine(string line) =>
        DisplayTextSanitizer.Sanitize(line.Replace('\n', '\0').Replace('\r', '\0').Replace('\t', '\0')).Text;

    /// <summary>The reply as it will leave: a set too large for one frame is refused whole before anything records it as released.</summary>
    private static EnvReply Deliverable(EnvReply reply) =>
        reply.Set.Outcome == EnvOutcome.Resolved && !ApproverProtocol.Fits(reply)
            ? Refused(reply.Set.Project, reply.Set.Profile, EnvOutcome.TooLarge)
            : reply;

    private static EnvReply Refused(string project, string profile, EnvOutcome outcome, string reason) =>
        new(EnvResolved.Refused(project, outcome, profile: profile), reason);

    /// <summary>A run refused at admission, saying whether the vault is locked or the request belongs to no session.</summary>
    private static EnvReply Refused(string project, string profile, (AuditMethod Method, string Reason) refusal) =>
        Refused(project, profile, refusal.Method == AuditMethod.VaultLocked ? EnvOutcome.Locked : EnvOutcome.NoSession, refusal.Reason);

    /// <summary>The vault a request refused at admission is recorded against: this one, only when the request named it.</summary>
    private string? VaultNamed(string vault) => _vault.Names(vault) ? _vault.Key : null;

    private static EnvReply Refused(string project, string profile, EnvOutcome outcome)
    {
        var refused = EnvResolved.Refused(project, outcome, profile: profile);
        return new(refused, refused.Refusal);
    }

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
            refusal = (AuditMethod.NoSession, "the request came from a connection attached to no unlock of the vault");
        }
        else if (!string.Equals(attachment.Vault, vault, StringComparison.Ordinal))
        {
            refusal = (AuditMethod.NoSession, "the request named a different vault from its attachment");
        }
        else if (!string.Equals(attachment.Session, session, StringComparison.Ordinal)
            || !string.Equals(session, lifetime.Id, StringComparison.Ordinal))
        {
            refusal = (AuditMethod.NoSession, "the request belongs to an unlock of the vault that has ended");
        }
        else
        {
            attachment.LastRequestAt = _clock.GetUtcNow();
            return true;
        }

        lifetime = null;
        return false;
    }

    /// <summary>One attached connection: what it named, when it attached, who it says it serves and when it last asked.</summary>
    private sealed class Attachment(string vault, string session, AttachClient? client, DateTimeOffset attachedAt)
    {
        private long _lastRequestTicks;

        internal string Vault { get; } = vault;

        internal string Session { get; } = session;

        internal AttachClient? Client { get; } = client;

        internal DateTimeOffset AttachedAt { get; } = attachedAt;

        internal IReadOnlyList<string> Exposure { get; init; } = [];

        internal DateTimeOffset LastRequestAt
        {
            get => new(Interlocked.Read(ref _lastRequestTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastRequestTicks, value.UtcTicks);
        }
    }
}
