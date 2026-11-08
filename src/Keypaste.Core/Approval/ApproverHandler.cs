using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>
/// The approver's decision procedure: resolve, re-scope, decide by the field's category, honour a refusal,
/// release under an Allow or ask a human, release one field.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the core with no I/O of its own — no terminal, no dialog, no pipe — so every
/// ordering that matters is reachable from a unit test. What it talks to are four seams: where
/// entries are resolved and read, where names come from, where a human is, and the rules on this
/// computer, which say how each category of field is answered for every agent.
/// </para>
/// <para>
/// <b>The order is the security property.</b> Resolve, then re-check what the bridge lets the agent
/// see, then refuse a Blocked category, then honour a refusal a person just gave, then release under
/// Allow, and only then ask a person; read the field last of all. Reading earlier would put a secret
/// in memory for requests that are about to be refused, and asking earlier would let an agent raise a
/// prompt for an entry it was never allowed to name.
/// </para>
/// <para>
/// A person answers Deny or Allow once. A passkey's fields are Blocked under every rule. The rules are
/// this computer's, never a vault's, so an agent record a vault carries releases nothing unasked.
/// </para>
/// </remarks>
public sealed class ApproverHandler
{
    private readonly ICredentialSource _source;
    private readonly IEntryNameLister _lister;
    private readonly ApprovalGate _gate;
    private readonly IAgentRules _rules;
    private readonly Action<string>? _narrate;
    private readonly Func<EntryName, bool> _requiresLiveApproval;

    /// <summary>Builds the handler over its four seams.</summary>
    /// <param name="source">Where entries are resolved and one field is read.</param>
    /// <param name="lister">Where names come from. Deliberately not the same seam.</param>
    /// <param name="gate">Where a human is asked, and where the deadline lives.</param>
    /// <param name="rules">The rules every agent's request is decided by; <see cref="IAgentRules.Defaults"/> asks about every value.</param>
    /// <param name="narrate">Optional: a line of running commentary for the operator's terminal.</param>
    /// <param name="requiresLiveApproval">
    /// Which entries a scoped token may never release, a protected environment's; null means the
    /// source's own answer (<see cref="ICredentialSource.RequiresLiveApproval"/>).
    /// </param>
    /// <exception cref="ArgumentNullException">Any of the four seams is null.</exception>
    public ApproverHandler(
        ICredentialSource source,
        IEntryNameLister lister,
        ApprovalGate gate,
        IAgentRules rules,
        Action<string>? narrate = null,
        Func<EntryName, bool>? requiresLiveApproval = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(rules);

        _source = source;
        _lister = lister;
        _gate = gate;
        _rules = rules;
        _narrate = narrate;
        _requiresLiveApproval = requiresLiveApproval ?? source.RequiresLiveApproval;
    }

    /// <summary>Whether a release of this entry is asked about live every time, by the rule a scoped token is held to.</summary>
    /// <param name="name">The entry.</param>
    /// <returns><see langword="true"/> when no token may release it.</returns>
    public bool RequiresLiveApproval(EntryName name) => _requiresLiveApproval(name);

    /// <summary>An entry's UUID as hex, which a line names it by.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>The UUID, or null when it cannot be read.</returns>
    public string? ItemId(EntryName name) => _source.ItemId(name);

    /// <summary>An entry's own tags, as the exposure and the categories read them.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its tags, or none when they cannot be read.</returns>
    public IReadOnlyList<string> Tags(EntryName name) => _source.Tags(name);

    /// <summary>The rules on this computer now.</summary>
    /// <param name="rules">The rules, when they could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>False only when the rules file cannot be read, which refuses every agent request.</returns>
    public bool TryRules([NotNullWhen(true)] out AgentRules? rules, out string problem) => _rules.TryRead(out rules, out problem);

