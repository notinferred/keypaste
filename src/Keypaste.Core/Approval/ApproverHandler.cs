using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>
/// The approver's decision procedure: resolve, re-scope, honour a refusal, release under the agent's
/// mode or what the person always allowed, or ask a human, release one field.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the core with no I/O of its own — no terminal, no dialog, no pipe — so every
/// ordering that matters is reachable from a unit test. What it talks to are four seams: where
/// entries are resolved and read, where names come from, where a human is, and each agent's settings,
/// which say what the agent may see and get, how it is asked and what the person always allowed.
/// </para>
/// <para>
/// <b>The order is the security property.</b> An agent given no values is refused before anything
/// is resolved, so it learns nothing of the vault from a credential request. Then resolve, then
/// re-check what the agent can see, then honour a refusal a person just gave, then release under an
/// Automatic mode or an Always allow row, and only then ask a person; read the field last of all. Reading earlier would put a secret in memory for requests that are about to be refused, and
/// asking earlier would let an agent raise a prompt for an entry it was never allowed to name.
/// </para>
/// <para>
/// <b>Where Automatic and Always allow sit in that order.</b> After the reach re-check, because what
/// an agent sees is the least of the bridge's <c>--expose</c>, its settings and this computer's
/// ceilings, and neither reaches past it. After the cooldown, because a person's explicit no outranks
/// both. And in here rather than inside <see cref="ApprovalGate"/>, because a release nobody is asked
/// about must not be able to collide with a prompt somebody is in the middle of answering.
/// </para>
/// <para>
/// A person answers once or Always allow, which releases once and then asks them to confirm the change.
/// A protected entry skips Automatic and Always allow and offers none, so it is asked about every time
/// whatever any setting says, and so is one whose tag became protected after they chose it. A request
/// asked about because an Automatic limit was reached offers none either, since an Always there would
/// bypass a limit the person set, and no Always allow row answers it.
/// </para>
/// </remarks>
public sealed class ApproverHandler
{
    private readonly ICredentialSource _source;
    private readonly IEntryNameLister _lister;
    private readonly ApprovalGate _gate;
    private readonly IAgentSettings _settings;
    private readonly HourlyLimit _hourly;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;
    private readonly Func<EntryName, bool> _requiresLiveApproval;

    /// <summary>Builds the handler over its four seams.</summary>
    /// <param name="source">Where entries are resolved and one field is read.</param>
    /// <param name="lister">Where names come from. Deliberately not the same seam.</param>
    /// <param name="gate">Where a human is asked, and where the deadline lives.</param>
    /// <param name="settings">
    /// Each agent's settings and this computer's ceilings; <see cref="IAgentSettings.Defaults"/> holds
    /// every agent to the defaults, which is what keypaste did before they existed.
    /// </param>
    /// <param name="narrate">Optional: a line of running commentary for the operator's terminal.</param>
    /// <param name="requiresLiveApproval">
    /// Which entries are asked about every time, with no Always allow and no Automatic release; null means the
    /// source's own answer (<see cref="ICredentialSource.RequiresLiveApproval"/>), so a host that
    /// forgets it still asks live about a protected environment, by tag.
    /// </param>
    /// <param name="hourly">
    /// The owner's hourly limits on Automatic, which outlive one unlock; null counts afresh, as a
    /// restarted owner does.
    /// </param>
    /// <param name="clock">What an Automatic limit's time is judged against, and whose time zone a confirmation shows it in; null is the system clock.</param>
    /// <exception cref="ArgumentNullException">Any of the four seams is null.</exception>
    /// <remarks>
    /// <paramref name="settings"/> is not nullable, and "every agent gets the defaults" is the value
    /// <see cref="IAgentSettings.Defaults"/>. A nullable seam makes every future reader work out what
    /// null means, and a thing with a meaning should be a value rather than an absence.
    /// </remarks>
    public ApproverHandler(
        ICredentialSource source,
        IEntryNameLister lister,
        ApprovalGate gate,
        IAgentSettings settings,
        Action<string>? narrate = null,
        Func<EntryName, bool>? requiresLiveApproval = null,
        HourlyLimit? hourly = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(settings);

        _source = source;
        _lister = lister;
        _gate = gate;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        _hourly = hourly ?? new HourlyLimit(_clock);
        _narrate = narrate;
        _requiresLiveApproval = requiresLiveApproval ?? source.RequiresLiveApproval;
    }

    /// <summary>Whether a release of this entry is asked about live every time, by the rule this handler applies.</summary>
    /// <param name="name">The entry.</param>
    /// <returns><see langword="true"/> when no mode, Always allow row or token may release it unprompted.</returns>
    /// <remarks>For the release paths beside this one, so a run naming the entry is held to the same rule.</remarks>
    public bool RequiresLiveApproval(EntryName name) => _requiresLiveApproval(name);

    /// <summary>An entry's UUID as hex, for the release paths beside this one to name it by in an Always allow row.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>The UUID, or null when it cannot be read.</returns>
    public string? ItemId(EntryName name) => _source.ItemId(name);

    /// <summary>An entry's own tags, as the exposure and the agent's settings read them.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its tags, or none when they cannot be read.</returns>
    /// <remarks>For the release paths beside this one, so a run is held to the agent's reach as a credential request is.</remarks>
    public IReadOnlyList<string> Tags(EntryName name) => _source.Tags(name);

