using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>
/// The approver's decision procedure: resolve, re-scope, check the grant, release under the agent's
/// mode or ask a human, release one field.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the core with no I/O of its own — no terminal, no dialog, no pipe — so every
/// ordering that matters is reachable from a unit test. What it talks to are five seams: where
/// entries are resolved and read, where names come from, where a human is, what they have already
/// said yes to, and each agent's settings, which say what the agent may see and get and how it is asked.
/// </para>
/// <para>
/// <b>The order is the security property.</b> An agent given no values is refused before anything
/// is resolved, so it learns nothing of the vault from a credential request. Then resolve, then
/// re-check what the agent can see, then look for a live grant, then honour a refusal a person just
/// gave, then release under an Automatic mode, and only then ask a person; read the field last of
/// all. Reading earlier would put a secret in memory for requests that are about to be refused, and
/// asking earlier would let an agent raise a prompt for an entry it was never allowed to name.
/// </para>
/// <para>
/// <b>Where Automatic sits in that order.</b> After the reach re-check, because what an agent sees is
/// the least of the bridge's <c>--expose</c>, its settings and this computer's ceilings, and no mode
/// reaches past it. After the grant cache, because a live grant is a decision a person made about this
/// exact request and re-attributing it to a mode would be a lie in the log. After the cooldown, because
/// a person's explicit no outranks a mode. And in here rather than inside <see cref="ApprovalGate"/>,
/// because a release nobody is asked about must not be able to collide with a prompt somebody is in
/// the middle of answering.
/// </para>
/// <para>
/// A person answers once or for the timed grant the prompt offers, which lasts the approver's
/// ceiling whatever the agent asked for. An entry in a protected profile, or one whose mode is Ask
/// every time, skips the grant cache and Automatic and is offered no timed grant, so it is asked about
/// every time; a protected entry is so whatever any setting says.
/// </para>
/// </remarks>
public sealed class ApproverHandler
{
    /// <summary>The one cooldown every request for more over the pipe shares (THREATS.md T-11).</summary>
    private const string _askedForMore = "access";

    private readonly ICredentialSource _source;
    private readonly IEntryNameLister _lister;
    private readonly ApprovalGate _gate;
    private readonly GrantCache _grants;
    private readonly IAgentSettings _settings;
    private readonly HourlyLimit _hourly;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;
    private readonly Func<EntryName, bool> _requiresLiveApproval;

    /// <summary>Builds the handler over its five seams.</summary>
    /// <param name="source">Where entries are resolved and one field is read.</param>
    /// <param name="lister">Where names come from. Deliberately not the same seam.</param>
    /// <param name="gate">Where a human is asked, and where the deadline lives.</param>
    /// <param name="grants">What a human has already said yes to.</param>
    /// <param name="settings">
    /// Each agent's settings and this computer's ceilings; <see cref="IAgentSettings.Defaults"/> holds
    /// every agent to the defaults, which is what keypaste did before they existed.
    /// </param>
    /// <param name="narrate">Optional: a line of running commentary for the operator's terminal.</param>
    /// <param name="requiresLiveApproval">
    /// Which entries are asked about every time, with no grant and no Automatic release; null means the
    /// source's own answer (<see cref="ICredentialSource.RequiresLiveApproval"/>), so a host that
    /// forgets it still asks live about a protected environment, by tag.
    /// </param>
    /// <param name="hourly">
    /// The owner's hourly limits on Automatic, which outlive one unlock; null counts afresh, as a
    /// restarted owner does.
    /// </param>
    /// <param name="clock">What an Automatic limit's time is judged against, and whose time zone a confirmation shows it in; null is the system clock.</param>
    /// <exception cref="ArgumentNullException">Any of the five seams is null.</exception>
    /// <remarks>
    /// <paramref name="settings"/> is not nullable, and "every agent gets the defaults" is the value
    /// <see cref="IAgentSettings.Defaults"/>. A nullable seam makes every future reader work out what
    /// null means, and a thing with a meaning should be a value rather than an absence.
    /// </remarks>
    public ApproverHandler(
        ICredentialSource source,
        IEntryNameLister lister,
        ApprovalGate gate,
        GrantCache grants,
        IAgentSettings settings,
        Action<string>? narrate = null,
        Func<EntryName, bool>? requiresLiveApproval = null,
        HourlyLimit? hourly = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(settings);

        _source = source;
        _lister = lister;
        _gate = gate;
        _grants = grants;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        _hourly = hourly ?? new HourlyLimit(_clock);
        _narrate = narrate;
        _requiresLiveApproval = requiresLiveApproval ?? source.RequiresLiveApproval;
    }