    /// <summary>Which category one field of an entry falls in.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="field">The field as a request or a run's set names it.</param>
    /// <returns>Its category, read without reading the field's value.</returns>
    public FieldCategory Category(EntryName name, string field)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        return FieldCategories.Of(field, !CredentialFields.IsStandard(field) && _source.IsProtectedField(name, field), _source.Tags(name));
    }

    /// <summary>Which entry names may be shown to an agent: those its bridge's exposure reaches, while Settings shows item details.</summary>
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

        if (!_rules.TryRead(out var rules, out var problem))
        {
            _narrate?.Invoke($"refused a listing: {Unusable(problem)}");
            return ValueTask.FromResult(new NamesReply(false, [], Unusable(problem), true) { Refusal = AuditMethod.Failed });
        }

        if (!rules.ShowsDetails)
        {
            return ValueTask.FromResult(
                new NamesReply(false, [], "the person hides item details from agents; ask for an item by its name", true) { Refusal = AuditMethod.Blocked });
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
                .Select(entry => (Entry: entry, Reach: exposure.Reach(entry.Name, entry.Tags)))
                .Where(listed => listed.Reach != ExposureReach.None)
                .Select(listed => listed.Entry.Within(listed.Reach)),
        ];

        return ValueTask.FromResult(new NamesReply(true, seen, string.Empty, true));
    }

    /// <summary>Decides one credential request, asking a human if its category says to.</summary>
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

        if (!_rules.TryRead(out var rules, out var unusable))
        {
            _narrate?.Invoke($"refused a request: {Unusable(unusable)}");
            return Refused(AuditMethod.Failed, Unusable(unusable));
        }

        if (!_source.TryResolve(request.Entry, out var name, out var failure))
        {
            if (failure == CredentialFailure.Ambiguous && _source.TryChoose(request.Entry, out var items, out failure))
            {
                return await PickAsync(request, rules, exposure, items, cancellationToken).ConfigureAwait(false);
            }

            return Unresolved(failure);
        }

        return await DecideAsync(request, rules, exposure, name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A request that named no single item refused, saying why.</summary>
    /// <remarks>
    /// A name that resolves to nothing and one outside what the agent sees get the same answer:
    /// telling them apart would let an agent enumerate entries in parts of the vault it was
    /// never allowed to see (THREATS.md T-4). The audit line's reason still records which.
    /// </remarks>
    private CredentialReply Unresolved(CredentialFailure failure) => failure switch
    {
        CredentialFailure.VaultLocked => Refused(AuditMethod.VaultLocked, "no vault is unlocked"),
        _ when IsStale(failure) => Refused(AuditMethod.VaultChanged, Stale(failure, "a request")),
        _ => Refused(AuditMethod.OutOfScope, Explain(failure)),
    };

    /// <summary>Whether the bridge lets the agent name this field of this item.</summary>
    private bool Reaches(EntryExposure exposure, EntryName name, string field) =>
        EntryExposure.Permits(exposure.Reach(name, _source.Tags(name)), field);

    /// <summary>Decides a request one item answers to, from the re-check of what the agent can see on.</summary>
    private async ValueTask<CredentialReply> DecideAsync(
        CredentialRequest request,
        AgentRules rules,
        EntryExposure exposure,
        EntryName name,
        CancellationToken cancellationToken)
    {
        // The re-check that makes handles safe. The bridge can test a path against its globs
        // before forwarding, but it cannot resolve a handle or read a tag without the vault, so
        // without this a handle would be the way around what the agent may see.
        var reach = exposure.Reach(name, _source.Tags(name));

        if (reach == ExposureReach.None)
        {
            return Refused(AuditMethod.OutOfScope, "the item is outside what this agent can see through this bridge");
        }

        if (!EntryExposure.Permits(reach, request.Field))
        {
            return Refused(AuditMethod.OutOfScope, "this agent reaches the item through a project tag, which lets it see only the item's project variables");
        }

        var display = ApprovalPrompt.Shown(name);
        var itemId = _source.ItemId(name);
        var category = Category(name, request.Field);
        var rule = rules.RuleFor(category);

        if (rule == CategoryRule.Block)
        {
            _narrate?.Invoke($"refused {display}: {Blocked(category)}");
            return Refused(AuditMethod.Blocked, Blocked(category), display) with { Item = itemId };
        }

        var cooldownKey = ApprovalGate.FieldKey(EntryHandle.For(name), request.Field);

        // Before Allow, not after. A person who refused this exact request a moment ago has said
        // something more specific and more recent than any rule, and a rule must never resurrect it.
        if (_gate.IsInCooldown(cooldownKey))
        {
            _narrate?.Invoke($"refused {display}: {Explain(ApprovalAnswer.Cooldown)}");
            return Refused(AuditMethod.Cooldown, Explain(ApprovalAnswer.Cooldown), display);
        }

        if (rule == CategoryRule.Allow)
        {
            return Unasked(request, name, itemId, display, cooldownKey, category, cancellationToken);
        }

        var prompt = ApprovalPrompt.For(request.ClientName, name, request.Field, request.Reason, request.ClientLabel) with { Item = itemId };
        var answer = await _gate.AskAsync(cooldownKey, prompt, cancellationToken).ConfigureAwait(false);

        return Answered(request, name, itemId, display, cooldownKey, prompt.Client, answer, "a person approved this one request", cancellationToken);
    }

    /// <summary>
    /// Asks the person to pick which of the items an agent's request names it gets, when more than one it can see does:
    /// the agent did not name one item, so nothing is released unasked, whatever the rule for the field.
    /// </summary>
    /// <remarks>
    /// Only the items the agent can see whose field is not Blocked are offered, so one it cannot get is never shown to be
    /// there; when that leaves one, the request is decided as one naming it. Exactly the picked item's field is read, by its
    /// UUID, once the person allows it.
    /// </remarks>
    private async ValueTask<CredentialReply> PickAsync(
        CredentialRequest request,
        AgentRules rules,
        EntryExposure exposure,
        IReadOnlyList<AnsweringItem> items,
        CancellationToken cancellationToken)
    {
        List<AnsweringItem> seen = [.. items.Where(item => Reaches(exposure, item.Name, request.Field))];
        List<AnsweringItem> offered = [.. seen.Where(item => rules.RuleFor(Category(item.Name, request.Field)) != CategoryRule.Block)];

        switch (offered.Count)
        {
            case 0 when seen.Count > 0:
                return Refused(AuditMethod.Blocked, "every item answering to that name holds the field in a category the person blocks for agents");
            case 0:
                return Refused(AuditMethod.OutOfScope, "the item is outside what this agent can see through this bridge");
            case 1:
                return await DecideAsync(request, rules, exposure, offered[0].Name, cancellationToken).ConfigureAwait(false);
            case > ApprovalPrompt.MaximumCandidates:
                return Refused(AuditMethod.OutOfScope, $"more than {ApprovalPrompt.MaximumCandidates} items this agent can see answer to that name, too many to pick from; a person can rename or move some");
        }

        var shared = offered[0].Name;
        var display = ApprovalPrompt.Shown(shared);
        var cooldownKey = ApprovalGate.FieldKey(EntryHandle.For(shared), request.Field);

        if (_gate.IsInCooldown(cooldownKey))
        {
            _narrate?.Invoke($"refused {display}: {Explain(ApprovalAnswer.Cooldown)}");
            return Refused(AuditMethod.Cooldown, Explain(ApprovalAnswer.Cooldown), display);
        }

        List<ItemCandidate> candidates = [.. offered.Select(Candidate)];
        var prompt = ApprovalPrompt.For(request.ClientName, shared, request.Field, request.Reason, request.ClientLabel) with { Candidates = candidates };
        var pick = await _gate.AskPickingAsync(cooldownKey, prompt, cancellationToken).ConfigureAwait(false);
        var picked = pick.Picked is { } index ? candidates[index] : null;

        return Answered(
            request,
            picked?.Name ?? shared,
            picked?.Item,
            display,
            cooldownKey,
            prompt.Client,
            pick.Answer,
            "a person picked this item and approved this one request",
            cancellationToken);
    }

    /// <summary>One item on a picker.</summary>
    private static ItemCandidate Candidate(AnsweringItem answering) => new()
    {
        Name = answering.Name,
        Item = answering.Name.Uuid!,
        Title = EntryNameSanitizer.Sanitize(answering.Name.Title).Text,
        Group = EntryNameSanitizer.SanitizePath(answering.Name.GroupPath).Text,
        Username = DisplayTextSanitizer.Sanitize(answering.Username).Text,
        Url = DisplayTextSanitizer.Sanitize(answering.Url).Text,
    };

    /// <summary>A prompted request once the person has answered.</summary>
    private CredentialReply Answered(
        CredentialRequest request,
        EntryName name,
        string? itemId,
        string display,
        string cooldownKey,
        string client,
        ApprovalAnswer answer,
        string approved,
        CancellationToken cancellationToken)
    {
        if (!answer.Releases())
        {
            _narrate?.Invoke($"refused {display} for {client}: {Explain(answer)}");
            return Refused(answer.ToAuditMethod(), Explain(answer), display) with { Item = itemId };
        }

        if (Withdrawn(display, cancellationToken) is { } withdrawn)
        {
            return withdrawn with { Item = itemId };
        }

        // Last of all. Nothing has decrypted a field until a person said yes to this exact request.
        if (!_source.TryRead(name, request.Field, out var released, out var readFailure, out var unfilled))
        {
            return Unread(readFailure, cooldownKey, request.Field, unfilled, display, client) with { Item = itemId };
        }

        using (released)
        {
            return Deliver(
                new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = approved,
                    Entry = display,
                    Item = itemId,
                    CodeSeconds = released.CurrentFor is { } current ? WholeSeconds(current) : 0,
                    Value = released.Value.ToString(),
                },
                display,
                $"released {display} to {client} once",
                cooldownKey);
        }
    }

    /// <summary>Releases a field nobody is asked about, because its category is Allow: item details shown, or a rule the person confirmed.</summary>
    /// <remarks>
    /// Its line names the category, and never a person, since nobody saw this request. The read is still
    /// last of all, exactly as on the prompted path.
    /// </remarks>
    private CredentialReply Unasked(
        CredentialRequest request,
        EntryName name,
        string? itemId,
        string display,
        string cooldownKey,
        FieldCategory category,
        CancellationToken cancellationToken)
    {
        var client = ApprovalPrompt.For(request.ClientName, name, request.Field, string.Empty, request.ClientLabel).Client;

        if (Withdrawn(display, cancellationToken) is { } withdrawn)
        {
            return withdrawn with { Item = itemId };
        }

        if (!_source.TryRead(name, request.Field, out var released, out var readFailure, out var unfilled))
        {
            return Unread(readFailure, cooldownKey, request.Field, unfilled, display, client) with { Item = itemId };
        }

        var why = category == FieldCategory.Detail
            ? "released without asking: agents see item details"
            : $"released without asking: the person allows {FieldCategories.Shown(category)} for every agent";

        using (released)
        {
            // One line on the operator's terminal either way, and the only live signal that this
            // happened at all: no prompt was drawn and nobody was asked (THREATS.md T-12).
            return Deliver(
                new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Allowed,
                    Reason = why,
                    Entry = display,
                    Item = itemId,
                    CodeSeconds = released.CurrentFor is { } current ? WholeSeconds(current) : 0,
                    Value = released.Value.ToString(),
                },
                display,
                $"released {display} to {client} without asking ({FieldCategories.Shown(category)} allowed)");
        }
    }

    /// <summary>Why a request for a Blocked category was refused.</summary>
    /// <param name="category">The field's category.</param>
    /// <returns>The reason.</returns>
    public static string Blocked(FieldCategory category) => category == FieldCategory.Passkey
        ? "a passkey's fields are never released to agents"
        : $"the person blocks {FieldCategories.Shown(category)} for agents";

    /// <summary>Why an agent request was refused when the rules could not be read.</summary>
    private static string Unusable(string problem) => $"agent rules are not usable: {problem}";

    /// <summary>What a person is being asked about now.</summary>
    /// <returns>The waiting request, if any, carrying no value.</returns>
    public ApproverActivity Activity() =>
        new(_gate.Waiting is { } waiting ? [waiting] : [])
        {
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
    /// The refusal names which authority the release had, so a release under a rule is never written up
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
