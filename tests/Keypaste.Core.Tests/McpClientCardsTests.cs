using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// The Agents screen's rows: agents attached now, seen in this vault's log or named by a row of <c>clients.toml</c>, the AI
/// tools found on this computer, each sharing the row of the agent of its id, and every other agent last.
/// </summary>
public sealed class McpClientCardsTests
{
    private const string _vault = "0123456789abcdef";
    private static readonly DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ConnectedClient Connected(string id, string? name, string? label, int minutesAgo = 0) =>
        new(id, name, "1.0", label, _now.AddMinutes(-60), _now.AddMinutes(-minutesAgo));

    private static McpToolFinding Found(string id, McpEntryPresence presence, string? problem = null) => new(
        McpClientCatalog.Find(id)!,
        new McpClientDetection(McpClientPlatform.Linux, Found: true, Program: null, "/home/person/tool.json", problem),
        problem is null ? new McpEntryReading(presence, "/home/person/tool.json", presence == McpEntryPresence.Unknown ? "/home/person/tool.json is not JSON" : null) : null);

    /// <summary>The cards, every other agent's always last.</summary>
    private static IReadOnlyList<McpClientCard> Cards(
        IReadOnlyList<ConnectedClient> connected,
        IReadOnlyList<AuditEntry> audit,
        AccessCaps? caps = null,
        IReadOnlyList<McpToolFinding>? findings = null)
    {
        var cards = McpClientCards.Build(connected, audit, _vault, caps ?? AccessCaps.Empty, findings ?? [], _now);

        Assert.Equal((AgentNames.Any, McpClientCards.EveryOtherAgent), (cards[^1].Key, cards[^1].Name));
        Assert.Equal(AgentNames.Any, cards[^1].Label);
        return cards;
    }

    /// <summary>The cards before every other agent's.</summary>
    private static IReadOnlyList<McpClientCard> Agents(
        IReadOnlyList<ConnectedClient> connected,
        IReadOnlyList<AuditEntry> audit,
        AccessCaps? caps = null,
        IReadOnlyList<McpToolFinding>? findings = null) =>
        [.. Cards(connected, audit, caps, findings).SkipLast(1)];

    private static AuditEntry Line(string label, string name, int hoursAgo, string vault = _vault, string tool = "request_credential") => new()
    {
        Line = 1,
        At = _now.AddHours(-hoursAgo),
        Label = label,
        Name = name,
        Client = label.Length > 0 ? label : name,
        Tool = tool,
        Vault = vault,
        Decision = "granted",
    };

    [Fact]
    public void AConnectedBridge_IsConnected()
    {
        var card = Assert.Single(Agents([Connected("c1", "Claude Code", "claude-code")], []));
        Assert.Equal(("claude-code", ClientStatus.Connected, 1, "MCP stdio"), (card.Key, card.Status, card.Connections, card.Kind));
        Assert.Empty(card.Ceiling.Rows);
    }

    [Fact]
    public void ATokenRun_IsNoMcpClient()
    {
        var token = Line(string.Empty, "keypaste run --token", 1, tool: "run") with { Method = "token" };

        Assert.Empty(Agents([], [token]));
    }

    /// <summary>The owner records every <c>keypaste run --session</c> request under the runner's name, refused ones included: no card is a runner's.</summary>
    [Fact]
    public void ASessionRun_IsNoMcpClient_ButALabelledBridgeOfThatName_Is()
    {
        var runs = new[] { "prompt", "grant-cache", "invalid-request", "no-session" }
            .Select(method => Line(string.Empty, RequestRecords.SessionRunner, 1, tool: "run") with { Method = method });
        var labelled = Line("claude-code", RequestRecords.SessionRunner, 1, tool: "run") with { Method = "prompt" };

        Assert.Empty(Agents([], [.. runs]));
        Assert.Equal("claude-code", Assert.Single(Agents([], [labelled])).Key);
    }

    [Fact]
    public void SeenOnlyInTheLog_IsIdle_WithLastSeen()
    {
        var card = Assert.Single(Agents([], [Line("cursor", "Cursor", 5), Line("cursor", "Cursor", 2)]));
        Assert.Equal(ClientStatus.Idle, card.Status);
        Assert.Equal(_now.AddHours(-2), card.LastSeen);
    }

    [Fact]
    public void AnotherVaultsLines_AndOldOnes_AreIgnored()
    {
        var cards = Agents(
            [],
            [Line("elsewhere", "X", 1, vault: "ffffffffffffffff"), Line("ancient", "Y", 24 * 40), Line("listed", "Z", 1, tool: "run --token")]);

        Assert.Empty(cards);
    }

    [Fact]
    public void ACeilingRowWithoutActivity_IsListed()
    {
        var caps = Caps(
            new CapsRow { Label = "cursor", Mode = AgentMode.Ask },
            new CapsRow { Label = "cursor", Project = Scope("billing"), Mode = AgentMode.Ask },
            new CapsRow { Label = AgentNames.Any, Values = false });

        var card = Assert.Single(Agents([], [], caps));

        Assert.Equal(("cursor", ClientStatus.Idle), (card.Key, card.Status));
        Assert.Equal(2, card.Ceiling.Rows.Count);
        Assert.True(card.Ceiling.Values);
        Assert.Null(card.LastSeen);
    }

    [Fact]
    public void UnlabeledClients_AreKeyedByName_AndHeldToTheStarRow()
    {
        var caps = Caps(new CapsRow { Label = AgentNames.Any, Values = false });

        var card = Assert.Single(Agents([Connected("c1", "Zed", null)], [], caps));

        Assert.Equal("name:Zed", card.Key);
        Assert.Null(card.Label);
        Assert.False(card.Ceiling.Values);
    }

