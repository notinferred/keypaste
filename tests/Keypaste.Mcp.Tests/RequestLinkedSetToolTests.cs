using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary><c>request_linked_set</c> over a real MCP client and a fake owner on a real pipe: a malformed set never reaches the owner, and a released one carries every field or none.</summary>
public sealed class RequestLinkedSetToolTests
{
    private const string _first = "SET_SENTINEL_FIRST_7d21";
    private const string _second = "SET_SENTINEL_SECOND_7d21";

    private static readonly string _stripe = EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<CallToolResult> AskAsync(McpClient client, object? items, string reason = "rotate the billing webhook") =>
        client.CallToolAsync(ToolText.SetToolName, new Dictionary<string, object?> { ["items"] = items, ["reason"] = reason }, cancellationToken: Token).AsTask();

    private static Dictionary<string, object?>[] Items(params (string Entry, string Field)[] items) =>
        [.. items.Select(item => new Dictionary<string, object?> { ["entry"] = item.Entry, ["field"] = item.Field })];

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static SetReply Released(params (string Entry, string Field, string Value)[] fields) => new()
    {
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "a person approved this one set",
        Fields = [.. fields.Select(field => new SetField { Entry = field.Entry, Field = field.Field, Value = field.Value })],
    };

    [Fact]
    public async Task TheTool_IsListedWithItsSchema_AndNeverAsReadOnly()
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync();

