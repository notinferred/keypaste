using Keypaste.Core.Audit;

namespace Keypaste.Core.Clients;

/// <summary>Whether a client is attached now.</summary>
public enum ClientStatus
{
    /// <summary>At least one of its bridges is attached to the live session.</summary>
    Connected = 0,

    /// <summary>Seen in this vault's audit lines or named by a policy row, and not attached now.</summary>
    Idle = 1,
}

/// <summary>One MCP client as the Agents screen shows it: never a value.</summary>
/// <param name="Key">Its label, or <c>name:</c> and the name it gave when it has no label.</param>
/// <param name="Name">What a person reads.</param>
/// <param name="Label">Its <c>--client-label</c>, or null.</param>
/// <param name="Kind">How it reaches keypaste.</param>
/// <param name="Status">Whether it is attached now.</param>
/// <param name="Connections">How many of its bridges are attached now.</param>
/// <param name="LastSeen">When it last asked for anything, or null when never.</param>
/// <param name="Policy">The policy its requests are held to.</param>
public sealed record McpClientCard(
    string Key,
    string Name,
    string? Label,
    string Kind,
    ClientStatus Status,
    int Connections,
    DateTimeOffset? LastSeen,
    ClientPolicy Policy);

/// <summary>
/// The MCP clients of one vault: those attached now, those its audit lines name, and those a policy
/// row names (D-0361). What a client says about itself decides nothing (THREATS.md T-3).
/// </summary>
public static class McpClientCards
{
    /// <summary>How far back an audit line makes a client worth listing.</summary>
    public static readonly TimeSpan SeenWindow = TimeSpan.FromDays(30);

    /// <summary>How a client that reaches keypaste through a bridge is described.</summary>
    public const string StdioKind = "MCP stdio";

    private static readonly HashSet<string> _bridgeTools = new(["list_entry_names", "request_credential", "run"], StringComparer.Ordinal);

    /// <summary>Builds the cards.</summary>
    /// <param name="connected">The clients attached to the live session.</param>
    /// <param name="audit">Audit entries read so far.</param>
    /// <param name="vaultKey">The vault's identity key; lines of other vaults are ignored.</param>
    /// <param name="policies">The policies in force.</param>
    /// <param name="now">The time the window is measured from.</param>
    /// <returns>Connected clients first, then the most recently seen, then by key.</returns>
    public static IReadOnlyList<McpClientCard> Build(
        IReadOnlyList<ConnectedClient> connected,
        IReadOnlyList<AuditEntry> audit,
        string vaultKey,
        ClientPolicies policies,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(vaultKey);
        ArgumentNullException.ThrowIfNull(policies);

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
            // A token run names itself like a client but has no bridge; matched on the method because
            // a bridge may assert any client name.
            if (!_bridgeTools.Contains(entry.Tool)
                || string.Equals(entry.Method, "token", StringComparison.Ordinal)
                || !string.Equals(entry.Vault, vaultKey, StringComparison.Ordinal)
                || entry.At is not { } at
                || now - at > SeenWindow
                || KeyOf(entry.Label, entry.Name) is not { } key)
            {
                continue;
            }

            Card(key, Nonempty(entry.Label), entry.Name).Seen(at);
        }

        foreach (var row in policies.Rows.Where(row => !string.Equals(row.Label, ClientPolicies.AnyClient, StringComparison.Ordinal)))
        {
            Card(row.Label, row.Label, null);
        }

        return
        [
            .. cards.Values
                .Select(draft => new McpClientCard(
                    draft.Key,
                    (draft.Label is { } label ? McpClientCatalog.Find(label)?.DisplayName : null) ?? draft.Name ?? draft.Label ?? draft.Key,
                    draft.Label,
                    StdioKind,
                    draft.Connections > 0 ? ClientStatus.Connected : ClientStatus.Idle,
                    draft.Connections,
                    draft.LastSeen,
                    policies.For(draft.Label)))
                .OrderBy(card => card.Status)
                .ThenByDescending(card => card.LastSeen ?? DateTimeOffset.MinValue)
                .ThenBy(card => card.Key, StringComparer.Ordinal),
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

    private sealed class Draft(string key, string? label)
    {
        internal string Key { get; } = key;

        internal string? Label { get; } = label;

        internal string? Name { get; set; }

        internal int Connections { get; set; }

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
