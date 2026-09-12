using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// A tool call that arrives while the handshake is still being processed waits for it instead of
/// being told the client never introduced itself (docs/STEPS.md F.5).
/// </summary>
/// <remarks>
/// <para>
/// The ordering this exists for cannot be produced in process. Every test here drives a real
/// <c>McpClient</c>, which performs the handshake and waits for its response, so the message that
/// overtakes it never can — which is exactly why the defect reached a release runner twice and was
/// once written off as a client-ordering requirement in <c>verify-mcp-stdio.sh</c>'s comments.
/// </para>
/// <para>
/// So what is tested is the waiting, through a predicate that flips the way <c>ClientInfo</c> does.
/// Delete the loop in <see cref="McpAudit.AwaitCompletionAsync"/> and the first test here goes red;
/// the second says the budget still runs out, which is what keeps a client that sent nothing
/// refused rather than waited for forever.
/// </para>
/// </remarks>
public sealed class HandshakeGraceTests
{
    [Fact]
    public async Task AnIdentityThatArrivesLate_IsWaitedFor()
    {
        var asked = 0;

        var complete = await McpAudit.AwaitCompletionAsync(
            () => ++asked >= 3,
            McpAudit.HandshakeGrace,
            TimeProvider.System,
            TestContext.Current.CancellationToken);

        Assert.True(complete);
        Assert.True(asked >= 3, $"the predicate was asked {asked} times, so nothing waited");
    }

    [Fact]
    public async Task AnIdentityThatNeverArrives_StillRefuses()
    {
        var grace = TimeSpan.FromMilliseconds(120);
        var started = TimeProvider.System.GetTimestamp();

        PoolTimeline.Mark("grace-enter");
        var complete = await McpAudit.AwaitCompletionAsync(
            () => false,
            grace,
            TimeProvider.System,
            TestContext.Current.CancellationToken);
        PoolTimeline.Mark("grace-exit");

        Assert.False(complete);

        // It waited rather than returning at once, and it stopped rather than waiting forever.
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        Assert.True(elapsed >= grace, $"gave up after {elapsed.TotalMilliseconds}ms of a {grace.TotalMilliseconds}ms budget");
        Assert.True(elapsed < grace + TimeSpan.FromSeconds(5), $"waited {elapsed.TotalMilliseconds}ms, far past its budget");
    }

    [Fact]
    public async Task AnIdentityAlreadyKnown_CostsNoWait()
    {
        var started = TimeProvider.System.GetTimestamp();

        var complete = await McpAudit.AwaitCompletionAsync(
            () => true,
            McpAudit.HandshakeGrace,
            TimeProvider.System,
            TestContext.Current.CancellationToken);

        Assert.True(complete);
        Assert.True(
            TimeProvider.System.GetElapsedTime(started) < McpAudit.HandshakeGrace,
            "the ordinary call paid the grace period it was not supposed to need");
    }

    [Fact]
    public async Task ACallerThatGivesUp_GetsTheAnswerSoFarRatherThanAnException()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var complete = await McpAudit.AwaitCompletionAsync(
            () => false,
            McpAudit.HandshakeGrace,
            TimeProvider.System,
            cancelled.Token);

        Assert.False(complete);
    }
}
