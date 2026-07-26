using System.Collections.Concurrent;
using System.Security.Cryptography;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// A <c>keypaste agent</c> that answers however a test tells it to, over a real named pipe.
/// </summary>
/// <remarks>
/// <para>
/// A fake <em>handler</em> behind a real listener, rather than a fake connection. That keeps the
/// wire in the picture: the bridge under test encodes a real request, sends it down a real pipe,
/// and decodes a real reply. Stubbing the connection instead would have skipped the one place in
/// keypaste where a credential crosses a process boundary — which is exactly the place worth not
/// skipping.
/// </para>
/// <para>
/// <see cref="Running"/> defaults to false, so a test that says nothing about the approver is
/// testing the case where nobody has started one. That is the ordinary state of a freshly spawned
/// bridge, and it should be the default a test falls into rather than one it has to remember.
/// </para>
/// </remarks>
internal sealed class FakeApprover : IAsyncDisposable
{
    /// <summary>What this approver releases when it is told to say yes.</summary>
    internal const string Sentinel = "sk_live_APPROVED_SENTINEL_1f4c";

    private readonly CancellationTokenSource _stop = new();
    private readonly Handler _handler;
    private ApproverListener? _listener;
    private Task? _running;
    private bool _disposed;

    internal FakeApprover()
    {
        _handler = new Handler(this);
        PipeName = "keypaste-mcp-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
    }

    /// <summary>The pipe the bridge will look for this on.</summary>
    internal string PipeName { get; }

    /// <summary>Whether an approver is listening at all. False by default.</summary>
    internal bool Running => _listener is not null;

    /// <summary>What the approver decides. Denies unless a test says otherwise.</summary>
    internal CredentialReply Answer { get; set; } = Denial(AuditMethod.Prompt, "a person refused this request");

    /// <summary>What the approver says the vault contains.</summary>
    internal NamesReply Names { get; set; } = new(false, [], "no vault is unlocked");

    /// <summary>Every request that reached the approver, in the order it arrived.</summary>
    internal ConcurrentBag<CredentialRequest> Received { get; } = [];

    /// <summary>Set to make the approver park on a request, so a timeout or a race is reachable.</summary>
    internal TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether requests should park on <see cref="Held"/> before being answered.</summary>
    internal bool Hold { get; set; }


    /// <summary>Completes once a request has genuinely reached the approver.</summary>
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts listening. Until this is called, the bridge finds nobody home.</summary>
    internal FakeApprover Start()
    {
        _listener = new ApproverListener(PipeName, _handler);
        _running = _listener.RunAsync(_stop.Token);
        return this;
    }

    /// <summary>Starts listening and says yes to everything, releasing <see cref="Sentinel"/>.</summary>
    internal FakeApprover StartApproving(int ttlSeconds = 300)
    {
        Answer = new CredentialReply
        {
            Decision = AuditDecision.Granted,
            Method = AuditMethod.Prompt,
            Reason = $"a person approved this request for {ttlSeconds} seconds",
            Entry = "env/dev/STRIPE_KEY",
            TtlSeconds = ttlSeconds,
            Value = Sentinel,
        };

        return Start();
    }

    /// <summary>Starts listening and refuses everything, for the stated reason.</summary>
    internal FakeApprover StartRefusing(AuditMethod method, string reason = "a person refused this request")
    {
        Answer = Denial(method, reason);
        return Start();
    }

    internal static CredentialReply Denial(AuditMethod method, string reason) => new()
    {
        Decision = AuditDecision.Denied,
        Method = method,
        Reason = reason,
        TtlSeconds = 0,
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Held.TrySetResult();

        await _stop.CancelAsync();

        if (_running is not null)
        {
            try
            {
                await _running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }
        }

        _listener?.Dispose();
        _stop.Dispose();
    }

    private sealed class Handler(FakeApprover approver) : IApproverHandler
    {
        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(approver.Names);

        public async ValueTask<CredentialReply> RequestAsync(
            CredentialRequest request,
            string connectionId,
            CancellationToken cancellationToken)
        {
            approver.Received.Add(request);
            approver.Entered.TrySetResult();

            if (approver.Hold)
            {
                await approver.Held.Task.ConfigureAwait(false);
            }

            return approver.Answer;
        }

        public void Disconnected(string connectionId)
        {
        }
    }
}
