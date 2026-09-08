using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The bridge's link to the approver, driven directly over a real named pipe.
/// </summary>
/// <remarks>
/// The concurrency refusal is asserted through a real MCP connection in
/// <see cref="ConcurrentRequestsTests"/>, because that is where STEPS V-F.3b says the evidence has
/// to come from. What is left here is the pair of properties an MCP client cannot reach from the
/// far end: the SDK does not carry a client's cancellation through to the server's token, so
/// cancellation and disconnection have to be driven at this seam or not at all.
/// </remarks>
public sealed class ApproverConnectionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(10);

    private static CredentialRequest Request(string entry = "env/dev/STRIPE_KEY") => new()
    {
        Entry = entry,
        Field = "password",
        Reason = "deploy the billing service to staging",
        TtlSeconds = 300,
        Exposure = ["env/**"],
        ClientName = "claude-code",
    };

    /// <summary>
    /// A caller that gave up must not have its request sent a second time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property guard rather than a reproduction, and worth saying which. Before F.3b a cancelled
    /// exchange fell into the reconnect-and-retry path meant for an approver that had been
    /// restarted — and what stopped the re-send was only that reconnecting observed the same
    /// cancelled token and failed. Nothing named that, so a later change passing
    /// <c>CancellationToken.None</c> to the reconnect would have quietly turned an abandoned
    /// request into a prompt nobody was waiting for, raised on a fresh connection whose id scopes a
    /// different grant and a different cooldown.
    /// </para>
    /// <para>
    /// The connection is now finished explicitly instead: the reply to the abandoned exchange may
    /// still be in flight and a frame stopped mid-write may be on the wire, and the protocol has
    /// nothing to correlate a reply by, so reusing that stream is how one request's answer becomes
    /// another's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACancelledExchange_DoesNotSendTheRequestAgain()
    {
        await using var approver = new FakeApprover();
        approver.Hold = true;
        approver.StartApproving();

        await using var connection = new ApproverConnection(approver.PipeName);

        using var giveUp = new CancellationTokenSource();

        var call = connection.RequestAsync(Request(), giveUp.Token).AsTask();

        await approver.Entered.Task.WaitAsync(Promptly, Token);

        await giveUp.CancelAsync();

        var (reply, _) = await call.WaitAsync(Promptly, Token);

        Assert.Null(reply);

        approver.Held.TrySetResult();

        // Proving a negative, so it is given time to happen rather than checked instantly. A
        // re-send would arrive on the heels of the release above.
        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);

        Assert.Single(approver.Received);
    }

    /// <summary>
    /// An approver that has stopped is reported as absent, not as a failure, because the refusal
    /// for the first names the command that fixes it and the refusal for the second does not.
    /// </summary>
    [Fact]
    public async Task AnApproverThatStopped_IsReportedAsUnreachable()
    {
        var approver = new FakeApprover();
        approver.StartApproving();

        await using var connection = new ApproverConnection(approver.PipeName);

        var (first, answered) = await connection.RequestAsync(Request(), Token);

        Assert.NotNull(first);
        Assert.Equal(ApproverOutcome.Answered, answered);

        await approver.DisposeAsync();

        var (second, outcome) = await connection.RequestAsync(Request(), Token);

        Assert.Null(second);
        Assert.Equal(ApproverOutcome.Unreachable, outcome);
    }
}
