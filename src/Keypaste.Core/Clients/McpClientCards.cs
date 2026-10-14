using Keypaste.Core.Agents;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Clients;

/// <summary>Whether a client is attached now.</summary>
public enum ClientStatus
{
    /// <summary>At least one of its bridges is attached to the live session.</summary>
    Connected = 0,

    /// <summary>Seen in this vault's audit lines or named by a row of <c>clients.toml</c>, and not attached now.</summary>
    Idle = 1,
}

/// <summary>One agent or AI tool as the Agents screen shows it: never a value.</summary>
/// <param name="Key">Its label, or <c>name:</c> and the name it gave when it has no label; a found tool's is its id, and every other agent's <c>*</c>.</param>
/// <param name="Name">What a person reads.</param>
/// <param name="Label">Its <c>--client-label</c>, or null.</param>
/// <param name="Kind">How it reaches keypaste.</param>
/// <param name="Status">Whether it is attached now.</param>
/// <param name="Connections">How many of its bridges are attached now.</param>
/// <param name="LastSeen">When it last asked for anything, or null when never.</param>
/// <param name="Ceiling">This computer's ceilings on what it may get.</param>
public sealed record McpClientCard(
    string Key,
    string Name,
    string? Label,
    string Kind,
    ClientStatus Status,
    int Connections,
    DateTimeOffset? LastSeen,
    AccessCeiling Ceiling)
{
    /// <summary>The catalogue's tool this card is, when its key is a tool's id.</summary>
    public McpClient? Tool { get; init; }

    /// <summary>Whether the tool was found on this computer and holds keypaste; null when it was not found, or the card is no tool's.</summary>
    public McpToolPresence? Presence { get; init; }
}

/// <summary>
/// The agents and AI tools of one vault: the agents attached now, those its audit lines name and those a
/// row of <c>clients.toml</c> names (D-0361), the tools found on this computer, and every other agent
/// last. What an agent says about itself decides nothing (THREATS.md T-3).
/// </summary>
public static class McpClientCards
{
    /// <summary>How far back an audit line makes a client worth listing.</summary>
    public static readonly TimeSpan SeenWindow = TimeSpan.FromDays(30);

    /// <summary>How a client that reaches keypaste through a bridge is described.</summary>
    public const string StdioKind = "MCP stdio";

    /// <summary>What the card whose settings every agent without its own gets is called.</summary>
    public const string EveryOtherAgent = "Every other agent";

    private static readonly HashSet<string> _bridgeTools = new([AuditTools.List, AuditTools.Credential, AuditTools.Run], StringComparer.Ordinal);

