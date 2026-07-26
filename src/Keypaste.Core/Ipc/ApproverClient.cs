using System.IO.Pipes;

namespace Keypaste.Core.Ipc;

/// <summary>
/// The bridge's end of the pipe. Every failure is a null answer, never an exception.
/// </summary>
/// <remarks>
/// <para>
/// No approver running, a pipe that closed mid-request, a reply that will not parse — all of them
/// come back as <see langword="null"/>, which the bridge turns into a denial with a reason an agent
/// can act on. That is CORE.md law 3.7 expressed as a return type: there is no path through this
/// class that produces a credential by accident, and no exception that could skip the audit line
/// the bridge writes before answering.
/// </para>
/// <para>
/// Requests are serialised, one at a time per connection, because the protocol is a plain
/// request-and-reply over one duplex stream with nothing to correlate two replies by. The MCP SDK
/// dispatches tool calls concurrently, so this genuinely happens rather than being theoretical.
/// </para>
/// </remarks>
public sealed class ApproverClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly MessageFramer _framer;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private bool _disposed;

    private ApproverClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _framer = new MessageFramer(pipe, ownsStream: false);
    }

    /// <summary>Connects to an approver, or reports that there is not one.</summary>
    /// <param name="pipeName">From <see cref="ApproverEndpoint.Resolve"/>.</param>
    /// <param name="timeout">How long to wait for the approver to answer the door.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>A connected client, or null when nothing is listening.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is null.</exception>
    public static async ValueTask<ApproverClient?> TryConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipeName);

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);

            return new ApproverClient(pipe);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>Whether the pipe is still up. False does not mean the approver is gone for good.</summary>
    public bool IsConnected => !_disposed && _pipe.IsConnected;

    /// <summary>Asks for the entry names an agent may be shown.</summary>
    /// <param name="request">The exposure to apply.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The reply, or null when the approver could not be reached or understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<NamesReply?> ListAsync(NamesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var frame = await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false);

        return frame is not null && ApproverProtocol.TryDecode(frame, out NamesReply? reply) ? reply : null;
    }

    /// <summary>Asks for one field of one entry.</summary>
    /// <param name="request">What the agent asked for.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The reply, or null when the approver could not be reached or understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<CredentialReply?> RequestAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var frame = await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false);

        return frame is not null && ApproverProtocol.TryDecode(frame, out CredentialReply? reply) ? reply : null;
    }

    private async ValueTask<byte[]?> ExchangeAsync(byte[] request, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }

        try
        {
            await _framer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            return await _framer.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            // Not the cancellation token's business: a released slot has to be released even when
            // the caller gave up, or one abandoned request wedges the connection permanently.
            _oneAtATime.Release();
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
        _framer.Dispose();
        _oneAtATime.Dispose();

        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
