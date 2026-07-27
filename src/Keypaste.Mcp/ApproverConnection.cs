using Keypaste.Core.Ipc;

namespace Keypaste.Mcp;

/// <summary>
/// The bridge's link to <c>keypaste agent</c>: connects on demand, and reconnects once when the
/// approver has been restarted underneath it.
/// </summary>
/// <remarks>
/// <para>
/// Connecting lazily rather than at startup is deliberate. The bridge is spawned by an MCP client
/// at the client's convenience, often long before anybody starts an approver, and refusing to start
/// without one would make keypaste look broken in the client's log rather than saying so in an
/// answer an agent can act on. The audit log is a startup precondition; an approver is not.
/// </para>
/// <para>
/// <b>Reachable and unreachable are different answers.</b> "No approver is running" is something a
/// person can fix in five seconds, and the refusal for it names the command. "The approver was
/// there and the exchange failed" is not, and says so instead. Collapsing the two would make the
/// common case unactionable.
/// </para>
/// </remarks>
internal sealed class ApproverConnection(string pipeName) : IAsyncDisposable
{
    /// <summary>
    /// How long to wait for the approver to answer the door.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Short on purpose. When no approver is running this wait is pure latency in front of a
    /// refusal, and it happens on every call until somebody starts one — which is the ordinary
    /// state of a bridge an MCP client spawned at its own convenience.
    /// </para>
    /// <para>
    /// <b>Half a second is a measurement, not a guess (DECISIONS.md D-0035).</b> A whole round trip
    /// against a running approver — process start, connect, prompt, release — measured 248 ms, while
    /// the same call with nothing listening measured 2306 ms against the two seconds this used to
    /// be. Almost none of the budget is spent when somebody is there, because
    /// <c>ApproverListener</c> always has a pending instance up, so the operating system completes
    /// the connect with no application involvement. Cutting the ceiling therefore takes 1.8 s off
    /// every refusal without touching the path that works.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private ApproverClient? _client;
    private bool _disposed;

    /// <summary>Asks the approver to decide one credential request.</summary>
    /// <param name="request">What the agent asked for.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call.</param>
    /// <returns>
    /// The approver's answer; or null with <c>Reachable</c> false when no approver could be reached
    /// at all, and null with <c>Reachable</c> true when one was reached and the exchange failed.
    /// </returns>
    internal async ValueTask<(CredentialReply? Reply, bool Reachable)> RequestAsync(
        CredentialRequest request,
        CancellationToken cancellationToken) =>
        await ExchangeAsync(
            (client, token) => client.RequestAsync(request, token),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Asks the approver which entry names may be shown.</summary>
    /// <param name="request">The exposure to apply.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call.</param>
    /// <returns>The reply, and whether an approver was reachable at all.</returns>
    internal async ValueTask<(NamesReply? Reply, bool Reachable)> ListAsync(
        NamesRequest request,
        CancellationToken cancellationToken) =>
        await ExchangeAsync(
            (client, token) => client.ListAsync(request, token),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<(T? Reply, bool Reachable)> ExchangeAsync<T>(
        Func<ApproverClient, CancellationToken, ValueTask<T?>> exchange,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return (null, false);
        }

        try
        {
            var client = await ConnectedAsync(cancellationToken).ConfigureAwait(false);

            if (client is null)
            {
                return (null, false);
            }

            var reply = await exchange(client, cancellationToken).ConfigureAwait(false);

            if (reply is not null)
            {
                return (reply, true);
            }

            // One reconnect, and one retry. The ordinary cause of a dead exchange is an approver
            // that was stopped and started again between two tool calls, which a person would
            // reasonably expect to just work rather than to cost them one mysterious refusal.
            await DropAsync().ConfigureAwait(false);

            var reconnected = await ConnectedAsync(cancellationToken).ConfigureAwait(false);

            if (reconnected is null)
            {
                return (null, false);
            }

            return (await exchange(reconnected, cancellationToken).ConfigureAwait(false), true);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async ValueTask<ApproverClient?> ConnectedAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        if (_client is { IsConnected: true })
        {
            return _client;
        }

        await DropAsync().ConfigureAwait(false);

        _client = await ApproverClient.TryConnectAsync(pipeName, ConnectTimeout, cancellationToken).ConfigureAwait(false);

        return _client;
    }

    private async ValueTask DropAsync()
    {
        var going = _client;
        _client = null;

        if (going is not null)
        {
            await going.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await DropAsync().ConfigureAwait(false);
        _oneAtATime.Dispose();
    }
}