    /// <summary>Builds the cards: each agent, each tool found on this computer, and every other agent.</summary>
    /// <param name="connected">The clients attached to the live session.</param>
    /// <param name="audit">Audit entries read so far.</param>
    /// <param name="vaultKey">The vault's identity key; lines of other vaults are ignored.</param>
    /// <param name="caps">This computer's ceilings.</param>
    /// <param name="findings">The tools <see cref="McpToolSurvey"/> found; a tool's card is keyed by its id, so an agent of that name shares it.</param>
    /// <param name="now">The time the window is measured from.</param>
    /// <param name="settings">The names the vault holds settings under, each listed though it has not asked.</param>
    /// <returns>
    /// Agents attached now, then the most recently seen, then found tools not yet used (connected first, in
    /// catalogue order), then agents with settings alone by key, then every other agent.
    /// </returns>
    public static IReadOnlyList<McpClientCard> Build(
        IReadOnlyList<ConnectedClient> connected,
        IReadOnlyList<AuditEntry> audit,
        string vaultKey,
        AccessCaps caps,
        IReadOnlyList<McpToolFinding> findings,
        DateTimeOffset now,
        IReadOnlyList<string>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(vaultKey);
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(findings);

        var cards = new Dictionary<string, Draft>(StringComparer.Ordinal);

        Draft Card(string key, string? label, string? name)
        {
            if (!cards.TryGetValue(key, out var draft))
            {
                draft = new Draft(key, label);
                cards[key] = draft;
            }

            draft.Name ??= name is { Length: > 0 } ? name : null;
            return draft;
        }

        foreach (var client in connected)
        {
            if (KeyOf(client.Label, client.Name) is not { } key)
            {
                continue;
            }

            var draft = Card(key, Nonempty(client.Label), client.Name);
            draft.Connections++;
            draft.Seen(client.LastRequestAt);
        }

        foreach (var entry in audit)
        {
            // A token run and a run --session name themselves like a client but have no bridge. The token
            // run is matched on its method, which a bridge's line never has; an unlabelled bridge asserting
            // the runner's name only hides its own card.
            if (!_bridgeTools.Contains(entry.Tool)
                || string.Equals(entry.Method, "token", StringComparison.Ordinal)
                || (entry.Label.Length == 0 && string.Equals(entry.Name, RequestRecords.SessionRunner, StringComparison.Ordinal))
                || !string.Equals(entry.Vault, vaultKey, StringComparison.Ordinal)
                || entry.At is not { } at
                || now - at > SeenWindow
                || KeyOf(entry.Label, entry.Name) is not { } key)
            {
                continue;
            }

            Card(key, Nonempty(entry.Label), entry.Name).Seen(at);
        }

        foreach (var row in caps.Rows.Where(row => !string.Equals(row.Label, AgentNames.Any, StringComparison.Ordinal)))
        {
            Card(row.Label, row.Label, null);
        }

        foreach (var name in (settings ?? []).Where(AgentNames.IsNamed))
        {
            Card(name, name, null);
        }

        foreach (var finding in findings)
        {
            Card(finding.Tool.Id, finding.Tool.Id, null).Finding = finding;
        }

        var everyOther = cards.Remove(AgentNames.Any, out var star) ? star : new Draft(AgentNames.Any, AgentNames.Any);
        everyOther.Name = EveryOtherAgent;

        McpClientCard Built(Draft draft)
        {
            return new McpClientCard(
                draft.Key,
                (draft.Label is { } label ? McpClientCatalog.Find(label)?.DisplayName : null) ?? draft.Name ?? draft.Label ?? draft.Key,
                draft.Label,
                StdioKind,
                draft.Connections > 0 ? ClientStatus.Connected : ClientStatus.Idle,
                draft.Connections,
                draft.LastSeen,
                caps.For(draft.Label))
            {
                Tool = draft.Finding?.Tool ?? McpClientCatalog.All.FirstOrDefault(known => string.Equals(known.Id, draft.Label, StringComparison.Ordinal)),
                Presence = draft.Finding?.Presence,
            };
        }

        return
        [
            .. cards.Values
                .Select(Built)
                .OrderBy(card => card.Status)
                .ThenBy(card => card.LastSeen is null)
                .ThenByDescending(card => card.LastSeen ?? DateTimeOffset.MinValue)
                .ThenBy(card => card.Presence is null)
                .ThenBy(card => card.Presence?.State != McpToolState.Connected)
                .ThenBy(card => CatalogueOrder(card.Tool))
                .ThenBy(card => card.Key, StringComparer.Ordinal),
            Built(everyOther),
        ];
    }

    /// <summary>How many distinct clients are attached.</summary>
    /// <param name="connected">The clients attached to the live session.</param>
    /// <returns>The number of distinct keys.</returns>
    public static int Count(IReadOnlyList<ConnectedClient> connected)
    {
        ArgumentNullException.ThrowIfNull(connected);

        return connected.Select(client => KeyOf(client.Label, client.Name)).OfType<string>().Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>A client's key: its label, else its name, else nothing to key it by.</summary>
    public static string? KeyOf(string? label, string? name) =>
        Nonempty(label) ?? (Nonempty(name) is { } named ? "name:" + named : null);

    private static string? Nonempty(string? text) => text is { Length: > 0 } ? text : null;

    private static int CatalogueOrder(McpClient? tool)
    {
        for (var i = 0; i < McpClientCatalog.All.Count; i++)
        {
            if (ReferenceEquals(McpClientCatalog.All[i], tool))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private sealed class Draft(string key, string? label)
    {
        internal string Key { get; } = key;

        internal string? Label { get; } = label;

        internal string? Name { get; set; }

        internal int Connections { get; set; }

        internal McpToolFinding? Finding { get; set; }

        internal DateTimeOffset? LastSeen { get; private set; }

        internal void Seen(DateTimeOffset at)
        {
            if (LastSeen is null || at > LastSeen)
            {
                LastSeen = at;
            }
        }
    }
}