    [Fact]
    public void Count_IsDistinctClients()
    {
        Assert.Equal(2, McpClientCards.Count([Connected("c1", "Claude", "cc"), Connected("c2", "Claude", "cc"), Connected("c3", "Zed", null)]));
        Assert.Equal(0, McpClientCards.Count([]));
    }

    /// <summary>
    /// Agents in use now, then the most recently asked, then found tools not yet used, connected first and in catalogue
    /// order, then agents with settings alone, then every other agent.
    /// </summary>
    [Fact]
    public void Ordering_InUseFirst_ThenMostRecentlyAsked_ThenFoundTools_ThenSettingsAlone_ThenEveryOtherAgent()
    {
        var cards = Cards(
            [Connected("c1", "Zed", "zed")],
            [Line("older", "Older", 10), Line("newer", "Newer", 1)],
            Caps(new CapsRow { Label = "quiet", Mode = AgentMode.Ask }),
            [Found("cursor", McpEntryPresence.Absent), Found("codex", McpEntryPresence.Absent), Found("vscode", McpEntryPresence.Present)]);

        Assert.Equal(["zed", "newer", "older", "vscode", "codex", "cursor", "quiet", AgentNames.Any], cards.Select(card => card.Key));
    }

    [Fact]
    public void EveryOtherAgent_IsAlwaysLast_HeldToTheStarRows()
    {
        var caps = Caps(new CapsRow { Label = AgentNames.Any, Values = false }, new CapsRow { Label = "cursor", Mode = AgentMode.Ask });

        var alone = Assert.Single(Cards([], [], caps: AccessCaps.Empty));
        var everyOther = Cards([Connected("c1", "Zed", "zed")], [], caps)[^1];

        Assert.Null(alone.LastSeen);
        Assert.Equal((ClientStatus.Idle, 0), (everyOther.Status, everyOther.Connections));
        Assert.False(everyOther.Ceiling.Values);
        Assert.Equal(AgentNames.Any, Assert.Single(everyOther.Ceiling.Rows).Label);
        Assert.Null(everyOther.Tool);
        Assert.Null(everyOther.Presence);
    }

    [Fact]
    public void AFoundTool_IsListedUnderItsId_SayingWhetherItHoldsKeypaste()
    {
        var cards = Agents(
            [],
            [],
            findings:
            [
                Found("claude-code", McpEntryPresence.Present),
                Found("codex", McpEntryPresence.Absent),
                Found("cursor", McpEntryPresence.Unknown),
                Found("vscode", McpEntryPresence.Absent, problem: "VSCODE_PORTABLE is set"),
            ]).ToDictionary(card => card.Key);

        Assert.Equal(["claude-code", "codex", "cursor", "vscode"], cards.Keys.Order(StringComparer.Ordinal));
        Assert.All(cards.Values, card => Assert.Equal((card.Key, card.Key, ClientStatus.Idle), (card.Tool!.Id, card.Label!, card.Status)));
        Assert.Equal(("Claude Code", McpToolState.Connected), (cards["claude-code"].Name, cards["claude-code"].Presence!.State));
        Assert.Equal(McpToolState.NotConnected, cards["codex"].Presence!.State);
        Assert.Equal(new McpToolPresence(McpToolState.CannotTell, "/home/person/tool.json is not JSON"), cards["cursor"].Presence);
        Assert.Equal(new McpToolPresence(McpToolState.Problem, "VSCODE_PORTABLE is set"), cards["vscode"].Presence);
    }

    /// <summary>A tool and the agent named by its id are one row: the bridge's activity and the tool's presence together.</summary>
    [Fact]
    public void AFoundTool_AndTheAgentOfItsId_AreOneRow()
    {
        var card = Assert.Single(Agents(
            [Connected("c1", "Claude Code", "claude-code")],
            [Line("claude-code", "Claude Code", 3)],
            findings: [Found("claude-code", McpEntryPresence.Present)]));

        Assert.Equal(("claude-code", ClientStatus.Connected, McpToolState.Connected), (card.Key, card.Status, card.Presence!.State));
        Assert.Equal("claude-code", card.Tool!.Id);
    }

    /// <summary>An agent named for a tool that was not found keeps the tool, so its row is the tool's, but says nothing of its presence.</summary>
    [Fact]
    public void AnAgentNamedForAToolNotFound_HasTheToolButNoPresence()
    {
        var cards = Agents([], [Line("cursor", "Cursor", 2), Line("Cursor", "Cursor", 1), Line("my-script", "script", 1)]).ToDictionary(card => card.Key);

        Assert.Equal("cursor", cards["cursor"].Tool!.Id);
        Assert.Null(cards["cursor"].Presence);
        Assert.Null(cards["Cursor"].Tool);
        Assert.Null(cards["my-script"].Tool);
    }

    /// <summary>An agent whose settings the vault holds has its row though it has not asked, and <c>*</c> and an unnamed label add none.</summary>
    [Fact]
    public void AnAgentWithSettingsInTheVault_HasARow()
    {
        var cards = McpClientCards.Build([], [Line("seen", "Zed", hoursAgo: 1)], _vault, AccessCaps.Empty, [], _now, ["vault-only", "seen", AgentNames.Any, ""]);

        Assert.Equal(["seen", "vault-only", AgentNames.Any], cards.Select(card => card.Key));
        Assert.Equal(("vault-only", ClientStatus.Idle, (DateTimeOffset?)null), (cards[1].Label, cards[1].Status, cards[1].LastSeen));
    }
}