    /// <summary>What applies to an agent's requests now: its settings in the vault beneath this computer's ceilings.</summary>
    /// <param name="label">The bridge's raw <c>--client-label</c>, or null.</param>
    /// <param name="access">What applies, when the settings could be read.</param>
    /// <param name="problem">Why they could not be, otherwise empty.</param>
    /// <returns>False only when <c>clients.toml</c> or the vault's settings cannot be read, which refuses every request.</returns>
    public bool TryAccess(string? label, [NotNullWhen(true)] out EffectiveAccess? access, out string problem)
    {
        access = _settings.TryRead(label, out var records, out var caps, out problem)
            ? EffectiveAccess.Create(label, records, caps)
            : null;

        return access is not null;
    }

    /// <summary>One agent's settings as the person's own screen shows them: what the vault says and what this computer holds back.</summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="settings">Its settings, when they could be read.</param>
    /// <param name="problem">Why they could not be, otherwise empty.</param>
    /// <returns>False only when <c>clients.toml</c> or the vault's settings cannot be read.</returns>
    public bool TrySettings(string agent, [NotNullWhen(true)] out AgentSettingsState? settings, out string problem)
    {
        ArgumentNullException.ThrowIfNull(agent);

        settings = _settings.TryRead(agent, out var records, out var caps, out problem)
            ? AgentSettingsState.Read(agent, records, caps)
            : null;

        return settings is not null;
    }

