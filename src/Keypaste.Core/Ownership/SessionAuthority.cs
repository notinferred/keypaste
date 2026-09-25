using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
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
/// (THREATS.md T-34).
/// </para>
/// <para>
/// An agent's <c>run</c> is resolved the same way, against the bridge's exposure and the client's
/// policy, and asked about as the exact program, command line, directory and variable names, unless a
/// timed grant that person gave the same command line on the same connection still covers the same
/// names (D-0358). The owner runs nothing: the bridge starts the command.
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

    private readonly VaultIdentity _vault;
    private readonly Func<SessionLifetime?> _lifetime;
    private readonly ApproverHandler _inner;
    private readonly SessionEnvironments? _environments;
    private readonly Action? _lockNow;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Attachment> _attached = new(StringComparer.Ordinal);
    private readonly Lock _ledgerGate = new();
    private (SessionLifetime Lifetime, ReleaseLedger Ledger)? _ledger;

    /// <summary>Builds the authority for one owned vault.</summary>
    /// <param name="vault">The vault this process holds.</param>
    /// <param name="lifetime">The current unlocked lifetime, or null while the vault is locked.</param>
    /// <param name="inner">What decides a request once it belongs to the current session.</param>
    /// <param name="environments">What env sets are released with, or null to refuse every one.</param>
    /// <param name="lockNow">What <c>keypaste lock</c> runs, after its reply has left; null refuses it.</param>
    /// <param name="clock">What attachments and releases are timed on; null is the system clock.</param>
    public SessionAuthority(
        VaultIdentity vault,
        Func<SessionLifetime?> lifetime,
        ApproverHandler inner,
        SessionEnvironments? environments = null,
        Action? lockNow = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(inner);

        _vault = vault;
        _lifetime = lifetime;
        _inner = inner;
        _environments = environments;
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
                        pair.Value.LastRequestAt))
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
    /// <remarks>What a stricter policy for that client does to what it already holds, so the Agents list matches the policy.</remarks>
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

        var now = _clock.GetUtcNow();
        _attached[connectionId] = new Attachment(request.Vault, lifetime.Id, request.Client, now) { LastRequestAt = now };

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

        var committed = lifetime.TryCommit();

        if (!committed && (reply.Decision == AuditDecision.Granted || reply.Method == AuditMethod.Cancelled))
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

        if (reply.Decision == AuditDecision.Granted && reply.Entry is { } entry)
        {
            Record(lifetime, [entry], ClientName(request.ClientLabel, request.ClientName), AuditLog.Wire(reply.Method));
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
                        cooldownKey,
                        prompt.Project,
                        prompt.Profile,
                        prompt.Command,
                        preview.Keys,
                        TimeSpan.FromSeconds(grantSeconds),
                        entries: [.. preview.Keys.Select(key => new EntryName(EnvProfileNames.GroupPath(request.Project, request.Profile), key))]);
                }

                return answer.Releases();
            },
            cancellationToken).ConfigureAwait(false);

        var reply = Deliverable(new EnvReply(resolved, resolved.Outcome == EnvOutcome.Declined ? Declined(answer) : resolved.Refusal));

        Settled(environments, admitted, reply.Set, GrantSummary.EnvClient, "run --session");

        return reply;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The order is the security property. Admitted, then the request's rules, then the exposure and
    /// the client's policy, then resolved under the admitted lifetime; the person is asked about the
    /// names the set holds once exposure, the name rule and the audit line's room are checked, unless
    /// a timed grant this connection holds for the same command line covers the same names. The set is
    /// read again after the answer, so an edit saved meanwhile is what leaves.
    /// </para>
    /// <para>
    /// A missing project, a missing profile, an unusable set and one outside the exposure all come back
    /// as <see cref="AuditMethod.OutOfScope"/>, so a run cannot map the vault (THREATS.md T-4); the
    /// reason, which only the audit line keeps, says which. No standing rule releases a run.
    /// </para>
    /// </remarks>
    public async ValueTask<RunReply> ReleaseRunAsync(RunRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        var project = request.Project ?? EnvReferenceResolution.MixedProject;
        var profile = request.Project is null ? EnvReferenceResolution.MixedProfile : request.Profile;

        RunReply Refuse(EnvOutcome outcome, AuditMethod method, string reason, IReadOnlyList<string>? entries = null, string? session = null) =>
            new(EnvResolved.Refused(project, outcome, profile: profile), method, reason) { Entries = entries ?? [], Session = session };

        if (!TryAdmit(request.Vault, request.Session, connectionId, out var admitted, out var refusal))
        {
            return Refuse(refusal.Method == AuditMethod.VaultLocked ? EnvOutcome.Locked : EnvOutcome.NoSession, refusal.Method, refusal.Reason);
        }

        if (_environments is not { } environments)
        {
            return Refuse(EnvOutcome.NoSession, AuditMethod.NoSession, "the keypaste process holding this vault does not run commands for agents");
        }

        var arguments = new RunArguments(
            request.Command,
            request.Directory,
            request.Project,
            request.Project is null ? null : request.Profile,
            request.Keys,
            request.References,
            request.Reason,
            RunRequestRules.DefaultTimeoutSeconds);

        if ((RunRequestRules.Check(arguments) ?? RunRequestRules.CheckProgram(request.Program, request.Command)) is { } problem)
        {
            return Refuse(EnvOutcome.Invalid, AuditMethod.InvalidRequest, $"the request's {problem.Argument} {problem.Rule}", session: request.Session);
        }

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out var globError))
        {
            return Refuse(EnvOutcome.Invalid, AuditMethod.Failed, $"the exposure this bridge was configured with is not usable: {globError}", session: request.Session);
        }

        if (!_inner.TryPolicyFor(request.ClientLabel, out var policy, out var clientsProblem))
        {
            environments.Narrate?.Invoke($"refused a run: the clients file is not usable: {clientsProblem}");
            return Refuse(EnvOutcome.Unreadable, AuditMethod.Failed, $"the clients file is not usable: {clientsProblem}", session: request.Session);
        }

        EnvReferenceDocument? document = null;

        if (request.References is { } references)
        {
            List<ReferenceLine> lines = [];

            foreach (var (reference, index) in references.Select((reference, index) => (reference, index)))
            {
                _ = KpReferences.TryParse(reference.Reference, out var parsed, out _);

                if (parsed is EntryReference { Entry: var named } && ReservedGroups.IsReserved(named.GroupPath))
                {
                    return Refuse(EnvOutcome.Unauthorized, AuditMethod.OutOfScope, "a reference names a group keypaste keeps for itself", session: request.Session);
                }

                lines.Add(new ReferenceLine(reference.Name, parsed, null, index + 1));
            }

            document = new EnvReferenceDocument(lines, []);
        }

        var command = EnvReleasePrompt.CommandLine(request.Command);
        var client = ClientName(request.ClientLabel, request.ClientName);
        string[] mode = document is null
            ? ["set", request.Project!, request.Profile, .. (request.Keys ?? []).Order(StringComparer.Ordinal)]
            : ["refs", .. request.References!.Select(reference => reference.Name + "=" + reference.Reference)];
        var runKey = string.Join('\0', ["run", connectionId, request.Directory, request.Program, .. request.Command, "\u0001", .. mode]);

        // A denial cools every run of this connection down, not only this argv: another argument or a
        // space is not a new question (T-11).
        var cooldownKey = "run\0" + connectionId;

        var answer = ApprovalAnswer.NoChannel;
        var method = AuditMethod.Prompt;
        var granted = 0;
        IReadOnlyList<string> shown = [];
        string? refused = null;
        var refusedMethod = AuditMethod.InvalidRequest;

        async ValueTask<bool> Confirm(EnvPreview preview, CancellationToken withdrawn)
        {
            var entries = EntriesOf(preview, document, request);
            shown = [.. entries.Select(entry => ApprovalPrompt.Shown(entry.Name))];

            // Exposure first, so a set outside it is refused as out of scope whatever its size (T-4).
            if (entries.Any(entry => !exposure.Allows(entry.Name)))
            {
                (refused, refusedMethod) = ("a variable's entry is outside this bridge's configured exposure", AuditMethod.OutOfScope);
                return false;
            }

            if (RunRequestRules.NameProblem(preview.Keys) is { } names)
            {
                (refused, refusedMethod) = ($"the set's variables {names}", AuditMethod.InvalidRequest);
                return false;
            }

            if (AuditedLength(shown) > MaximumAuditedEntriesBytes)
            {
                (refused, refusedMethod) = ("the run names more entries than one audit line can record", AuditMethod.InvalidRequest);
                return false;
            }

            var onceOnly = entries.Any(entry => EnvProfileNames.RequiresLiveApproval(entry.Name))
                ? OnceOnly.ProtectedProfile
                : policy == ClientPolicy.AskEveryTime ? OnceOnly.ClientPolicy : OnceOnly.None;
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
                [.. preview.Keys.Select((name, i) => new RunPromptVariable(name, shown[i], entries[i].Field))],
                grantSeconds,
                onceOnly);

            answer = await environments.Gate.AskAsync(cooldownKey, prompt, withdrawn).ConfigureAwait(false);

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

        if (resolved.Outcome == EnvOutcome.Resolved)
        {
            var released = new RunReply(resolved, method, method == AuditMethod.GrantCache
                ? "served from a run a person had approved on this connection"
                : granted > 0
                    ? $"a person approved this command line for {ApprovalLimits.Describe(granted)} on this connection"
                    : "a person approved this one run")
            {
                GrantedSeconds = granted,
                Entries = shown,
                Session = request.Session,
            };

            if (!ApproverProtocol.Fits(released))
            {
                var tooLarge = EnvResolved.Refused(resolved.Project, EnvOutcome.TooLarge, profile: resolved.Profile);
                return new RunReply(tooLarge, AuditMethod.Undeliverable, ApproverProtocol.UndeliverableReason(method))
                {
                    Entries = shown,
                    Session = request.Session,
                };
            }

            Settled(environments, admitted, resolved, client, "run", shown);

            // Every run is narrated with its command, a grant-served one too: the grant approved a
            // command line, not what the files it runs do (T-35).
            environments.Narrate?.Invoke(method == AuditMethod.GrantCache
                ? $"released {resolved.Variables.Count} variable(s) to {client} for `{command}` from a timed grant ({granted}s left)"
                : $"released {resolved.Variables.Count} variable(s) to {client} for `{command}` {(granted > 0 ? $"for {granted}s" : "once")}");

            return released;
        }

        Settled(environments, admitted, resolved, client, "run", shown);

        if (refused is not null)
        {
            return Refuse(EnvOutcome.Declined, refusedMethod, refused, shown, request.Session);
        }

        var (outcomeMethod, reason) = resolved.Outcome switch
        {
            EnvOutcome.Declined => (answer.ToAuditMethod(), Declined(answer)),
            EnvOutcome.NoProject or EnvOutcome.NoProfile or EnvOutcome.Unusable or EnvOutcome.Unauthorized => (AuditMethod.OutOfScope, resolved.Refusal),
            EnvOutcome.Unsaved or EnvOutcome.ChangedOnDisk or EnvOutcome.ChangedWhileAsked => (AuditMethod.VaultChanged, resolved.Refusal),
            EnvOutcome.Locked => (AuditMethod.VaultLocked, resolved.Refusal),
            EnvOutcome.TooLarge => (AuditMethod.Undeliverable, resolved.Refusal),
            EnvOutcome.Invalid => (AuditMethod.InvalidRequest, resolved.Refusal),
            _ => (AuditMethod.Failed, resolved.Refusal),
        };

        return new RunReply(resolved, outcomeMethod, reason) { Entries = shown, Session = request.Session };
    }

    /// <summary>Each entry of a released set, as every prompt and audit line writes it.</summary>
    private static List<string> EntriesOf(EnvResolved set) =>
        [.. set.Variables.Select(variable => ApprovalPrompt.Shown(new EntryName(EnvProfileNames.GroupPath(set.Project, set.Profile), variable.Key)))];

    /// <summary>The most bytes a run line's <c>entries</c> may take, so the line fits after the person has answered.</summary>
    public const int MaximumAuditedEntriesBytes = 2048;

    /// <summary>The entry and field behind each name a run would inject, in the preview's order.</summary>
    private static List<(EntryName Name, string Field)> EntriesOf(EnvPreview preview, EnvReferenceDocument? document, RunRequest request)
    {
        if (document is null)
        {
            var group = EnvProfileNames.GroupPath(request.Project!, request.Profile);
            return [.. preview.Keys.Select(key => (new EntryName(group, key), "password"))];
        }

        var byName = document.Lines.ToDictionary(line => line.Name, line => line.Reference, StringComparer.Ordinal);

        return
        [
            .. preview.Keys.Select(name => byName.GetValueOrDefault(name) switch
            {
                EnvReference env => (new EntryName(EnvProfileNames.GroupPath(env.Project, env.Profile), env.Key), "password"),
                EntryReference entry => (entry.Entry, entry.Field),
                _ => (new EntryName(string.Empty, name), "password"),
            }),
        ];
    }

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

    /// <summary>After a set was resolved: records a release in the ledger, and ends every env and run grant when the file changed under the owner.</summary>
    private void Settled(
        SessionEnvironments environments,
        SessionLifetime admitted,
        EnvResolved resolved,
        string client,
        string how,
        IReadOnlyList<string>? entries = null)
    {
        if (resolved.Outcome == EnvOutcome.ChangedOnDisk)
        {
            environments.Grants?.RevokeEntries(VaultEdit.Everything);
        }

        if (resolved.Outcome == EnvOutcome.Resolved)
        {
            Record(admitted, entries ?? EntriesOf(resolved), client, how);
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
        : "an unnamed client";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The order is the security property. The token is verified against the vault as its file holds
    /// it, never echoed and never distinguished between unknown and revoked; its scope must cover the
    /// set; a protected profile needs the token to allow it and then a live answer every time, with
    /// no grant ever stored. Nobody is asked otherwise.
    /// </para>
    /// <para>
    /// This owner, not the runner, writes the audit line for every outcome once the token is looked
    /// at, before it replies: a runner is the untrusted side that could skip it. A release whose line
    /// cannot be written is refused and nothing is sent (T-6).
    /// </para>
    /// </remarks>
    public async ValueTask<EnvReply> ReleaseTokenEnvAsync(TokenEnvRequest request, string connectionId, CancellationToken cancellationToken)
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

        if ((EnvReleasePrompt.Problem(request.Project, request.Command, request.Directory)
            ?? (EnvProfileNames.IsValid(request.Profile, out var invalidProfile) ? null : invalidProfile)) is { } problem)
        {
            return Refused(request.Project, request.Profile, EnvOutcome.Invalid, problem);
        }

        if (environments.VaultFor(admitted) is not { } vault)
        {
            return Refused(request.Project, request.Profile, EnvOutcome.Locked, _lockedReason);
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
            return Refused(request.Project, request.Profile, EnvOutcome.Locked, _lockedReason);
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

        var audited = Audited(environments.Audit, request, info, Deliverable(reply));

        if (audited.Set.Outcome == EnvOutcome.Resolved && info is not null)
        {
            Record(admitted, EntriesOf(audited.Set), $"token '{EntryNameSanitizer.Sanitize(info.Name).Text}'", "token");
        }

        return audited;
    }

    private async ValueTask<EnvReply> ReleaseUnderTokenAsync(
        TokenEnvRequest request,
        TokenInfo info,
        SessionLifetime admitted,
        SessionEnvironments environments,
        CancellationToken cancellationToken)
    {
        var liveOnly = EnvProfileNames.IsProtected(request.Profile);

        if (!info.Covers(request.Project, request.Profile) || (liveOnly && !info.AllowProd))
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

        async ValueTask<bool> AskLive(EnvPreview preview, CancellationToken withdrawn)
        {
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
            liveOnly ? AskLive : null,
            cancellationToken).ConfigureAwait(false);

        return new EnvReply(resolved, resolved.Outcome == EnvOutcome.Declined ? Declined(answer) : resolved.Refusal);
    }

    /// <summary>The reply, once its audit line is written; a release whose line cannot be written becomes a refusal.</summary>
    private EnvReply Audited(Func<AuditLog?>? audit, TokenEnvRequest request, TokenInfo? info, EnvReply reply)
    {
        var released = reply.Set.Outcome == EnvOutcome.Resolved;
        var said = released
            ? $"{reply.Set.Variables.Count} variable(s)"
            : reply.Reason.Length > 0 ? reply.Reason : reply.Set.Refusal;

        var record = new AuditRecord
        {
            Tool = "run",
            Client = new AuditClient("keypaste run --token", CoreInfo.Version, null),
            Args = new AuditArgs
            {
                Entry = EntryNameSanitizer.SanitizePath(
                    $"{EnvConvention.RootGroup}/{request.Project}/{request.Profile}",
                    maximumLength: AuditArgs.EntryLength).Text,
            },
            Decision = released ? AuditDecision.Granted : AuditDecision.Denied,
            Method = AuditMethod.Token,
            Reason = info is null ? said : TokenAuditReason.Format(info.Id, EntryNameSanitizer.Sanitize(info.Name).Text, said),
            Session = request.Session,
            Vault = _vault.Key,
            Entries = released ? EntriesOf(reply.Set) : null,
        };

        bool written;

        try
        {
            written = audit?.Invoke() is { } log && log.TryAppend(record, out _);
        }
        catch (ObjectDisposedException)
        {
            written = false;
        }

        return written || !released
            ? reply
            : Refused(request.Project, request.Profile, EnvOutcome.Unreadable, "the audit log could not be written");
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
    /// <remarks>The lock runs after this returns, so the reply leaves before the lifetime ends and the listener stops.</remarks>
    public ValueTask<LockReply> LockAsync(LockRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryAdmit(request.Vault, request.Session, connectionId, out _, out var refusal))
        {
            return ValueTask.FromResult(new LockReply(false, refusal.Reason));
        }

        if (_lockNow is not { } lockNow)
        {
            return ValueTask.FromResult(new LockReply(false, "the keypaste process holding this vault cannot be locked from outside"));
        }

        _ = Task.Run(lockNow, CancellationToken.None);
        return ValueTask.FromResult(new LockReply(true, string.Empty));
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

        internal DateTimeOffset LastRequestAt
        {
            get => new(Interlocked.Read(ref _lastRequestTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastRequestTicks, value.UtcTicks);
        }
    }
}
