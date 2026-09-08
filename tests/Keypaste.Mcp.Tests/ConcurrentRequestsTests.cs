using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// What a second tool call gets while the first one is still in front of a person.
/// </summary>
/// <remarks>
/// <para>
/// Through one real MCP connection, which is the whole point of the file. <c>ApprovalGate</c> has
/// refused a concurrent prompt since 2.2 and <c>ApprovalGateTests</c> proved it — but the bridge
/// queued requests in front of that check, so the refusal was unreachable for two calls on one
/// connection and the second became a delayed second prompt. A test that asks the gate directly
/// cannot see that, which is why STEPS V-F.3b rules one out as evidence.
/// </para>
/// <para>
/// The cancellation and disconnection halves of F.3b are in <c>ApproverConnectionTests</c>
/// instead: the SDK does not carry a client's cancellation through to the server's token here, as
/// <c>ServerToolsTests.ACallTheClientAbandons_IsStillAudited_AndCarriesNoCredentialIntoTheLog</c>
/// records, so driving them from this end would test nothing.
/// </para>
/// </remarks>
public sealed class ConcurrentRequestsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>How long a call gets to come back before it counts as having been queued.</summary>
    /// <remarks>
    /// Generous on purpose. The assertion is "promptly rather than behind a human", and a human is
    /// bounded by <c>ApprovalLimits.Window</c> at forty-five seconds, so ten seconds separates the
    /// two answers without making the test a stopwatch.
    /// </remarks>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(10);

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
            ["ttl_seconds"] = 300,
        };

    /// <summary>Waits until a call has genuinely reached the approver and parked there.</summary>
    private static async Task HoldingAsync(FakeApprover approver) =>
        await approver.Entered.Task.WaitAsync(Promptly, Token);

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
        var winner = await Task.WhenAny(call, Task.Delay(Promptly, Token));

        Assert.True(winner == call, queued);

        return await call;
    }

    /// <summary>
    /// The defect F.3b exists to close: a second request used to wait for the first to be answered.
    /// </summary>
    [Fact]
    public async Task ASecondRequestWhileAPromptIsOpen_ComesBackBusyWithoutWaitingForIt()
    {
        await using var harness = new McpHarness();
        harness.Approver.Hold = true;
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        var held = client.CallToolAsync(
            ToolText.CredentialToolName,
            Credential("env/dev/FIRST"),
            cancellationToken: Token).AsTask();

        await HoldingAsync(harness.Approver);

        var refusal = await PromptlyAsync(
            client.CallToolAsync(
                ToolText.CredentialToolName,
                Credential("env/dev/SECOND"),
                cancellationToken: Token).AsTask(),
            "the second request did not come back while the first was still open: it was queued behind the prompt");

        Assert.True(refusal.IsError);
        Assert.Contains("BUSY", TextOf(refusal), StringComparison.Ordinal);

        harness.Approver.Held.TrySetResult();
        await held;

        // The load-bearing half. A queued request eventually reaches the approver and becomes a
        // second prompt; a refused one never arrives at all.
        Assert.Single(harness.Approver.Received);
    }

    /// <summary>
    /// The refusal says it was recorded, so it has to have been. Law 3.3 does not make an exception
    /// for the calls keypaste itself turned away.
    /// </summary>
    [Fact]
    public async Task TheBusyRefusalIsRecordedInTheAuditLog()
    {
        await using var harness = new McpHarness();
        harness.Approver.Hold = true;
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        var held = client.CallToolAsync(
            ToolText.CredentialToolName,
            Credential("env/dev/FIRST"),
            cancellationToken: Token).AsTask();

        await HoldingAsync(harness.Approver);

        await PromptlyAsync(
            client.CallToolAsync(
                ToolText.CredentialToolName,
                Credential("env/dev/SECOND"),
                cancellationToken: Token).AsTask(),
            "the second request was queued behind the prompt, so there is no refusal to have recorded");

        Assert.Equal(1, MethodsOf(harness).Count(method => method == "busy"));

        harness.Approver.Held.TrySetResult();
        await held;

        // Never alongside a release: a refusal that reads as a grant is the one shape the log must
        // not be able to take.
        foreach (var line in harness.AuditLines())
        {
            using var parsed = JsonDocument.Parse(line);

            if (parsed.RootElement.GetProperty("method").GetString() == "busy")
            {
                Assert.Equal("denied", parsed.RootElement.GetProperty("decision").GetString());
                Assert.DoesNotContain(FakeApprover.Sentinel, line, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The listing shares the one pipe, so it is refused by the same rule.
    /// </summary>
    /// <remarks>
    /// Pointed at an approver of this test's own, so the names come back through the real
    /// <c>ApproverEntryNameSource</c> rather than the fake source the rest of the bridge tests use.
    /// That is the path the shipped binary runs, and the only one where a listing contends for the
    /// slot at all.
    /// </remarks>
    [Fact]
    public async Task AListingWhileARequestIsOpen_IsBusyAndIsRecorded()
    {
        await using var approver = new FakeApprover();
        approver.Names = new NamesReply(true, [new EntryName("env/dev", "STRIPE_KEY")], string.Empty, true);
        approver.Hold = true;
        approver.StartApproving();

        await using var harness = new McpHarness(approver.PipeName);
        var client = await harness.StartAsync();

        var held = client.CallToolAsync(
            ToolText.CredentialToolName,
            Credential("env/dev/STRIPE_KEY"),
            cancellationToken: Token).AsTask();

        await HoldingAsync(approver);

        var refusal = await PromptlyAsync(
            client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token).AsTask(),
            "the listing waited for the prompt instead of being refused");

        Assert.True(refusal.IsError);
        Assert.Contains("BUSY", TextOf(refusal), StringComparison.Ordinal);
        Assert.Equal(1, MethodsOf(harness).Count(method => method == "busy"));

        approver.Held.TrySetResult();
        await held;
    }

    /// <summary>
    /// Busy is a moment, not a state. Once the prompt is answered the next request is asked
    /// normally, which is what makes "wait and try again" honest advice.
    /// </summary>
    [Fact]
    public async Task AfterTheFirstRequestResolves_AFreshRequestIsAskedNormally()
    {
        await using var harness = new McpHarness();
        harness.Approver.Hold = true;
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        var held = client.CallToolAsync(
            ToolText.CredentialToolName,
            Credential("env/dev/FIRST"),
            cancellationToken: Token).AsTask();

        await HoldingAsync(harness.Approver);

        var refused = await PromptlyAsync(
            client.CallToolAsync(
                ToolText.CredentialToolName,
                Credential("env/dev/SECOND"),
                cancellationToken: Token).AsTask(),
            "the second request was queued behind the prompt");

        Assert.True(refused.IsError);

        harness.Approver.Held.TrySetResult();
        await held;

        harness.Approver.Hold = false;

        var fresh = await PromptlyAsync(
            client.CallToolAsync(
                ToolText.CredentialToolName,
                Credential("env/dev/THIRD"),
                cancellationToken: Token).AsTask(),
            "the slot was never given back, so the next request had nobody to ask");

        Assert.False(fresh.IsError);
        Assert.Contains(FakeApprover.Sentinel, TextOf(fresh), StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal never took the slot, so it must not release one either.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is a busy return placed inside the block whose
    /// <c>finally</c> releases the semaphore. Every slot here is a <c>SemaphoreSlim(1, 1)</c>, so an
    /// extra release throws rather than silently admitting two — but a bridge that throws on its
    /// second refusal is still a wedged bridge, and nothing else in the suite would notice.
    /// </remarks>
    [Fact]
    public async Task TheSlotSurvivesRefusal_SoARefusedBridgeIsNotAWedgedOne()
    {
        await using var harness = new McpHarness();
        harness.Approver.Hold = true;
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        var held = client.CallToolAsync(
            ToolText.CredentialToolName,
            Credential("env/dev/FIRST"),
            cancellationToken: Token).AsTask();

        await HoldingAsync(harness.Approver);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var refusal = await PromptlyAsync(
                client.CallToolAsync(
                    ToolText.CredentialToolName,
                    Credential("env/dev/SPAM" + attempt),
                    cancellationToken: Token).AsTask(),
                $"refusal {attempt} was queued rather than refused");

            Assert.True(refusal.IsError);
            Assert.Contains("BUSY", TextOf(refusal), StringComparison.Ordinal);
        }

        harness.Approver.Held.TrySetResult();
        await held;

        harness.Approver.Hold = false;

        var after = await PromptlyAsync(
            client.CallToolAsync(
                ToolText.CredentialToolName,
                Credential("env/dev/AFTER"),
                cancellationToken: Token).AsTask(),
            "the bridge was wedged by its own refusals");

        Assert.False(after.IsError);
    }
}