    /// <summary>Which entry names may be shown to an agent: those its bridge's exposure and its settings both reach.</summary>
    /// <param name="request">The exposure the bridge is configured with, and its label.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The names, or the reason there are none.</returns>
    public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Complete on all three paths: this handler hands on every name the lister gave it, and a
        // refusal that names nothing has left nothing out. What one frame can carry is decided in
        // ApproverProtocol, which is the only place that knows what a name costs to encode.
        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out _))
        {
            return ValueTask.FromResult(
                new NamesReply(false, [], "this bridge's --expose is not usable", true) { Refusal = AuditMethod.Failed });
        }

        if (!TryAccess(request.ClientLabel, out var access, out var problem))
        {
            _narrate?.Invoke($"refused a listing: {Unusable(problem)}");
            return ValueTask.FromResult(new NamesReply(false, [], Unusable(problem), true) { Refusal = AuditMethod.Failed });
        }

        if (!_lister.TryList(exposure, out var names, out var failure))
        {
            return ValueTask.FromResult(
                IsStale(failure)
                    ? new NamesReply(false, [], Stale(failure, "a listing"), true) { Refusal = AuditMethod.VaultChanged }
                    : new NamesReply(false, [], Explain(failure), true));
        }

        List<ListedEntry> seen =
        [
            .. names
                .Select(entry => (Entry: entry, Reach: access.Reach(exposure, entry.Name, entry.Tags)))
                .Where(listed => listed.Reach != ExposureReach.None)
                .Select(listed => listed.Entry.Within(listed.Reach)),
        ];

        return ValueTask.FromResult(new NamesReply(true, seen, string.Empty, true));
    }

    /// <summary>Decides one credential request, asking a human if it has to.</summary>
    /// <param name="request">What the agent asked for.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The decision, and the field value on the one path that has one.</returns>
    public async ValueTask<CredentialReply> RequestAsync(
        CredentialRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        if (CredentialRequestRules.Check(request.Entry, request.Field, request.Reason) is { } problem)
        {
            return Refused(AuditMethod.InvalidRequest, $"the request's {problem.Argument} {problem.Rule}");
        }

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out var globError))
        {
            return Refused(AuditMethod.Failed, $"this bridge's --expose is not usable: {globError}");
        }

        if (!TryAccess(request.ClientLabel, out var access, out var unusable))
        {
            _narrate?.Invoke($"refused a request: {Unusable(unusable)}");
            return Refused(AuditMethod.Failed, Unusable(unusable));
        }

        // Before anything is resolved, so an agent given no values learns nothing of the vault from a credential request.
        if (!access.Values)
        {
            return Refused(AuditMethod.InjectOnly, "this agent is given no values; they reach only commands it runs");
        }

        if (!_source.TryResolve(request.Entry, out var name, out var failure))
        {
            // A name that resolves to nothing and one outside what the agent sees get the same answer:
            // telling them apart would let an agent enumerate entries in parts of the vault it was
            // never allowed to see (THREATS.md T-4). The audit line's reason still records which.
            return failure switch
            {
                CredentialFailure.VaultLocked => Refused(AuditMethod.VaultLocked, "no vault is unlocked"),
                _ when IsStale(failure) => Refused(AuditMethod.VaultChanged, Stale(failure, "a request")),
                _ => Refused(AuditMethod.OutOfScope, Explain(failure)),
            };
        }

        // The re-check that makes handles safe. The bridge can test a path against its globs
        // before forwarding, but it cannot resolve a handle or read a tag without the vault, so
        // without this a handle would be the way around what the agent may see.
        var tags = _source.Tags(name);
        var reach = access.Reach(exposure, name, tags);

        if (reach == ExposureReach.None)
        {
            return Refused(AuditMethod.OutOfScope, "the item is outside what this agent can see through this bridge");
        }

        if (!EntryExposure.Permits(reach, request.Field))
        {
            return Refused(AuditMethod.OutOfScope, "this agent reaches the item through a project tag, which lets it see only the item's project variables");
        }

        var display = ApprovalPrompt.Shown(name);
        var cooldownKey = ApprovalGate.FieldKey(EntryHandle.For(name), request.Field);
        var isProtected = _requiresLiveApproval(name);
        var item = access.Item(tags, _clock.GetUtcNow());

        // Before Automatic and Always allow, not after. A person who refused this exact request a moment
        // ago has said something more specific and more recent than any setting, and a setting must
        // never resurrect a decision they just made.
        if (_gate.IsInCooldown(cooldownKey))
        {
            _narrate?.Invoke($"refused {display}: {Explain(ApprovalAnswer.Cooldown)}");
            return Refused(AuditMethod.Cooldown, Explain(ApprovalAnswer.Cooldown), display);
        }

        // A limit that is spent asks instead: the person is the fallback, never a refusal.
        if (!isProtected && access.TryAutomatic(item, _hourly))
        {
            return Unasked(
                request,
                name,
                display,
                cooldownKey,
                access.Label!,
                AuditMethod.Automatic,
                shown => $"released without asking: {shown}'s mode for this item is Automatic",
                "Automatic",
                cancellationToken);
        }

        // Where it is Automatic and still reached here, its hourly limit is spent; no row bypasses that.
        var limitReached = item.LimitReached || item.Mode == AgentMode.Automatic;
        var itemId = _source.ItemId(name);

        if (!isProtected && !limitReached && itemId is not null && access.AlwaysAllows(itemId))
        {
            return Unasked(
                request,
                name,
                display,
                cooldownKey,
                access.Label!,
                AuditMethod.AlwaysAllowed,
                shown => $"released without asking: a person always allowed this item for {shown}",
                "Always allow",
                cancellationToken);
        }

        var offer = isProtected || limitReached ? null : Offer(access, itemId, tags, display);
        var prompt = ApprovalPrompt.For(request.ClientName, name, request.Field, request.Reason, request.ClientLabel) with
        {
            OnceOnly = isProtected ? OnceOnly.ProtectedProfile : limitReached ? OnceOnly.LimitReached : offer is null ? OnceOnly.Unnamed : OnceOnly.None,
            Always = offer,
        };

        var (answer, kept) = await _gate.AskKeepingAsync(cooldownKey, prompt, cancellationToken).ConfigureAwait(false);

        try
        {
            var reply = Answered(request, name, display, cooldownKey, prompt, answer, cancellationToken);

            // Always allow's release goes with the slot the gate kept, so its confirmation follows with
            // no other request in front of the person between them.
            if (reply.Always is null)
            {
                return reply;
            }

            reply = reply with { Kept = kept };
            kept = null;
            return reply;
        }
        finally
        {
            kept?.Dispose();
        }
    }

    /// <summary>A prompted request once the person has answered.</summary>
    private CredentialReply Answered(
        CredentialRequest request,
        EntryName name,
        string display,
        string cooldownKey,
        ApprovalPrompt prompt,
        ApprovalAnswer answer,
        CancellationToken cancellationToken)
    {
        var always = answer == ApprovalAnswer.Always ? prompt.Always : null;

        // Always allow allows this one request and then asks for the change; on a prompt that did not
        // offer it, it is an answer nobody could have meant, so nothing is released.
        if (answer == ApprovalAnswer.Always)
        {
            answer = always is null ? ApprovalAnswer.Failed : ApprovalAnswer.ApprovedOnce;
        }

        if (!answer.Releases())
        {
            _narrate?.Invoke($"refused {display} for {prompt.Client}: {Explain(answer)}");
            return Refused(answer.ToAuditMethod(), Explain(answer), display);
        }

        if (Withdrawn(display, cancellationToken) is { } withdrawn)
        {
            return withdrawn;
        }

        // Last of all. Nothing has decrypted a field until a person said yes to this exact request.
        if (!_source.TryRead(name, request.Field, out var released, out var readFailure, out var unfilled))
        {
            return Unread(readFailure, cooldownKey, request.Field, unfilled, display, prompt.Client);
        }

        using (released)
        {
            return Deliver(
                new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = "a person approved this one request",
                    Entry = display,
                    CodeSeconds = released.CurrentFor is { } current ? WholeSeconds(current) : 0,
                    Value = released.Value.ToString(),
                    Always = always,
                },
                display,
                $"released {display} to {prompt.Client} once",
                cooldownKey);
        }
    }

    /// <summary>Releases a field nobody is asked about, because of a setting the person confirmed: an Automatic mode or an Always allow row.</summary>
    /// <remarks>
    /// Its line names which setting it was, and never a person, since nobody saw this request. The read is
    /// still last of all, exactly as on the prompted path: a setting that covers the entry is not a reason
    /// to decrypt anything until the release itself is certain.
    /// </remarks>
    private CredentialReply Unasked(
        CredentialRequest request,
        EntryName name,
        string display,
        string cooldownKey,
        string agent,
        AuditMethod method,
        Func<string, string> why,
        string setting,
        CancellationToken cancellationToken)
    {
        var shown = EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text;

        if (Withdrawn(display, cancellationToken) is { } withdrawn)
        {
            return withdrawn;
        }

        if (!_source.TryRead(name, request.Field, out var released, out var readFailure, out var unfilled))
        {
            return Unread(readFailure, cooldownKey, request.Field, unfilled, display, shown);
        }

        using (released)
        {
            // One line on the operator's terminal either way, and the only live signal that this
            // happened at all: no prompt was drawn and nobody was asked (THREATS.md T-12).
            return Deliver(
                new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = method,
                    Reason = why(shown),
                    Entry = display,
                    CodeSeconds = released.CurrentFor is { } current ? WholeSeconds(current) : 0,
                    Value = released.Value.ToString(),
                },
                display,
                $"released {display} to {shown} without asking ({setting})");
        }
    }

    /// <summary>
    /// What Always allow offers a named agent: an item in exactly one project environment makes that
    /// environment Automatic, and any other item gets a row of its own, by its UUID.
    /// </summary>
    private static AlwaysOffer? Offer(EffectiveAccess access, string? itemId, IReadOnlyList<string> tags, string display)
    {
        if (access.Label is null)
        {
            return null;
        }

        var members = tags.Select(ProjectTag.Read).Where(tag => tag.Kind == ProjectTagKind.Member).ToList();

        if (members is [{ Project: { } project, Environment: { } environment }] && ProjectScope.TryCreate(project, environment, out var scope, out _))
        {
            return new AlwaysOffer(AccessEdit.Automatic(scope), scope.Name);
        }

        return itemId is null ? null : new AlwaysOffer(AccessEdit.AlwaysItem(itemId), display);
    }

    /// <summary>The name of an item a row or a confirmation names, as a person reads it.</summary>
    /// <param name="item">The item's UUID as hex.</param>
    /// <returns>Its name, or words saying it is not in this vault.</returns>
    public string ShownItem(string item) =>
        _source.ItemNamed(item) is { } name ? ApprovalPrompt.Shown(name) : "an item no longer in this vault";

    /// <summary>Whether a protected tag keeps an item asked about, whatever its Always allow row says.</summary>
    /// <param name="item">The item's UUID as hex.</param>
    /// <returns>Whether the item is there and asked about every time.</returns>
    public bool IsProtectedItem(string item) =>
        _source.ItemNamed(item) is { } name && _requiresLiveApproval(name);

    /// <summary>Every agent's settings the vault holds, as keypaste reads them.</summary>
    /// <returns>The settings, saying when they apply only beneath the defaults.</returns>
    public AccessListReply ListAccess()
    {
        var list = _settings.List();

        return new AccessListReply(
            true,
            [.. list.Records.Select(record => new AccessRow(record.Name, AcafRecord.Write(record.Record.Access), record.Record.Understood))],
            true,
            list.State == SavedRead.Current
                ? string.Empty
                : "the vault holds a change not saved yet, or another program changed its file, so these apply only beneath the defaults");
    }

    /// <summary>Changes one agent's settings: at once when the change gives it nothing more, otherwise only once the person confirms it.</summary>
    /// <param name="request">The change, in <see cref="AccessEdit"/>'s words, which are read here again.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What became of it.</returns>
    /// <remarks>One that says an agent asked meets the bridge's <see cref="AccessChangeRules"/> again here, its reason included.</remarks>
    public ValueTask<AccessChangeReply> ChangeAccessAsync(AccessChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Reason.Length > AccessChangeRequest.MaximumReasonLength)
        {
            return ValueTask.FromResult(Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, $"the reason is longer than {AccessChangeRequest.MaximumReasonLength} characters"));
        }

        if (request.Origin == AccessOrigin.Agent && AccessChangeRules.Check(request) is { } problem)
        {
            return ValueTask.FromResult(Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, $"the {problem.Argument} argument {problem.Rule}"));
        }

        if (request.Forget is { } forget)
        {
            return AccessEdit.TryForget(forget, out var forgetting, out var forgetError)
                ? ChangeAccessAsync(request.Agent, forgetting, request.Origin, request.Reason, cancellationToken)
                : ValueTask.FromResult(Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, forgetError));
        }

        if (!AccessEdit.TryCreate(
                request.Project,
                request.Mode,
                request.See,
                request.Values,
                request.Run,
                request.PerHour,
                request.Until,
                request.Reset,
                out var edit,
                out var error))
        {
            return ValueTask.FromResult(Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, error));
        }

        return ChangeAccessAsync(request.Agent, edit, request.Origin, request.Reason, cancellationToken);
    }

    /// <summary>Changes one agent's settings, as <see cref="ChangeAccessAsync(AccessChangeRequest, CancellationToken)"/> does, from an edit already read.</summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="origin">Where the request says it came from.</param>
    /// <param name="reason">The requester's own reason, or empty.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What became of it.</returns>
    /// <remarks>
    /// <para>
    /// A change is judged by what the record it leaves would let the agent do, against what applies now,
    /// this computer's ceilings included; while that record would still hold parts keypaste does not
    /// understand, what it already said stays inactive, and only what the change adds beyond it, or the
    /// agent would get beneath the defaults, counts (<see cref="AccessChange"/>). One that gives nothing
    /// more applies at once: to the ceilings, so a restore or a merge cannot undo it here, and, unless an
    /// agent asked, to the vault's record. Any other is put to the person through the gate, under its
    /// window, its one slot and its cooldown (THREATS.md T-11), and only once they confirm is the record
    /// saved and this computer's ceiling lifted as far as the change goes. The record is changed only
    /// where the change names something; one the change cannot be written into is refused before anyone
    /// is asked, and when parts keypaste does not understand would remain, the confirmation and the
    /// reply say the agent gets no more than the defaults until they are fixed.
    /// </para>
    /// <para>
    /// The settings for every other agent, <c>*</c>, only ever take away from the defaults, so a change
    /// that would give them more is refused before anybody is asked.
    /// </para>
    /// </remarks>
    public ValueTask<AccessChangeReply> ChangeAccessAsync(
        string agent,
        AccessEdit edit,
        AccessOrigin origin,
        string reason,
        CancellationToken cancellationToken) =>
        ChangeAccessAsync(agent, edit, origin, reason, null, cancellationToken);

    /// <summary>Changes one agent's settings, asking any confirmation in the slot Always allow kept when there is one.</summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="origin">Where the request says it came from.</param>
    /// <param name="reason">The requester's own reason, or empty.</param>
    /// <param name="kept">The slot the release's prompt kept, or null.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What became of it.</returns>
    internal async ValueTask<AccessChangeReply> ChangeAccessAsync(
        string agent,
        AccessEdit edit,
        AccessOrigin origin,
        string reason,
        KeptSlot? kept,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(reason);

        var reply = await DecideChangeAsync(agent, edit, origin, reason, kept, cancellationToken).ConfigureAwait(false);

        // The change itself on every answer, so the line that records it says what was asked.
        return reply with { Reason = $"{reply.Reason} ({EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text}: {edit})" };
    }

    private async ValueTask<AccessChangeReply> DecideChangeAsync(
        string agent,
        AccessEdit edit,
        AccessOrigin origin,
        string reason,
        KeptSlot? kept,
        CancellationToken cancellationToken)
    {
        // Always allow tells the person why where they answered, once the release it allowed has left.
        var narrate = origin == AccessOrigin.Prompt ? null : _narrate;

        if (!AgentNames.IsValid(agent, out var nameError))
        {
            return Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, nameError);
        }

        if (!_settings.TryRead(agent, out var records, out var caps, out var problem))
        {
            narrate?.Invoke($"refused a change to {agent}'s settings: {Unusable(problem)}");
            return Changed(AuditDecision.Denied, AuditMethod.Failed, Unusable(problem));
        }

        var current = EffectiveAccess.Create(agent, records, caps);
        var writable = _settings.TryPreview(agent, edit, out var preview, out var refusal);
        var written = preview ?? Unwritten(agent, records, edit);

        if (string.Equals(agent, AgentNames.Any, StringComparison.Ordinal)
            && !AccessChange.Classify(EffectiveAccess.Defaults, written.Access).IsNarrowing)
        {
            return Changed(
                AuditDecision.Denied,
                AuditMethod.InvalidRequest,
                "the settings for every other agent, *, only take away from the defaults: never Automatic, more to see, values beyond them or commands");
        }

        // Judged by the record it would leave, which says more than the change names when it fixes a part keypaste could not read.
        var change = AccessChange.Classify(current, records, written);

        // What this computer's ceilings hold back already is lifted only by a change that names it.
        if (_settings.TryPreview(agent, AccessEdit.VaultSettings, out var standing, out _))
        {
            change = change.Without(AccessChange.Classify(current, records, standing).Gains, edit.Names);
        }

        if (change.IsNarrowing)
        {
            return Made(agent, edit, origin, written, Narrow(agent, edit, records, change, origin, narrate));
        }

        if (!writable)
        {
            narrate?.Invoke($"did not change {agent}'s settings: {refusal}");
            return Changed(AuditDecision.Denied, AuditMethod.Failed, refusal);
        }

        var prompt = AccessChangePrompt.For(agent, change, origin, reason, _clock.LocalTimeZone, ShownItem) with { StaysBeneathDefaults = !written.Understood };

        // Every request for more over the pipe shares one cooldown, whatever it asks for and whichever
        // agent it names, since both the name and the origin are the requester's own claim: no program
        // can keep a confirmation in front of the person by rewording, renaming or switching to a
        // terminal. The app's own settings and Always allow come only from the person at the owner, never
        // over the pipe, so their refusals hold nothing back and nothing holds them back.
        var overThePipe = origin is AccessOrigin.Agent or AccessOrigin.Terminal;
        string[] cooldownKeys = overThePipe ? [ApprovalGate.AskedForMore] : [];
        var answer = await _gate.AskAsync(cooldownKeys, prompt, coolUnanswered: overThePipe, kept, cancellationToken).ConfigureAwait(false);

        if (!answer.Releases())
        {
            var why = answer switch
            {
                ApprovalAnswer.Denied => "a person refused this change",
                ApprovalAnswer.Cooldown => CoolingDown(),
                _ => Explain(answer),
            };

            narrate?.Invoke($"did not change {agent}'s settings: {why}");
            return Changed(AuditDecision.Denied, answer.ToAuditMethod(), why);
        }

        if (Withdrawn(cancellationToken) is { } withdrawn)
        {
            return withdrawn;
        }

        var saved = _settings.Write(agent, edit);

        if (!saved.Holds)
        {
            narrate?.Invoke($"did not change {agent}'s settings: {saved.Reason}");
            return Changed(AuditDecision.Denied, MethodOf(saved), $"a person confirmed it, and the vault was not saved: {saved.Reason}") with
            {
                Unapplied = $"the vault was not saved: {saved.Reason}",
            };
        }

        var lifted = Lifted(agent, change);
        _narrate?.Invoke($"changed {agent}'s settings: {OnThisClock(edit)}");

        var unapplied = (written.Understood, lifted) switch
        {
            (true, null) => null,
            (true, _) => $"the vault holds it, but this computer's limits could not be lifted to match: {lifted}",
            (false, null) => $"the vault holds it, but {AccessChangePrompt.BeneathDefaultsText}",
            (false, _) => $"the vault holds it, but {AccessChangePrompt.BeneathDefaultsText}, and this computer's limits could not be lifted to match: {lifted}",
        };

        var confirmed = edit.UsesVault
            ? $"a person confirmed it, lifting what this computer held back: {string.Join("; ", prompt.Gains)}"
            : "a person confirmed it";

        return Made(
            agent,
            edit,
            origin,
            written,
            Changed(AuditDecision.Granted, AuditMethod.Prompt, unapplied is null ? confirmed : $"{confirmed}, and {unapplied}") with { Unapplied = unapplied });
    }

    /// <summary>
    /// A change as made, and for Always allow's, whether it left what it covers answered unasked here:
    /// when it did not, the reply says why, so the person who chose it is told rather than left to find
    /// each later request still asked about.
    /// </summary>
    private AccessChangeReply Made(string agent, AccessEdit edit, AccessOrigin origin, AcafRead written, AccessChangeReply reply)
    {
        if (origin != AccessOrigin.Prompt || reply is not { Decision: AuditDecision.Granted, Unapplied: null })
        {
            return reply;
        }

        var why = !TryAccess(agent, out var after, out var problem) ? Unusable(problem)
            : (edit.Project is { } scope && after.IsAutomaticIn(scope, _clock.GetUtcNow()))
                || (edit.AddsItem is { } item && after.AlwaysAllows(item))
                || (edit.AddsCommand is { } command && after.AlwaysAllows(command)) ? null
            : written.Understood ? "keypaste still asks before releasing it there"
            : AccessChangePrompt.BeneathDefaultsText;

        return why is null ? reply : reply with { Reason = $"{reply.Reason}, but {why}", Unapplied = why };
    }

    /// <summary>
    /// Resets this computer's ceilings when <c>clients.toml</c> holds what keypaste cannot parse, which
    /// refuses every agent request: only once the person confirms, having been shown what each agent then
    /// gets beyond the defaults, is the file set aside beside it and an empty one started.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What became of it.</returns>
    /// <remarks>
    /// A file that could not be read at all is left as it is, since what it holds is not known and may be
    /// well formed; one that can be used now has nothing to reset. The person's own, so it neither waits on
    /// a cooldown nor starts one.
    /// </remarks>
    public async ValueTask<AccessChangeReply> ResetCapsAsync(CancellationToken cancellationToken)
    {
        switch (_settings.ProbeCaps(out var problem))
        {
            case CapsFile.Usable:
                return Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, "this computer's limits can be read, so there was nothing to reset");
            case CapsFile.Unreadable:
                return Changed(
                    AuditDecision.Denied,
                    AuditMethod.Failed,
                    $"keypaste could not read this computer's limits, so it left the file as it is: {problem}; fix what keeps it from being read, or move it away yourself");
        }

        var prompt = AccessChangePrompt.ForReset(_settings.List(), _clock.LocalTimeZone, ShownItem);
        var answer = await _gate.AskAsync([], prompt, coolUnanswered: false, cancellationToken).ConfigureAwait(false);

        if (!answer.Releases())
        {
            return Changed(AuditDecision.Denied, answer.ToAuditMethod(), answer == ApprovalAnswer.Denied ? "a person refused this change" : Explain(answer));
        }

        if (Withdrawn(cancellationToken) is { } withdrawn)
        {
            return withdrawn;
        }

        return _settings.TryResetCaps(out var kept, out problem) switch
        {
            CapsChange.Written => Changed(
                AuditDecision.Granted,
                AuditMethod.Prompt,
                $"a person confirmed it: every limit this computer kept on agents was cleared, and the file keypaste could not read was kept beside it as {Path.GetFileName(kept)}; {string.Join("; ", prompt.Gains)}") with
            {
                Kept = kept,
            },
            CapsChange.Unchanged => Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, "this computer's limits could be read again, so nothing was reset"),
            _ => Changed(AuditDecision.Denied, AuditMethod.Failed, $"a person confirmed it, and this computer's limits were not reset: {problem}"),
        };
    }

    /// <summary>
    /// What an agent's record would read as with a change made to it, for a change the vault cannot
    /// take, so that what it would still lower on this computer is judged as its record would be.
    /// </summary>
    private static AcafRead Unwritten(string agent, AgentRecords records, AccessEdit edit)
    {
        var own = AgentNames.IsNamed(agent) ? records.Own : records.Any;
        var start = own.Count > 0 ? own[0] : records.Any.Count > 0 ? records.Any[0] : null;

        return new AcafRead(edit.Apply(start?.Access ?? AgentAccess.Defaults), own.All(record => record.Understood), true)
        {
            Said = edit.Apply(start?.Said ?? AgentAccess.Defaults),
        };
    }

    /// <summary>
    /// Applies a change that gives an agent nothing more: to this computer's ceilings first, then, unless
    /// the agent asked, to the vault, which takes a person's narrowing even when the ceilings cannot.
    /// </summary>
    private AccessChangeReply Narrow(
        string agent,
        AccessEdit edit,
        AgentRecords records,
        AccessChange change,
        AccessOrigin origin,
        Action<string>? narrate)
    {
        var unkept = Lowered(agent, records, change);

        if (origin == AccessOrigin.Agent)
        {
            if (unkept is not null)
            {
                narrate?.Invoke($"did not change {agent}'s settings: {unkept}");
                return Changed(AuditDecision.Denied, AuditMethod.Failed, unkept);
            }

            _narrate?.Invoke($"changed {agent}'s settings: {OnThisClock(edit)}");
            return Changed(AuditDecision.Granted, AuditMethod.Narrowing, "applied at once on this computer, since it gives the agent nothing more");
        }

        var saved = _settings.Write(agent, edit);

        if (!saved.Holds && unkept is not null)
        {
            narrate?.Invoke($"did not change {agent}'s settings: {unkept}; {saved.Reason}");
            return Changed(AuditDecision.Denied, MethodOf(saved), $"{unkept}, and the vault was not saved: {saved.Reason}");
        }

        _narrate?.Invoke($"changed {agent}'s settings: {OnThisClock(edit)}");

        var applied = (saved.Holds, unkept) switch
        {
            (true, null) => "applied at once, since it gives the agent nothing more",
            (true, _) => $"applied at once in the vault, since it gives the agent nothing more, but {unkept}, so a restored or merged older copy of the vault could undo it here",
            _ => $"applied at once on this computer, since it gives the agent nothing more, and the vault was not saved: {saved.Reason}",
        };

        return Changed(AuditDecision.Granted, AuditMethod.Narrowing, applied);
    }

    /// <summary>Lowers this computer's ceilings for a narrowing, as the file holds them now; null when they hold it, otherwise why not.</summary>
    private string? Lowered(string agent, AgentRecords records, AccessChange change)
    {
        try
        {
            return _settings.TryChangeCaps(
                caps =>
                {
                    var lowered = caps.Narrowed(agent, EffectiveAccess.Create(agent, records, caps), change);

                    // Also where it takes away what a record not wholly understood says, so it holds once that record is fixed or an older one is merged back.
                    return change.Said is { } said ? lowered.Narrowed(agent, said, change) : lowered;
                },
                out var error)
                ? null
                : $"this computer's limits could not be written: {error}";
        }
        catch (ArgumentException tooMany)
        {
            return $"this computer's limits could not hold it: {tooMany.Message}";
        }
    }

    /// <summary>
    /// Lifts this computer's ceilings as far as a confirmed change goes, from what the file holds once the
    /// person has answered, so a ceiling written while they were asked stays; null when that was done, otherwise why not.
    /// </summary>
    private string? Lifted(string agent, AccessChange change)
    {
        try
        {
            return _settings.TryChangeCaps(caps => caps.Lifted(agent, change), out var error) ? null : error;
        }
        catch (ArgumentException tooMany)
        {
            return tooMany.Message;
        }
    }

    /// <summary>A change as the operator's terminal reads it, its end on this computer's clock.</summary>
    private string OnThisClock(AccessEdit edit) => edit.OnThisClock(edit.ToString(), _clock.LocalTimeZone);

    private static AuditMethod MethodOf(AgentSave saved) => saved.Outcome switch
    {
        AgentSaveOutcome.Locked => AuditMethod.VaultLocked,
        AgentSaveOutcome.ChangedOnDisk => AuditMethod.VaultChanged,
        _ => AuditMethod.Failed,
    };

    private static AccessChangeReply Changed(AuditDecision decision, AuditMethod method, string reason) =>
        new(decision, method, reason) { Unapplied = decision == AuditDecision.Denied ? reason : null };

    /// <summary>Refuses, before anything is written, a change withdrawn while the person was asked.</summary>
    private static AccessChangeReply? Withdrawn(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Changed(AuditDecision.Denied, AuditMethod.Cancelled, "the request was withdrawn before anything was changed")
            : null;

    /// <summary>Why an agent request was refused when its settings could not be read.</summary>
    private static string Unusable(string problem) => $"agent settings are not usable: {problem}";

    /// <summary>Tells the person who chose Always allow that the change it asked for did not take effect.</summary>
    /// <param name="notice">What to tell them, from <see cref="AlwaysNotice.For"/>.</param>
    public void Tell(AlwaysNotice notice) => _gate.Tell(notice);

    /// <summary>What a person is being asked about now.</summary>
    /// <returns>The waiting request, if any, carrying no value.</returns>
    public ApproverActivity Activity() =>
        new(_gate.Waiting is { } waiting ? [waiting] : [])
        {
            WaitingChanges = _gate.WaitingChange is { } change ? [change] : [],
            WaitingEdits = _gate.WaitingEdit is { } edit ? [edit] : [],
        };

    /// <summary>Hands back a release the peer can actually be sent, or a refusal saying why not.</summary>
    /// <param name="granted">The release, already authorized and already read.</param>
    /// <param name="display">The sanitized entry, for the refusal and the terminal.</param>
    /// <param name="narration">What to tell the operator when the release does leave.</param>
    /// <param name="askedUnder">The cooldown a person was asked under, which an undeliverable release cools, or null where nobody was.</param>
    /// <returns><paramref name="granted"/>, or a denial under <see cref="AuditMethod.Undeliverable"/>.</returns>
    /// <remarks>
    /// <para>
    /// The check is <see cref="ApproverProtocol"/>'s, because only the writer knows what a value
    /// costs once escaped. Asking here as well as there is not a second answer to one question — it
    /// is the same function and the same budget — it is that <b>this method narrates</b>, and a line
    /// saying a credential was released before the reply has left the process is a claim nothing
    /// else in keypaste would ever contradict. The owner records the reply after this, and checks it
    /// again as it will be sent.
    /// </para>
    /// <para>
    /// The refusal names which authority the release had, so an Automatic release is never written up
    /// as a person's (THREATS.md T-16).
    /// </para>
    /// </remarks>
    private CredentialReply Deliver(CredentialReply granted, string display, string narration, string? askedUnder = null)
    {
        if (!ApproverProtocol.Fits(granted))
        {
            _narrate?.Invoke($"could not deliver {display}: the reply is larger than one message");

            // The size belongs to the entry, so every retry would end the same way, and asking again for each is T-11 with a lever attached.
            if (askedUnder is not null)
            {
                _gate.CoolDown(askedUnder);
            }

            return Refused(
                AuditMethod.Undeliverable, ApproverProtocol.UndeliverableReason(granted.Method), display);
        }

        _narrate?.Invoke(narration);

        return granted;
    }

    /// <summary>
    /// Refuses a release whose field could not be read, naming a stale vault as that, and a placeholder that
    /// cannot be filled in by its field under <see cref="AuditMethod.Unfillable"/>.
    /// </summary>
    /// <remarks>
    /// A placeholder refusal cools the field down as a person's refusal does: asking again ends the same way
    /// until somebody edits the item, so each retry would otherwise put the same question to a person for nothing.
    /// </remarks>
    private CredentialReply Unread(CredentialFailure failure, string cooldownKey, string field, string unfilled, string display, string client)
    {
        if (IsStale(failure))
        {
            return Refused(AuditMethod.VaultChanged, Stale(failure, display), display);
        }

        if (failure != CredentialFailure.Unfilled)
        {
            return Refused(AuditMethod.Failed, Explain(failure), display);
        }

        var reason = unfilled.Length > 0 ? $"the item's {field} {unfilled}" : Explain(failure);

        _gate.CoolDown(cooldownKey);
        _narrate?.Invoke($"refused {display} for {client}: {reason}; edit the item so keypaste can fill it in");

        return Refused(AuditMethod.Unfillable, reason, display);
    }

    private static bool IsStale(CredentialFailure failure) =>
        failure is CredentialFailure.ChangedOnDisk or CredentialFailure.Unsaved;

    /// <summary>Says why a vault that no longer matches its file answered nothing.</summary>
    /// <param name="failure">Which way it no longer matches.</param>
    /// <param name="what">What was refused, for the operator's terminal.</param>
    /// <returns>The refusal's reason.</returns>
    private string Stale(CredentialFailure failure, string what)
    {
        _narrate?.Invoke(failure == CredentialFailure.ChangedOnDisk
            ? $"refused {what}: another program changed the vault file, so nothing is served from the copy unlocked here; restart keypaste approve to serve what the file holds now"
            : $"refused {what}: {Explain(failure)}");

        return Explain(failure);
    }

    /// <summary>Refuses, before anything is read, a request withdrawn while it was being decided.</summary>
    /// <remarks>
    /// A lock withdraws requests through this token, so after one nothing more is decrypted
    /// for them. The owner's commit check, not this one, is what guarantees nothing is released.
    /// </remarks>
    private static CredentialReply? Withdrawn(string display, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Refused(AuditMethod.Cancelled, "the request was withdrawn before anything was read", display)
            : null;

    /// <summary>Why a request for more was not put to the person, and when it can be asked again.</summary>
    private string CoolingDown() => _gate.CoolingDown("make it in the app's own agent settings, which do not wait");

    /// <summary>A value's lifetime as a reply carries it, rounded up so a code with part of a second left is not told it has none.</summary>
    private static int WholeSeconds(TimeSpan lifetime) => (int)Math.Ceiling(lifetime.TotalSeconds);

    private static CredentialReply Refused(AuditMethod method, string reason, string? entry = null) => new()
    {
        Decision = AuditDecision.Denied,
        Method = method,
        Reason = reason,
        Entry = entry,
    };

    private static string Explain(ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => "a person refused this request",
        ApprovalAnswer.TimedOut => "nobody answered inside the window",
        ApprovalAnswer.Cancelled => "the agent gave up before anybody answered",
        ApprovalAnswer.Busy => "another prompt was already waiting for an answer",
        ApprovalAnswer.Cooldown => "the same request was refused a moment ago",
        ApprovalAnswer.NoChannel => "there was nowhere to ask a person",
        _ => "asking a person went wrong",
    };

    private static string Explain(CredentialFailure failure) => failure switch
    {
        CredentialFailure.VaultLocked => "no vault is unlocked",
        CredentialFailure.NotFound => "no item answers to that name",
        CredentialFailure.Ambiguous => "more than one item answers to that name",
        CredentialFailure.NoSuchField => "the field asked for is not one keypaste releases",
        CredentialFailure.Empty => "the item has nothing in that field",
        CredentialFailure.ChangedOnDisk =>
            "another program changed the vault file since keypaste read it; nothing is released until keypaste merges that save or a person reloads the vault",
        CredentialFailure.Unsaved => "the vault has a change that is not saved yet",
        CredentialFailure.Unfilled => "the item's field holds a KeePass placeholder keypaste does not fill in",
        CredentialFailure.NoCode => "the item has no TOTP keypaste can compute",
        _ => "the vault could not be read",
    };
}