    /// <summary>Whether a release of this entry is asked about live every time, by the rule this handler applies.</summary>
    /// <param name="name">The entry.</param>
    /// <returns><see langword="true"/> when no grant, mode or token may release it unprompted.</returns>
    /// <remarks>For the release paths beside this one, so a run naming the entry is held to the same rule (D-0371).</remarks>
    public bool RequiresLiveApproval(EntryName name) => _requiresLiveApproval(name);

    /// <summary>An entry's own tags, as the exposure and the agent's settings read them.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its tags, or none when they cannot be read.</returns>
    /// <remarks>For the release paths beside this one, so a run is held to the agent's reach as a credential request is (D-0422).</remarks>
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
    /// <param name="connectionId">Who is asking, and what any resulting grant is scoped to.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The decision, and the field value on the one path that has one.</returns>
    public async ValueTask<CredentialReply> RequestAsync(
        CredentialRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        if (CredentialRequestRules.Check(request.Entry, request.Field, request.Reason, request.TtlSeconds) is { } problem)
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

        // Before anything is resolved, so an agent given no values learns nothing of the vault from
        // a credential request, and before the grant cache, so a grant it held earlier serves nothing.
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

        var handle = EntryHandle.For(name);
        var display = ApprovalPrompt.For(request.ClientName, name, request.Field, request.Reason, 0).Entry;
        var key = new GrantKey(connectionId, handle, request.Field);
        var isProtected = _requiresLiveApproval(name);
        var item = access.Item(tags, _clock.GetUtcNow());

        // A protected profile, or an entry whose mode is Ask every time, is asked about every time:
        // no grant serves it and no mode releases it.
        var onceOnly = isProtected
            ? OnceOnly.ProtectedProfile
            : item.Mode == AgentMode.Ask ? OnceOnly.AskEveryTime : OnceOnly.None;
        var liveOnly = onceOnly != OnceOnly.None;

        // Declared before the try so the copy the cache hands out is zeroed on every path — the
        // repo's idiom for a disposable that only exists on one branch.
        ReleasedField? live = null;

        try
        {
            if (!liveOnly && _grants.TryUse(key, out live, out var remaining))
            {
                return Deliver(
                    new CredentialReply
                    {
                        Decision = AuditDecision.Granted,
                        Method = AuditMethod.GrantCache,
                        Reason = "served from an allowance a person had already given, inside its lifetime",
                        Entry = display,
                        TtlSeconds = (int)remaining.TotalSeconds,
                        Value = live.Value.ToString(),
                    },
                    display,
                    $"reused an approval for {display} ({(int)remaining.TotalSeconds}s left)");
            }
        }
        finally
        {
            live?.Dispose();
        }

        // Before Automatic, not after. A person who refused this exact request a moment ago has
        // said something more specific and more recent than any mode, and a mode must never
        // resurrect a decision they just made.
        if (_gate.IsInCooldown(CooldownKey(key)))
        {
            _narrate?.Invoke($"refused {display}: {Explain(ApprovalAnswer.Cooldown)}");
            return Refused(AuditMethod.Cooldown, Explain(ApprovalAnswer.Cooldown), display);
        }

        // A limit that is spent asks instead: the person is the fallback, never a refusal.
        if (!liveOnly && access.TryAutomatic(item, _hourly))
        {
            return Automatic(request, name, display, access.Label!, cancellationToken);
        }

        // The person chooses once or the timed grant on screen, so its length is the approver's
        // ceiling and never the agent's ttl_seconds.
        var grantSeconds = liveOnly ? 0 : _gate.Limits.MaximumTtlSeconds;
        var prompt = ApprovalPrompt.For(request.ClientName, name, request.Field, request.Reason, grantSeconds, request.ClientLabel) with
        {
            OnceOnly = onceOnly,
            AlwaysFor = isProtected || item.Mode == AgentMode.Automatic ? null : AlwaysFor(access, tags),
        };

        var (answer, kept) = await _gate.AskKeepingAsync(CooldownKey(key), prompt, cancellationToken).ConfigureAwait(false);

        try
        {
            var reply = Answered(request, name, display, key, prompt, grantSeconds, answer, cancellationToken);

            // Always for's release goes with the slot the gate kept, so its confirmation follows with
            // no other request in front of the person between them.
            if (reply.AlwaysFor is null)
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
        GrantKey key,
        ApprovalPrompt prompt,
        int grantSeconds,
        ApprovalAnswer answer,
        CancellationToken cancellationToken)
    {
        var alwaysFor = answer == ApprovalAnswer.AlwaysForProject ? prompt.AlwaysFor : null;

        // Always for allows this one request and then asks for the change; on a prompt that did not
        // offer it, it is an answer nobody could have meant, so nothing is released.
        if (answer == ApprovalAnswer.AlwaysForProject)
        {
            answer = alwaysFor is null ? ApprovalAnswer.Failed : ApprovalAnswer.ApprovedOnce;
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
        if (!_source.TryRead(name, request.Field, out var released, out var readFailure))
        {
            return Unread(readFailure, display);
        }

        using (released)
        {
            var timed = answer == ApprovalAnswer.Approved && grantSeconds > 0;

            if (timed)
            {
                // Stored even when the reply below turns out not to fit. The size belongs to the
                // entry rather than to the request, so every retry would end the same way — and
                // re-prompting for each one is THREATS.md T-11 with a lever attached. From the
                // cache, the second ask is refused without troubling anybody, and the copy is
                // zeroed at its ordinary TTL. A once answer keeps nothing, so it asks again.
                _grants.Store(key, released, TimeSpan.FromSeconds(grantSeconds), prompt);
            }

            return Deliver(
                new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = timed
                        ? $"a person approved this request for {ApprovalLimits.Describe(grantSeconds)}"
                        : "a person approved this one request",
                    Entry = display,
                    TtlSeconds = timed ? grantSeconds : 0,
                    Value = released.Value.ToString(),
                    AlwaysFor = alwaysFor,
                },
                display,
                timed
                    ? $"released {display} to {prompt.Client} for {grantSeconds}s"
                    : $"released {display} to {prompt.Client} once");
        }
    }

    /// <summary>Releases a field because the agent's mode for it is Automatic, a setting the person confirmed.</summary>
    /// <remarks>
    /// <para>
    /// <b>The approver's ceiling applies.</b> The lifetime the agent is told is its ask clamped by
    /// <see cref="ApprovalLimits"/>, the one place that arithmetic lives.
    /// </para>
    /// <para>
    /// <b>It does not seed the grant cache.</b> Doing so would serve every later request inside the
    /// TTL for free and off the hourly limit, turning a limit on releases into a limit on grants; it
    /// would hide releases two onward behind <c>grant-cache</c> lines that claim a person; and nobody
    /// is being asked twice anyway. The cost is one vault read per call, which is CPU spent to buy an
    /// accurate log.
    /// </para>
    /// <para>
    /// The read is still last of all, exactly as on the prompted path: a mode that covers the entry is
    /// not a reason to decrypt anything until the release itself is certain.
    /// </para>
    /// </remarks>
    private CredentialReply Automatic(
        CredentialRequest request,
        EntryName name,
        string display,
        string agent,
        CancellationToken cancellationToken)
    {
        var ttl = _gate.Limits.EffectiveTtlSeconds(request.TtlSeconds);

        if (Withdrawn(display, cancellationToken) is { } withdrawn)
        {
            return withdrawn;
        }

        if (!_source.TryRead(name, request.Field, out var released, out var readFailure))
        {
            return Unread(readFailure, display);
        }

        using (released)
        {
            var shown = EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text;

            // One line on the operator's terminal either way, and the only live signal that this
            // happened at all: no prompt was drawn and nobody was asked (THREATS.md T-12).
            return Deliver(
                new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Automatic,
                    Reason = $"released without asking: {shown}'s mode for this item is Automatic",
                    Entry = display,
                    TtlSeconds = ttl,
                    Value = released.Value.ToString(),
                },
                display,
                $"released {display} to {shown} without asking (Automatic)");
        }
    }

