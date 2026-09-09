using System.Security.Cryptography;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The one thing the client refuses to do quietly: carry two exchanges at once.
/// </summary>
/// <remarks>
/// The pipe is a plain request-and-reply over one duplex stream, and the protocol has no message id
/// to correlate a reply by. So two exchanges in flight together do not merely contend — they
/// interleave frames, and whichever reads first takes the other's answer. On the credential path
/// that is one request receiving another's released field.
/// </remarks>
public sealed class ApproverClientTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeSpan _promptly = TimeSpan.FromSeconds(10);

    private static CredentialRequest Request(string entry = "env/dev/STRIPE_KEY") => new()
    {
        Entry = entry,
        Field = "password",
        Reason = "deploy billing to staging",
        TtlSeconds = 300,
        Exposure = ["env/**"],
        ClientName = "claude-code",
    };

    /// <summary>
    /// Contention here means the caller's own serialisation has already failed, so it throws.
    /// </summary>
    /// <remarks>
    /// <c>ApproverConnection</c> refuses a second exchange before it reaches this class, and answers
    /// the agent with a busy signal. If one gets through anyway that guarantee is broken, and
    /// returning null would file the breakage as an ordinary failed exchange — one more refusal an
    /// agent is told it may retry. A bug is not an answer, so it is not given one.
    /// </remarks>
    [Fact]
    public async Task TwoExchangesAtOnce_AreABrokenInvariantRatherThanAQueue()
    {
        var handler = new ParkingHandler();
        await using var host = new Host(handler);

        await using var client =
            await ApproverClient.TryConnectAsync(host.PipeName, _promptly, Token)
            ?? throw new InvalidOperationException("the test could not connect to its own listener");

        var parked = client.RequestAsync(Request(), Token).AsTask();

        await handler.Entered.Task.WaitAsync(_promptly, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.RequestAsync(Request("env/dev/OTHER"), Token));

        handler.Release.TrySetResult();

        Assert.NotNull(await parked.WaitAsync(_promptly, Token));

        // The one that threw never reached the wire, which is the point: it was refused before it
        // could put a second frame on a stream already carrying one.
        Assert.Single(handler.Received);
    }

    /// <summary>A listener on its own pipe, torn down with the test.</summary>
    private sealed class Host : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;

        internal Host(IApproverHandler handler)
        {
            PipeName = "keypaste-client-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            _listener = new ApproverListener(PipeName, handler);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>Parks on the first request, so a second can be raced against it.</summary>
    private sealed class ParkingHandler : IApproverHandler
    {
        internal List<CredentialRequest> Received { get; } = [];

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new NamesReply(true, [], string.Empty, true));

        public async ValueTask<CredentialReply> RequestAsync(
            CredentialRequest request,
            string connectionId,
            CancellationToken cancellationToken)
        {
            lock (Received)
            {
                Received.Add(request);
            }

            Entered.TrySetResult();

            await Release.Task.ConfigureAwait(false);

            return new CredentialReply
            {
                Decision = AuditDecision.Granted,
                Method = AuditMethod.Prompt,
                Reason = "a person approved this request",
                Entry = request.Entry,
                TtlSeconds = 300,
                Value = "sk_live_client_sentinel",
            };
        }

        public void Disconnected(string connectionId)
        {
        }
    }
}
