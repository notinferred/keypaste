using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>The Agents screen's MCP clients: attached now, seen in this vault's log, or named by a row of <c>clients.toml</c>.</summary>
public sealed class McpClientCardsTests
{
    private const string _vault = "0123456789abcdef";
    private static readonly DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ConnectedClient Connected(string id, string? name, string? label, int minutesAgo = 0) =>
        new(id, name, "1.0", label, _now.AddMinutes(-60), _now.AddMinutes(-minutesAgo));

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
        var cards = McpClientCards.Build([Connected("c1", "Claude Code", "claude-code")], [], _vault, AccessCaps.Empty, _now);

        var card = Assert.Single(cards);
        Assert.Equal(("claude-code", ClientStatus.Connected, 1, "MCP stdio"), (card.Key, card.Status, card.Connections, card.Kind));
        Assert.Empty(card.Ceiling.Rows);
    }

    [Fact]
    public void ATokenRun_IsNoMcpClient()
    {
        var token = Line(string.Empty, "keypaste run --token", 1, tool: "run") with { Method = "token" };

        Assert.Empty(McpClientCards.Build([], [token], _vault, AccessCaps.Empty, _now));
    }

    /// <summary>The owner records every <c>keypaste run --session</c> request under the runner's name, refused ones included: no card is a runner's.</summary>
    [Fact]
    public void ASessionRun_IsNoMcpClient_ButALabelledBridgeOfThatName_Is()
    {
        var runs = new[] { "prompt", "grant-cache", "invalid-request", "no-session" }
            .Select(method => Line(string.Empty, RequestRecords.SessionRunner, 1, tool: "run") with { Method = method });
        var labelled = Line("claude-code", RequestRecords.SessionRunner, 1, tool: "run") with { Method = "prompt" };

        Assert.Empty(McpClientCards.Build([], [.. runs], _vault, AccessCaps.Empty, _now));
        Assert.Equal("claude-code", Assert.Single(McpClientCards.Build([], [labelled], _vault, AccessCaps.Empty, _now)).Key);
    }

    [Fact]
    public void SeenOnlyInTheLog_IsIdle_WithLastSeen()
    {
        var cards = McpClientCards.Build([], [Line("cursor", "Cursor", 5), Line("cursor", "Cursor", 2)], _vault, AccessCaps.Empty, _now);

        var card = Assert.Single(cards);
        Assert.Equal(ClientStatus.Idle, card.Status);
        Assert.Equal(_now.AddHours(-2), card.LastSeen);
    }

    [Fact]
    public void AnotherVaultsLines_AndOldOnes_AreIgnored()
    {
        var cards = McpClientCards.Build(
            [],
            [Line("elsewhere", "X", 1, vault: "ffffffffffffffff"), Line("ancient", "Y", 24 * 40), Line("listed", "Z", 1, tool: "run --token")],
            _vault,
            AccessCaps.Empty,
            _now);

        Assert.Empty(cards);
    }

    [Fact]
    public void ACeilingRowWithoutActivity_IsListed()
    {
        var caps = Caps(
            new CapsRow { Label = "cursor", Mode = AgentMode.Ask },
            new CapsRow { Label = "cursor", Project = Scope("billing"), Mode = AgentMode.Ask },
            new CapsRow { Label = AgentNames.Any, Values = false });

        var card = Assert.Single(McpClientCards.Build([], [], _vault, caps, _now));

        Assert.Equal(("cursor", ClientStatus.Idle), (card.Key, card.Status));
        Assert.Equal(2, card.Ceiling.Rows.Count);
        Assert.True(card.Ceiling.Values);
        Assert.Null(card.LastSeen);
    }

    [Fact]
    public void UnlabeledClients_AreKeyedByName_AndHeldToTheStarRow()
    {
        var caps = Caps(new CapsRow { Label = AgentNames.Any, Values = false });

        var card = Assert.Single(McpClientCards.Build([Connected("c1", "Zed", null)], [], _vault, caps, _now));

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

    [Fact]
    public void Ordering_ConnectedFirst_ThenMostRecentlySeen_ThenKey()
    {
        var cards = McpClientCards.Build(
            [Connected("c1", "Zed", "zed")],
            [Line("older", "Older", 10), Line("newer", "Newer", 1)],
            _vault,
            Caps(new CapsRow { Label = "quiet", Mode = AgentMode.Ask }),
            _now);

        Assert.Equal(["zed", "newer", "older", "quiet"], cards.Select(card => card.Key));
    }
}
