using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// What a second tool call gets while the first one is still in front of a person: a lane of its own to the owner, whose
/// queue it waits in (F.80), until every lane this bridge may open is busy, when the next comes back busy at once.
/// </summary>
/// <remarks>
/// <para>
/// Through one real MCP connection, which is the whole point of the file: the bridge must not queue a call in front of the
/// owner's queue, where the bound per agent is kept (THREATS.md T-11), and a test that asks the gate directly cannot see
/// what the bridge does with a second call.
/// </para>
/// <para>
/// The cancellation and disconnection halves of F.3b are in <c>ApproverConnectionTests</c>
/// instead: the SDK client does not carry its own cancellation through to the server's token, so
/// driving them from this end would test nothing. <c>OneLinePerRequestTests</c> sends the
/// cancellation notification itself.
/// </para>
/// </remarks>
public sealed class ConcurrentRequestsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>How long a call gets to come back, or to reach the owner, before it counts as having been held up in the bridge.</summary>
    /// <remarks>
    /// Generous on purpose. The assertion is "promptly rather than behind a human", and a human is
    /// bounded by <c>ApprovalLimits.Window</c> at forty-five seconds, so ten seconds separates the
    /// two answers without making the test a stopwatch.
    /// </remarks>
    private static readonly TimeSpan _promptly = TimeSpan.FromSeconds(10);

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static List<string> MethodsOf(McpHarness harness) =>
        [.. harness.AuditLines().Select(line =>
        {
            using var parsed = JsonDocument.Parse(line);
            return parsed.RootElement.GetProperty("method").GetString()!;
        })];

    private static Dictionary<string, object?> Credential(string entry) =>
        new()
        {
            ["entry"] = entry,
            ["field"] = "password",
            ["reason"] = "deploy the billing service to staging",
        };

    /// <summary>Waits until a call has genuinely reached the approver and parked there.</summary>
    private static async Task HoldingAsync(FakeApprover approver)
    {
        try
        {
            await approver.Entered.Task.WaitAsync(_promptly, Token);
        }
        catch (TimeoutException)
        {
            Assert.Fail("the first request never reached the approver.");
        }
    }

    /// <summary>
    /// Awaits a call that must not be waiting on a person, and says so when it is.
    /// </summary>
    /// <remarks>
    /// Every answer this file asserts on has to arrive while a prompt is still open. Awaiting one
    /// directly would hang the run rather than fail it — which is exactly what the unfixed bridge
    /// does — and a suite that hangs is one nobody finishes reading.
    /// </remarks>
    private static async Task<CallToolResult> PromptlyAsync(Task<CallToolResult> call, string queued)
    {
        var winner = await Task.WhenAny(call, Task.Delay(_promptly, Token));

        Assert.True(winner == call, $"{queued}.");

        return await call;
    }

    /// <summary>Waits until the owner has received this many requests, each parked there.</summary>
    private static async Task ReachedAsync(FakeApprover approver, int count)
    {
        var deadline = DateTime.UtcNow + _promptly;

        while (approver.Received.Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"only {approver.Received.Count} of {count} requests reached the owner.");
            await Task.Delay(20, Token);
        }
    }

    private static Task<CallToolResult> Call(ModelContextProtocol.Client.McpClient client, string entry) =>
        client.CallToolAsync(ToolText.CredentialToolName, Credential(entry), cancellationToken: Token).AsTask();

    /// <summary>A second request while a prompt is open goes to the owner on a lane of its own, which queues it, and both are answered.</summary>
    [Fact]
    public async Task ASecondRequestWhileAPromptIsOpen_ReachesTheOwnerAndIsAnswered()
    {
        await using var harness = new McpHarness();
        harness.Approver.Hold = true;
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        var first = Call(client, "env/dev/FIRST");
        await HoldingAsync(harness.Approver);
        var second = Call(client, "env/dev/SECOND");
        await ReachedAsync(harness.Approver, 2);

        harness.Approver.Held.TrySetResult();

        Assert.False((await first).IsError);
        Assert.False((await second).IsError);
    }

    /// <summary>A listing while a request is open goes on another lane and is answered, since it asks nobody.</summary>
    [Fact]
    public async Task AListingWhileARequestIsOpen_IsAnswered()
    {
        await using var approver = new FakeApprover();
        approver.Names = new NamesReply(true, [new ListedEntry(new EntryName("env/dev", "STRIPE_KEY"), ["password"], [])], string.Empty, true);
        approver.Hold = true;
        approver.StartApproving();

        await using var harness = new McpHarness(approver.PipeName);
        var client = await harness.StartAsync();

        var held = Call(client, "env/dev/STRIPE_KEY");
        await HoldingAsync(approver);

        var listing = await PromptlyAsync(
            client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token).AsTask(),
            "the listing waited for the prompt");

        Assert.False(listing.IsError);
        Assert.Contains("STRIPE_KEY", TextOf(listing), StringComparison.Ordinal);

        approver.Held.TrySetResult();
        await held;
    }

    /// <summary>
    /// Once every lane carries a request, the next is refused as busy at once and recorded, never queued in the bridge; and
    /// the lanes come back once those are answered, so a busy bridge is not a wedged one.
    /// </summary>
    [Fact]
    public async Task WithEveryLaneBusy_TheNextIsRefusedAtOnce_AndTheLanesComeBack()
    {
        await using var harness = new McpHarness();
        harness.Approver.Hold = true;
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        List<Task<CallToolResult>> held = [.. Enumerable.Range(0, ApproverConnection.Lanes).Select(lane => Call(client, $"env/dev/HELD{lane}"))];
        await ReachedAsync(harness.Approver, ApproverConnection.Lanes);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var refusal = await PromptlyAsync(Call(client, $"env/dev/SPAM{attempt}"), $"refusal {attempt} was held up in the bridge");

            Assert.True(refusal.IsError);
            Assert.Contains("BUSY", TextOf(refusal), StringComparison.Ordinal);
        }

        Assert.Equal(3, MethodsOf(harness).Count(method => method == "busy"));
        Assert.Equal(ApproverConnection.Lanes, harness.Approver.Received.Count);

        harness.Approver.Held.TrySetResult();
        Assert.All(await Task.WhenAll(held), result => Assert.False(result.IsError));
        harness.Approver.Hold = false;

        var after = await PromptlyAsync(Call(client, "env/dev/AFTER"), "the bridge was wedged by its busy lanes");

        Assert.False(after.IsError);
        Assert.Contains(FakeApprover.Sentinel, TextOf(after), StringComparison.Ordinal);

        foreach (var line in harness.AuditLines().Where(line => line.Contains("\"method\":\"busy\"", StringComparison.Ordinal)))
        {
            Assert.Contains("\"decision\":\"denied\"", line, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeApprover.Sentinel, line, StringComparison.Ordinal);
        }
    }
}
