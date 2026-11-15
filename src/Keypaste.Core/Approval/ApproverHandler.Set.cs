using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

// A linked set: fields of one item and of the items find_access links to it, asked about whole and released whole or not at all.
public sealed partial class ApproverHandler
{
    /// <summary>Decides an agent's linked set, always asking a person, and releases every field or none.</summary>
    /// <param name="request">What the agent asked for, its anchor first.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The decision, and the values only when the person allowed the set once.</returns>
    /// <remarks>
    /// The order is the single request's, for every field: resolve, re-check what the agent may see, check each item is the anchor or
    /// one <see cref="AccessIndex.LinksOf"/> links to it among the items the exposure admits, refuse the whole set for any field a
    /// category blocks or a refusal holds back, then ask, whatever the rules allow, and read last. No rule releases a set (PRODUCT §3.2).
    /// </remarks>
    public async ValueTask<SetReply> RequestSetAsync(SetRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        if (SetRequestRules.Check(request.Items, request.Reason) is { } problem)
        {
            return SetRefused(AuditMethod.InvalidRequest, $"the request's {problem.Argument} {problem.Rule}", []);
        }

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out var globError))
        {
            return SetRefused(AuditMethod.Failed, $"this bridge's --expose is not usable: {globError}", []);
        }

        if (!_rules.TryRead(out var rules, out var unusable))
        {
            _narrate?.Invoke($"refused a set: {Unusable(unusable)}");
            return SetRefused(AuditMethod.Failed, Unusable(unusable), []);
        }

        List<(EntryName Entry, string Field)> asked = [];
        List<string> shown = [];

        foreach (var item in request.Items)
        {
            if (!_source.TryResolve(item.Entry, out var name, out var failure))
            {
                return failure == CredentialFailure.Ambiguous
                    ? SetRefused(AuditMethod.OutOfScope, "more than one item answers to a name in the set; a set names each item by its handle", shown)
                    : ForSet(Unresolved(failure), shown);
            }

            shown.Add(ApprovalPrompt.Shown(name));

            if (Within(exposure, name, item.Field) is { } outside)
            {
                return SetRefused(AuditMethod.OutOfScope, outside, shown);
            }

            asked.Add((name, item.Field));
        }

        if (asked.Distinct().Count() != asked.Count)
        {
            return SetRefused(AuditMethod.InvalidRequest, "the request's items must name each field once", shown);
        }

        if (Unlinked(exposure, rules, asked, shown) is { } unlinked)
        {
            return unlinked;
        }

        var agent = AgentNames.IsNamed(request.ClientLabel) ? request.ClientLabel : null;

        foreach (var (entry, field) in asked)
        {
            var category = Category(entry, field);
            var decided = rules.Decide(agent, _source.ItemId(entry), field, category);

            if (decided.Rule == CategoryRule.Block)
            {
                var blocked = $"the set asks for the {field} of {ApprovalPrompt.Shown(entry)}, and {Blocked(category, decided.Level)}";
                _narrate?.Invoke($"refused a set: {blocked}");
                return SetRefused(AuditMethod.Blocked, blocked, shown);
            }
        }

        List<string> cooldownKeys = [.. asked.Select(field => ApprovalGate.FieldKey(EntryHandle.For(field.Entry), field.Field)).Distinct(StringComparer.Ordinal)];

        if (cooldownKeys.Any(_gate.IsInCooldown))
        {
            _narrate?.Invoke($"refused a set: {Explain(ApprovalAnswer.Cooldown)}");
            return SetRefused(AuditMethod.Cooldown, Explain(ApprovalAnswer.Cooldown), shown);
        }

        var prompt = SetPrompt.For(request.ClientName, request.ClientLabel, asked, request.Reason);
        var answer = await _gate.AskAsync(cooldownKeys, prompt, cancellationToken, ApprovalGate.Asker(request.ClientLabel, connectionId)).ConfigureAwait(false);
        var client = prompt.Label ?? prompt.Client;

        if (!answer.Releases())
        {
            _narrate?.Invoke($"refused a set of {asked.Count} fields for {client}: {Explain(answer)}");
            return SetRefused(answer.ToAuditMethod(), Explain(answer), shown);
        }

        if (Withdrawn(shown[0], cancellationToken) is { } withdrawn)
        {
            return ForSet(withdrawn, shown);
        }

        return Released(asked, cooldownKeys, shown, client);
    }

    /// <summary>Why the bridge does not let the agent name this field of this item, or null when it does.</summary>
    private string? Within(EntryExposure exposure, EntryName name, string field)
    {
        var reach = exposure.Reach(name, _source.Tags(name));

        return reach == ExposureReach.None ? "the item is outside what this agent can see through this bridge"
            : !EntryExposure.Permits(reach, field) ? "this agent reaches the item through a project tag, which lets it see only the item's project variables"
            : null;
    }

    /// <summary>
    /// Refuses a set naming an item that is neither its anchor nor one a search that found the anchor links to it, among the items the
    /// exposure admits and under the Settings that decide what a search shows; null when every item is one of those.
    /// </summary>
    private SetReply? Unlinked(EntryExposure exposure, AgentRules rules, List<(EntryName Entry, string Field)> asked, List<string> shown)
    {
        if (!_lister.TryIndex(exposure, rules.ShowsUsername, rules.ShowsLinkedHosts, out var index, out var failure))
        {
            return IsStale(failure)
                ? SetRefused(AuditMethod.VaultChanged, Stale(failure, "a set"), shown)
                : SetRefused(failure == CredentialFailure.VaultLocked ? AuditMethod.VaultLocked : AuditMethod.Failed, Explain(failure), shown);
        }

        var anchor = asked[0].Entry;
        var admitted = AccessIndex.Admitted(index, exposure);

        if (admitted.FirstOrDefault(entry => entry.Listing.Name.Equals(anchor)) is not { } indexed)
        {
            return SetRefused(AuditMethod.OutOfScope, "the item is outside what this agent can see through this bridge", shown);
        }

        var links = AccessIndex.LinksOf(indexed, admitted);

        foreach (var (entry, _) in asked)
        {
            if (!entry.Equals(anchor) && !links.Any(link => link.Name.Equals(entry)))
            {
                var reason = $"{ApprovalPrompt.Shown(entry)} is not linked to {shown[0]}, the set's first item, as find_access links items";
                _narrate?.Invoke($"refused a set: {reason}");
                return SetRefused(AuditMethod.OutOfScope, reason, shown);
            }
        }

        return null;
    }

    /// <summary>Reads every field once the person allowed the set, refusing the whole set if any one cannot be read or the reply cannot carry them all.</summary>
    private SetReply Released(List<(EntryName Entry, string Field)> asked, List<string> cooldownKeys, List<string> shown, string client)
    {
        List<ReleasedField> read = [];

        try
        {
            List<SetField> fields = [];

            for (var index = 0; index < asked.Count; index++)
            {
                var (entry, field) = asked[index];
                var key = ApprovalGate.FieldKey(EntryHandle.For(entry), field);

                if (!_source.TryRead(entry, field, out var released, out var failure, out var unfilled))
                {
                    return ForSet(Unread(failure, key, field, unfilled, shown[index], client), shown);
                }

                read.Add(released);
                fields.Add(new SetField
                {
                    Entry = shown[index],
                    Field = field,
                    Value = released.Value.ToString(),
                    CodeSeconds = released.CurrentFor is { } current ? WholeSeconds(current) : 0,
                    Item = _source.ItemId(entry),
                });
            }

            var reply = new SetReply
            {
                Decision = AuditDecision.Granted,
                Method = AuditMethod.Prompt,
                Reason = "a person approved this one set",
                Fields = fields,
                Entries = shown,
            };

            if (!ApproverProtocol.Fits(reply))
            {
                _narrate?.Invoke($"could not deliver a set of {asked.Count} fields: the reply is larger than one message");

                // The size belongs to the items, so asking again ends the same way (T-11).
                foreach (var key in cooldownKeys)
                {
                    _gate.CoolDown(key);
                }

                return SetRefused(AuditMethod.Undeliverable, ApproverProtocol.UndeliverableReason(AuditMethod.Prompt), shown);
            }

            _narrate?.Invoke($"released a set of {asked.Count} fields ({string.Join(", ", fields.Select(field => $"{field.Entry} {field.Field}"))}) to {client} once");
            return reply;
        }
        finally
        {
            foreach (var released in read)
            {
                released.Dispose();
            }
        }
    }

    private static SetReply ForSet(CredentialReply refused, IReadOnlyList<string> shown) =>
        SetRefused(refused.Method, refused.Reason, shown);

    private static SetReply SetRefused(AuditMethod method, string reason, IReadOnlyList<string> shown) => new()
    {
        Decision = AuditDecision.Denied,
        Method = method,
        Reason = reason,
        Entries = [.. shown],
    };
}
