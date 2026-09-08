using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The listener and the client over a real named pipe, on whatever platform is running the suite.
/// </summary>
/// <remarks>
/// A real pipe rather than a pair of memory streams, because the thing most likely to be wrong here
/// is platform behaviour rather than logic: Windows consumes a server instance the moment a client
/// connects, while .NET on Unix implements the same API over a Unix domain socket. The one
/// assumption the approver rests on — that two bridges can be connected at once — is only checkable
/// against the real thing, which is why <see cref="TwoBridgesCanBeConnectedAtOnce"/> exists.
/// </remarks>
public sealed class ApproverListenerTests
{
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A pipe name no other test or machine will collide with.</summary>
    private static string UniqueName() =>
        "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    private static CredentialRequest Request(string entry = "env/dev/STRIPE_KEY") => new()
    {
        Entry = entry,
        Field = "password",
        Reason = "deploy billing to staging",
        TtlSeconds = 900,
        Exposure = ["env/**"],
        ClientName = "claude-code",
    };

    private static async Task<ApproverClient> ConnectAsync(string pipeName)
    {
        var client = await ApproverClient.TryConnectAsync(pipeName, _connectTimeout, Token);

        Assert.NotNull(client);
        return client;
    }

    [Fact]
    public async Task ACredentialRequestReachesTheApproverAndTheAnswerComesBack()
    {
        var handler = new RecordingHandler();
        await using var host = Host.Start(handler);
        await using var client = await ConnectAsync(host.PipeName);

        var reply = await client.RequestAsync(Request(), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(RecordingHandler.Sentinel, reply.Value, StringComparer.Ordinal);

        var seen = Assert.Single(handler.Credentials);
        Assert.Equal("env/dev/STRIPE_KEY", seen.Entry, StringComparer.Ordinal);
        Assert.Equal("deploy billing to staging", seen.Reason, StringComparer.Ordinal);
        Assert.Equal(["env/**"], seen.Exposure);
    }

    [Fact]
    public async Task ANamesRequestReachesTheApproverAndTheNamesComeBack()
    {
        var handler = new RecordingHandler();
        await using var host = Host.Start(handler);
        await using var client = await ConnectAsync(host.PipeName);

        var reply = await client.ListAsync(new NamesRequest(["env/**"]), Token);

        Assert.NotNull(reply);
        Assert.True(reply.VaultUnlocked);
        Assert.Equal([new EntryName("env/dev", "STRIPE_KEY")], reply.Names);
    }

    /// <summary>
    /// Claude Desktop and Claude Code each spawn their own bridge, so two connections at once is the
    /// normal case rather than an edge one. It is also the single behaviour most likely to differ
    /// between Windows named pipes and .NET's Unix emulation of them, which is why it is asserted
    /// against a real pipe on all three platforms in CI rather than reasoned about.
    /// </summary>
    [Fact]
    public async Task TwoBridgesCanBeConnectedAtOnce()
    {
        var handler = new RecordingHandler();
        await using var host = Host.Start(handler);

        await using var first = await ConnectAsync(host.PipeName);
        await using var second = await ConnectAsync(host.PipeName);

        var one = await first.RequestAsync(Request("env/dev/ONE"), Token);
        var two = await second.RequestAsync(Request("env/dev/TWO"), Token);

        Assert.NotNull(one);
        Assert.NotNull(two);

        // Different connections, so different grant scopes. A shared id would mean one client's
        // approval silently satisfied the other's request (THREATS.md T-3).
        Assert.Equal(2, handler.ConnectionIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A grant belongs to the process the human approved for, so the approver has to be told when
    /// that process goes away. Without this, grants would outlive the connection that earned them.
    /// </summary>
    [Fact]
    public async Task WhenABridgeDisconnects_TheApproverIsTold()
    {
        var handler = new RecordingHandler();
        await using var host = Host.Start(handler);

        var client = await ConnectAsync(host.PipeName);
        await client.RequestAsync(Request(), Token);
        await client.DisposeAsync();

        await handler.Gone.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Single(handler.Disconnections);
    }

    /// <summary>
    /// Nobody listening is the ordinary case — the human has not started the approver — and it has
    /// to be a null answer rather than an exception, because the bridge turns it into a denial with
    /// a line in the audit log (docs/PRODUCT.md laws 3.3 and 3.7).
    /// </summary>
    [Fact]
    public async Task WithNoApproverRunning_ConnectingReportsItRatherThanThrowing()
    {
        var client = await ApproverClient.TryConnectAsync(UniqueName(), TimeSpan.FromMilliseconds(500), Token);

        Assert.Null(client);
    }

    /// <summary>Names shaped as an ordinary vault's are, and far more of them than one frame holds.</summary>
    private static IReadOnlyList<EntryName> Crowd(int count) =>
        [.. Enumerable.Range(0, count)
            .Select(i => new EntryName("env/dev/services", $"SERVICE_ACCOUNT_ACCESS_TOKEN_AB_{i:D4}"))];

    /// <summary>
    /// A vault with more names than one frame can carry is answered, and the connection lives.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="AGarbageFrame_CostsThatConnectionAndNoOther"/>. There, ending the
    /// connection is the right answer to a peer's nonsense. Here nobody did anything wrong — the
    /// person simply has a thousand entries — and until the reply was bounded the write threw,
    /// <see cref="ApproverListener"/> swallowed it, and the agent was told only that
    /// something had gone wrong.
    /// </remarks>
    [Fact]
    public async Task AHugeListing_IsAnsweredAndTheConnectionSurvives()
    {
        var handler = new RecordingHandler { Names = Crowd(1000) };
        await using var host = Host.Start(handler);
        await using var client = await ConnectAsync(host.PipeName);

        var reply = await client.ListAsync(new NamesRequest(["env/**"]), Token);

        Assert.NotNull(reply);
        Assert.False(reply.Complete);
        Assert.NotEmpty(reply.Names);

        // The connection is still there, which is the half a reply on its own would not have shown.
        Assert.NotNull(await client.RequestAsync(Request(), Token));
    }

    /// <summary>
    /// ...and it does not cost the connection its grants.
    /// </summary>
    /// <remarks>
    /// Asserted separately because it fails for a different reason and it is the harm a person would
    /// actually notice: a torn-down connection takes <c>Disconnected</c> with it, and a grant is
    /// scoped to the connection the approver minted it for. Listing your own vault would have
    /// silently revoked the approval you gave a minute ago, and the next request would ask again.
    /// </remarks>
    [Fact]
    public async Task AHugeListing_DoesNotRevokeThisConnectionsGrants()
    {
        var handler = new RecordingHandler { Names = Crowd(1000) };
        await using var host = Host.Start(handler);
        await using var client = await ConnectAsync(host.PipeName);

        await client.ListAsync(new NamesRequest(["env/**"]), Token);

        Assert.Empty(handler.Disconnections);
    }

    /// <summary>
    /// A release too large to send is answered with a refusal, and the connection lives.
    /// </summary>
    /// <remarks>
    /// The credential half of <see cref="AHugeListing_IsAnsweredAndTheConnectionSurvives"/>, and the
    /// worse of the two: a listing that fails costs a person their approval, while this one fails
    /// <em>after</em> they gave it. The refusal fits by construction — it carries no value — so it
    /// travels the connection that is still open rather than arriving as a hangup.
    /// </remarks>
    [Fact]
    public async Task AnUndeliverableRelease_IsAnsweredAndTheConnectionSurvives()
    {
        var handler = new RecordingHandler { Value = new string('n', 100_000) };
        await using var host = Host.Start(handler);
        await using var client = await ConnectAsync(host.PipeName);

        var reply = await client.RequestAsync(Request(), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Undeliverable, reply.Method);
        Assert.Null(reply.Value);

        // The connection is still usable, which is the half a reply on its own would not have shown.
        handler.Value = RecordingHandler.Sentinel;

        var next = await client.RequestAsync(Request(), Token);

        Assert.NotNull(next);
        Assert.Equal(RecordingHandler.Sentinel, next.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// ...and it does not cost the connection its grants.
    /// </summary>
    /// <remarks>
    /// Asserted separately, for the reason
    /// <see cref="AHugeListing_DoesNotRevokeThisConnectionsGrants"/> gives. Here the revoked grant is
    /// the one that was minted for this very request: the write threw, the <c>finally</c> ran
    /// <c>Disconnected</c>, and the approval a person had just given was thrown away before anybody
    /// could use it.
    /// </remarks>
    [Fact]
    public async Task AnUndeliverableRelease_DoesNotRevokeThisConnectionsGrants()
    {
        var handler = new RecordingHandler { Value = new string('n', 100_000) };
        await using var host = Host.Start(handler);
        await using var client = await ConnectAsync(host.PipeName);

        await client.RequestAsync(Request(), Token);

        Assert.Empty(handler.Disconnections);
    }

    /// <summary>
    /// A peer that sends nonsense costs its own connection and nothing else. The approver holds the
    /// unlocked vault, so it is the last process in keypaste that may be taken down by something
    /// somebody sent it.
    /// </summary>
    [Fact]
    public async Task AGarbageFrame_CostsThatConnectionAndNoOther()
    {
        var handler = new RecordingHandler();
        await using var host = Host.Start(handler);

        // A raw pipe, so the frame really is garbage. Going through ApproverClient would have
        // encoded something well-formed and proved nothing.
        await using (var rude = new NamedPipeClientStream(
            ".", host.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await rude.ConnectAsync(Token);
            await rude.WriteAsync(Encoding.UTF8.GetBytes("this is not json at all\n"), Token);
            await rude.FlushAsync(Token);

            // The approver hangs up rather than answering a message it could not read.
            var buffer = new byte[1];
            Assert.Equal(0, await rude.ReadAsync(buffer, Token));
        }

        // ...and it is still serving everybody else.
        await using var polite = await ConnectAsync(host.PipeName);

        Assert.NotNull(await polite.ListAsync(new NamesRequest(["env/**"]), Token));
    }

    [Fact]
    public async Task AClientThatHasBeenDisposed_AnswersNullRatherThanThrowing()
    {
        var handler = new RecordingHandler();
        await using var host = Host.Start(handler);

        var client = await ConnectAsync(host.PipeName);
        await client.DisposeAsync();

        Assert.Null(await client.RequestAsync(Request(), Token));
        Assert.Null(await client.ListAsync(new NamesRequest([]), Token));
    }

    [Fact]
    public void TheListenerRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new ApproverListener(null!, new RecordingHandler()));
        Assert.Throws<ArgumentNullException>(() => new ApproverListener("x", null!));
    }

    /// <summary>A listener running on its own pipe, torn down with the test.</summary>
    private sealed class Host : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;

        private Host(string pipeName, ApproverListener listener)
        {
            PipeName = pipeName;
            _listener = listener;
            _running = listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal static Host Start(IApproverHandler handler)
        {
            var name = UniqueName();

            // No readiness probe: the listener binds in its constructor, so by the time this
            // returns the pipe exists. A probe would also have cost a connection and a
            // disconnection, which the counts in these tests would then have had to explain away.
            return new Host(name, new ApproverListener(name, handler));
        }

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

    private sealed class RecordingHandler : IApproverHandler
    {
        internal const string Sentinel = "sk_live_listener_sentinel";

        internal ConcurrentBag<CredentialRequest> Credentials { get; } = [];

        internal ConcurrentBag<string> ConnectionIds { get; } = [];

        internal ConcurrentBag<string> Disconnections { get; } = [];

        internal TaskCompletionSource Gone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>What the approver has to say. One ordinary entry unless a test says otherwise.</summary>
        internal IReadOnlyList<EntryName> Names { get; set; } = [new EntryName("env/dev", "STRIPE_KEY")];

        /// <summary>What a release carries. Settable so a test can hand back a field no frame holds.</summary>
        internal string Value { get; set; } = Sentinel;

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken)
        {
            ConnectionIds.Add(connectionId);

            return ValueTask.FromResult(new NamesReply(true, Names, string.Empty, true));
        }

        public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken)
        {
            Credentials.Add(request);
            ConnectionIds.Add(connectionId);

            return ValueTask.FromResult(new CredentialReply
            {
                Decision = AuditDecision.Granted,
                Method = AuditMethod.Prompt,
                Reason = "a person approved this request",
                Entry = request.Entry,
                TtlSeconds = 300,
                Value = Value,
            });
        }

        public void Disconnected(string connectionId)
        {
            Disconnections.Add(connectionId);
            Gone.TrySetResult();
        }
    }
}