        var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), each => each.Name == ToolText.SetToolName).ProtocolTool;

        Assert.False(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.IdempotentHint);
        Assert.False(tool.Annotations.DestructiveHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        var items = tool.InputSchema.GetProperty("properties").GetProperty("items");
        Assert.Equal((2, 4), (items.GetProperty("minItems").GetInt32(), items.GetProperty("maxItems").GetInt32()));
        Assert.Equal(["entry", "field"], items.GetProperty("items").GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(["items", "reason"], tool.InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task AReleasedSet_ReturnsEveryValue_NamedAsTheAgentAskedForIt()
    {
        await using var harness = new McpHarness();
        harness.Approver.SetAnswer = Released(("env/dev/STRIPE_KEY", "password", _first), ("env/dev/STRIPE_WEBHOOK", "password", _second));
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Items((_stripe, "password"), ("env/dev/STRIPE_WEBHOOK", "password")));

        Assert.False(result.IsError);
        var text = TextOf(result);
        Assert.StartsWith(ToolText.ReleasedSet(2), text, StringComparison.Ordinal);
        Assert.Contains($"The \"password\" of {_stripe}:\n{_first}", text, StringComparison.Ordinal);
        Assert.Contains($"The \"password\" of env/dev/STRIPE_WEBHOOK:\n{_second}", text, StringComparison.Ordinal);

        var fields = result.StructuredContent!.Value.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal([(_stripe, _first), ("env/dev/STRIPE_WEBHOOK", _second)], fields.Select(field => (field.GetProperty("entry").GetString(), field.GetProperty("value").GetString())));

        var sent = Assert.Single(harness.Approver.SetsReceived);
        Assert.Equal([new SetItem(_stripe, "password"), new SetItem("env/dev/STRIPE_WEBHOOK", "password")], sent.Items);
        Assert.Equal(["env/**"], sent.Exposure);
        Assert.Equal("rotate the billing webhook", sent.Reason);
        Assert.Empty(harness.AuditLines());
    }

    public static TheoryData<string, string> Malformed => new()
    {
        { "missing", ToolText.Invalid("items", "must be an array of objects, each with an entry and a field") },
        { "one", ToolText.Invalid("items", "must name 2 to 4 fields") },
        { "five", ToolText.Invalid("items", "must name 2 to 4 fields") },
        { "attachment", ToolText.Invalid("field", "must not be an attachment; ask for one with request_attachment") },
        { "twice", ToolText.Invalid("items", "must name each field once") },
        { "extra", ToolText.Invalid("items", "must be an array of objects, each with an entry and a field") },
        { "reason", ToolText.Invalid("reason", "must be 1 to 2000 characters") },
        { "outside", ToolText.SetOutOfScope },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task AMalformedOrUnexposedSet_IsRefusedWithoutAskingTheOwner(string shape, string refusal)
    {
        await using var harness = new McpHarness();
        harness.Approver.SetAnswer = Released(("a", "password", _first), ("b", "password", _second));
        harness.Approver.Start();
        var client = await harness.StartAsync();

        object? items = shape switch
        {
            "missing" => null,
            "one" => Items((_stripe, "password")),
            "five" => Items([.. Enumerable.Range(0, 5).Select(index => (_stripe, $"F{index}"))]),
            "attachment" => Items((_stripe, "password"), (_stripe, "attachment")),
            "twice" => Items((_stripe, "password"), (_stripe, "password")),
            "extra" => new[] { new Dictionary<string, object?> { ["entry"] = _stripe, ["field"] = "password", ["value"] = "x" }, new Dictionary<string, object?> { ["entry"] = _stripe, ["field"] = "url" } },
            "outside" => Items((_stripe, "password"), ("personal/bank", "password")),
            _ => Items((_stripe, "password"), (_stripe, "url")),
        };

        var result = await AskAsync(client, items, shape == "reason" ? string.Empty : "rotate the billing webhook");

        Assert.True(result.IsError, shape);
        Assert.Equal(refusal, TextOf(result));
        Assert.Empty(harness.Approver.SetsReceived);
        Assert.DoesNotContain(_first, harness.Transcript, StringComparison.Ordinal);

        using var line = JsonDocument.Parse(Assert.Single(harness.AuditLines()));
        Assert.Equal("request_linked_set", line.RootElement.GetProperty("tool").GetString());
        Assert.Equal("denied", line.RootElement.GetProperty("decision").GetString());
        Assert.Equal(shape == "outside" ? "out-of-scope" : "invalid-request", line.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task ASetTheOwnerDidNotRecord_ReleasesNothing_AndTheBridgeRecordsIt()
    {
        await using var harness = new McpHarness();
        harness.Approver.SetAnswer = Released(("a", "password", _first), ("b", "password", _second));
        harness.Approver.Records = false;
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Items((_stripe, "password"), (_stripe, "url")));

        Assert.True(result.IsError);
        Assert.Equal(ToolText.AuditUnavailable, TextOf(result));
        Assert.DoesNotContain(_first, harness.Transcript, StringComparison.Ordinal);

        using var line = JsonDocument.Parse(Assert.Single(harness.AuditLines()));
        Assert.Equal(("request_linked_set", McpAudit.Unrecorded), (line.RootElement.GetProperty("tool").GetString(), line.RootElement.GetProperty("reason").GetString()));
    }

    /// <summary>A grant that does not carry exactly the fields asked is no grant, and the bridge records the refusal itself.</summary>
    [Fact]
    public async Task AGrantMissingAField_ReleasesNone()
    {
        await using var harness = new McpHarness();
        harness.Approver.SetAnswer = Released(("a", "password", _first), ("b", "password", _second));
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Items((_stripe, "password"), (_stripe, "url"), (_stripe, "username")));

        Assert.True(result.IsError);
        Assert.DoesNotContain(_first, TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain(_first, harness.Transcript, StringComparison.Ordinal);
        Assert.Single(harness.AuditLines());
    }

    [Theory]
    [InlineData(AuditMethod.OutOfScope)]
    [InlineData(AuditMethod.Blocked)]
    [InlineData(AuditMethod.Prompt)]
    public async Task AnOwnersRefusal_IsSaidInItsOwnWords(AuditMethod method)
    {
        await using var harness = new McpHarness();
        harness.Approver.SetAnswer = new SetReply { Decision = AuditDecision.Denied, Method = method, Reason = "refused" };
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Items((_stripe, "password"), (_stripe, "url")));

        Assert.True(result.IsError);
        Assert.Equal(method == AuditMethod.OutOfScope ? ToolText.SetOutOfScope : ToolText.Refusal(method), TextOf(result));
        Assert.Empty(harness.AuditLines());
    }

    [Fact]
    public async Task WithNobodyHoldingTheVault_TheSetIsRefusedAndRecorded()
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Items((_stripe, "password"), (_stripe, "url")));

        Assert.True(result.IsError);
        Assert.Equal(ToolText.NoApprover, TextOf(result));

        using var line = JsonDocument.Parse(Assert.Single(harness.AuditLines()));
        Assert.Equal(("request_linked_set", "no-approver"), (line.RootElement.GetProperty("tool").GetString(), line.RootElement.GetProperty("method").GetString()));
        Assert.Equal(2, line.RootElement.GetProperty("args").GetProperty("set").GetProperty("of").GetInt32());
    }
}