    /// <summary>
    /// The project environment a person may make Automatic from the prompt: offered for a named agent
    /// asking for an entry in exactly one project, never for a protected one, and not where Automatic
    /// already applies and only its hourly limit is spent.
    /// </summary>
    private static ProjectScope? AlwaysFor(EffectiveAccess access, IReadOnlyList<string> tags)
    {
        var members = tags.Select(ProjectTag.Read).Where(tag => tag.Kind == ProjectTagKind.Member).ToList();

        return access.Label is not null
            && members is [{ Project: { } project, Environment: { } environment }]
            && ProjectScope.TryCreate(project, environment, out var scope, out _)
                ? scope
                : null;
    }

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

    /// <summary>Changes one agent's settings, asking any confirmation in the slot Always for kept when there is one.</summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="origin">Where the request says it came from.</param>
    /// <param name="reason">The requester's own reason, or empty.</param>
    /// <param name="kept">The slot the release's prompt kept (<see cref="CredentialReply"/>), or null.</param>
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
        // Always for tells the person why where they answered, once the release it allowed has left.
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

        var prompt = AccessChangePrompt.For(agent, change, origin, reason, _clock.LocalTimeZone) with { StaysBeneathDefaults = !written.Understood };

        // Every request for more over the pipe shares one cooldown, whatever it asks for and whichever
        // agent it names, since both the name and the origin are the requester's own claim: no program
        // can keep a confirmation in front of the person by rewording, renaming or switching to a
        // terminal. The app's own settings and Always for come only from the person at the owner, never
        // over the pipe, so their refusals hold nothing back and nothing holds them back.
        var overThePipe = origin is AccessOrigin.Agent or AccessOrigin.Terminal;
        string[] cooldownKeys = overThePipe ? [_askedForMore] : [];
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
            (true, _) => $"the vault holds it, but this computer's ceilings could not be lifted to match: {lifted}",
            (false, null) => $"the vault holds it, but {AccessChangePrompt.BeneathDefaultsText}",
            (false, _) => $"the vault holds it, but {AccessChangePrompt.BeneathDefaultsText}, and this computer's ceilings could not be lifted to match: {lifted}",
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
    /// A change as made, and for Always for's, whether it left the agent Automatic for that environment
    /// here: when it did not, the reply says why, so the person who chose it is told rather than left to
    /// find each later request still asked about.
    /// </summary>
    private AccessChangeReply Made(string agent, AccessEdit edit, AccessOrigin origin, AcafRead written, AccessChangeReply reply)
    {
        if (origin != AccessOrigin.Prompt || reply is not { Decision: AuditDecision.Granted, Unapplied: null } || edit.Project is not { } scope)
        {
            return reply;
        }

        var why = !TryAccess(agent, out var after, out var problem) ? Unusable(problem)
            : after.IsAutomaticIn(scope, _clock.GetUtcNow()) ? null
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
                return Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, "this computer's ceilings can be read, so there was nothing to reset");
            case CapsFile.Unreadable:
                return Changed(
                    AuditDecision.Denied,
                    AuditMethod.Failed,
                    $"keypaste could not read this computer's ceilings, so it left the file as it is: {problem}; fix what keeps it from being read, or move it away yourself");
        }

        var prompt = AccessChangePrompt.ForReset(_settings.List(), _clock.LocalTimeZone);
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
                $"a person confirmed it: every ceiling this computer kept on agents was cleared, and the file keypaste could not read was kept beside it as {Path.GetFileName(kept)}; {string.Join("; ", prompt.Gains)}") with
            {
                Kept = kept,
            },
            CapsChange.Unchanged => Changed(AuditDecision.Denied, AuditMethod.InvalidRequest, "this computer's ceilings could be read again, so nothing was reset"),
            _ => Changed(AuditDecision.Denied, AuditMethod.Failed, $"a person confirmed it, and this computer's ceilings were not reset: {problem}"),
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
                : $"this computer's ceilings could not be written: {error}";
        }
        catch (ArgumentException tooMany)
        {
            return $"this computer's ceilings could not hold it: {tooMany.Message}";
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

    /// <summary>Tells the person who chose Always for that the change it asked for did not take effect.</summary>
    /// <param name="notice">What to tell them, from <see cref="AlwaysForNotice.For"/>.</param>
    public void Tell(AlwaysForNotice notice) => _gate.Tell(notice);

    /// <summary>What a person is being asked about now and the grants they have given that are in force.</summary>
    /// <returns>The waiting request, if any, and every live grant, none carrying a value.</returns>
    public ApproverActivity Activity() =>
        new(_gate.Waiting is { } waiting ? [waiting] : [], _grants.InForce())
        {
            WaitingChanges = _gate.WaitingChange is { } change ? [change] : [],
        };

    /// <summary>Ends one grant, so the next request it would have answered is asked again.</summary>
    /// <param name="key">The grant, as <see cref="Activity"/> listed it.</param>
    /// <remarks>No cooldown starts: a revoked grant is a decision withdrawn, not a request refused.</remarks>
    public void Revoke(GrantKey key) => _grants.Revoke(key);

    /// <summary>Ends every grant, so each request they would have answered is asked again.</summary>
    public void RevokeAll() => _grants.RevokeEntries(VaultEdit.Everything);

    /// <summary>Revokes the grants scoped to a connection that has gone.</summary>
    /// <param name="connectionId">The connection that ended.</param>
    public void Disconnected(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        _grants.Revoke(connectionId);
    }

    /// <summary>Hands back a release the peer can actually be sent, or a refusal saying why not.</summary>
    /// <param name="granted">The release, already authorized and already read.</param>
    /// <param name="display">The sanitized entry, for the refusal and the terminal.</param>
    /// <param name="narration">What to tell the operator when the release does leave.</param>
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
    /// as a person's (THREATS.md T-16). It is the one denial in keypaste that follows a yes.
    /// </para>
    /// </remarks>
    private CredentialReply Deliver(CredentialReply granted, string display, string narration)
    {
        if (!ApproverProtocol.Fits(granted))
        {
            _narrate?.Invoke($"could not deliver {display}: the reply is larger than one message");

            return Refused(
                AuditMethod.Undeliverable, ApproverProtocol.UndeliverableReason(granted.Method), display);
        }

        _narrate?.Invoke(narration);

        return granted;
    }

    /// <summary>Refuses a release whose field could not be read, naming a stale vault as that.</summary>
    private CredentialReply Unread(CredentialFailure failure, string display) =>
        IsStale(failure)
            ? Refused(AuditMethod.VaultChanged, Stale(failure, display), display)
            : Refused(AuditMethod.Failed, Explain(failure), display);

    private static bool IsStale(CredentialFailure failure) =>
        failure is CredentialFailure.ChangedOnDisk or CredentialFailure.Unsaved;

    /// <summary>Says why a vault that no longer matches its file answered nothing.</summary>
    /// <param name="failure">Which way it no longer matches.</param>
    /// <param name="what">What was refused, for the operator's terminal.</param>
    /// <returns>The refusal's reason.</returns>
    /// <remarks>
    /// A file somebody else wrote zeroes every grant: each holds a value from the copy that is now
    /// stale, and that copy is not served again until a person reloads the vault (D-0317).
    /// </remarks>
    private string Stale(CredentialFailure failure, string what)
    {
        if (failure == CredentialFailure.ChangedOnDisk)
        {
            _grants.RevokeEntries(VaultEdit.Everything);
        }

        _narrate?.Invoke(failure == CredentialFailure.ChangedOnDisk
            ? $"refused {what}: another program changed the vault file, so nothing is served from the copy unlocked here; restart keypaste agent to serve what the file holds now"
            : $"refused {what}: {Explain(failure)}");

        return Explain(failure);
    }

    /// <summary>Refuses, before anything is read, a request withdrawn while it was being decided.</summary>
    /// <remarks>
    /// A lock withdraws requests through this token (D-0313), so after one nothing more is decrypted
    /// for them. The owner's commit check, not this one, is what guarantees nothing is released.
    /// </remarks>
    private static CredentialReply? Withdrawn(string display, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Refused(AuditMethod.Cancelled, "the request was withdrawn before anything was read", display)
            : null;

    /// <summary>
    /// What counts as "the same request" for the cooldown: the same field of the same entry, whichever
    /// connection asks and whether a run reaches it, so a client that reconnects or starts another
    /// bridge is refused without a second prompt (T-11).
    /// </summary>
    private static string CooldownKey(GrantKey key) => ApprovalGate.FieldKey(key.Handle, key.Field);

    /// <summary>Why a request for more was not put to the person, and when it can be asked again.</summary>
    private string CoolingDown()
    {
        var seconds = (int)Math.Ceiling(_gate.CooldownLeft(_askedForMore).TotalSeconds);

        return "a request for more access from an agent or a terminal was refused or went unanswered a moment ago, so this one was not put to the person; "
            + $"try again in {ApprovalLimits.Describe(Math.Max(seconds, 1))}, or make it in the app's own agent settings, which do not wait";
    }

    private static CredentialReply Refused(AuditMethod method, string reason, string? entry = null) => new()
    {
        Decision = AuditDecision.Denied,
        Method = method,
        Reason = reason,
        Entry = entry,
        TtlSeconds = 0,
    };

    private static string Explain(ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => "a person refused this request",
        ApprovalAnswer.TimedOut => "nobody answered inside the window",
        ApprovalAnswer.Cancelled => "the agent gave up before anybody answered",
        ApprovalAnswer.Busy => "another request was already in front of a person",
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
            "another program changed the vault file since keypaste read it; nothing is released until a person reloads the vault",
        CredentialFailure.Unsaved => "the vault has a change that is not saved yet",
        _ => "the vault could not be read",
    };
}
