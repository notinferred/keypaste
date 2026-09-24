using System.Globalization;
using System.IO.Pipes;

namespace Keypaste.Core.Ipc;

/// <summary>
/// Accepts bridge connections on a named pipe and hands each request to a handler.
/// </summary>
/// <remarks>
/// <para>
/// A fresh <see cref="NamedPipeServerStream"/> per connection, which is what Windows requires — an
/// instance is consumed the moment a client connects — and what .NET's Unix implementation supports
/// by sharing one listening socket between instances on the same path. One accept loop covers both.
/// </para>
/// <para>
/// <b>Two clients at once is the case that matters</b>, not an edge case: Claude Desktop and Claude
/// Code each spawn their own bridge, and both may be connected to one approver. That is why
/// connections are handled concurrently rather than one at a time, and why each gets its own
/// connection id and its own grants.
/// </para>
/// <para>
/// A connection that misbehaves — a frame that will not parse, a frame over the size limit, a peer
/// that vanishes — costs that connection and nothing else. The approver holds the unlocked vault,
/// so it is the last process in keypaste that may be brought down by something a peer sent.
/// </para>
/// </remarks>
public sealed class ApproverListener : IDisposable
{
    private static readonly TimeSpan _deliveryBound = TimeSpan.FromSeconds(1);

    private readonly string _pipeName;
    private readonly IApproverHandler _handler;
    private NamedPipeServerStream? _pending;
    private int _connections;
    private bool _disposed;

    /// <summary>Builds a listener, binding the pipe immediately.</summary>
    /// <param name="pipeName">The pipe to listen on, from <see cref="ApproverEndpoint.Resolve"/>.</param>
    /// <param name="handler">What answers the requests.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="IOException">The name is already taken.</exception>
    /// <remarks>
    /// Binding in the constructor rather than in <see cref="RunAsync"/> is deliberate twice over:
    /// <c>keypaste agent</c> fails at startup on a name somebody else holds, rather than appearing
    /// to work and never accepting anything; and a bridge started in the same breath cannot lose a
    /// race against a pipe that does not exist yet.
    /// </remarks>
    public ApproverListener(string pipeName, IApproverHandler handler)
    {
        ArgumentNullException.ThrowIfNull(pipeName);
        ArgumentNullException.ThrowIfNull(handler);

        _pipeName = pipeName;
        _handler = handler;
        _pending = Create();
    }

    /// <summary>How many connections have been accepted. A status line, not a decision input.</summary>
    public int Accepted => Volatile.Read(ref _connections);

    /// <summary>Accepts connections until cancelled.</summary>
    /// <param name="cancellationToken">Cancelled to stop listening.</param>
    /// <returns>A task that completes once the listener has stopped accepting.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var live = new List<Task>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = Interlocked.Exchange(ref _pending, null) ?? Create();

                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                catch (Exception)
                {
                    // One refused connection, not one dead approver. Rethrowing here ends the accept
                    // loop and with it the only process holding the unlocked vault - which is what
                    // this type's own summary says must not be reachable from something a peer sent.
                    // Cancellation still propagates above, because that is how this is asked to stop.
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                // The next instance goes up before this one is served, so there is no window in
                // which a second bridge finds nothing listening.
                _pending = Create();

                var id = string.Create(CultureInfo.InvariantCulture, $"conn-{Interlocked.Increment(ref _connections)}");

                live.RemoveAll(task => task.IsCompleted);
                live.Add(ServeAsync(pipe, id, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is how this stops.
        }

        await Task.WhenAll(live).ConfigureAwait(false);
    }

    private NamedPipeServerStream Create() =>
        new(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ServeAsync(NamedPipeServerStream pipe, string connectionId, CancellationToken cancellationToken)
    {
        var framer = new MessageFramer(pipe);
        Task<byte[]?>? next = null;

        try
        {
            next = framer.ReadAsync(cancellationToken).AsTask();

            while (await next.ConfigureAwait(false) is { } frame)
            {
                // Read ahead while the request is decided. A peer sends nothing until it has its
                // reply, so this read ending first means it hung up or spoke out of turn.
                next = framer.ReadAsync(cancellationToken).AsTask();

                var reply = await AnswerWhileWaitedForAsync(frame, connectionId, next, cancellationToken).ConfigureAwait(false);

                if (reply is null)
                {
                    // Unparseable, a kind this version does not serve, or a peer no longer waiting.
                    // Ending the connection is the whole response: replying to a message we could
                    // not read would mean guessing what it asked for.
                    return;
                }

                // Not the stop token: a request a lock or a shutdown withdrew has its denial to
                // deliver, and the bridge audits that rather than a reply that never came (D-0313).
                using var delivery = new CancellationTokenSource(_deliveryBound);
                await framer.WriteAsync(reply, delivery.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // A peer that went away, a frame over the limit, a pipe torn down under us, and anything
            // else the handler throws: a narrower filter misses something reachable from a peer's
            // bytes, and ServeAsync is fire-and-forget, so it surfaces at shutdown instead of here.
            // One connection's problem and never the approver's — this is the outermost boundary
            // between a peer and the process holding the unlocked vault (law 3.7).
        }
        finally
        {
            framer.Dispose();

            // Closing the pipe ends a read still pending; its outcome is nobody's to act on.
            _ = next?.ContinueWith(static read => read.Exception, TaskScheduler.Default);
            _handler.Disconnected(connectionId);
        }
    }

    /// <summary>Answers one request, withdrawing it if its peer stops waiting first.</summary>
    /// <param name="frame">The request.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="peer">The read of the peer's next frame, which ends early only if it hung up or spoke out of turn.</param>
    /// <param name="cancellationToken">Cancelled when the listener stops.</param>
    /// <returns>The reply, or null when the request was unreadable or nobody is waiting for it.</returns>
    /// <remarks>
    /// A prompt raised for a peer that has gone comes down rather than waiting out its window for an
    /// answer nobody will receive. A stopping listener is not a peer that went: its withdrawn requests
    /// still have their denials to deliver (D-0313).
    /// </remarks>
    private async Task<byte[]?> AnswerWhileWaitedForAsync(
        byte[] frame,
        string connectionId,
        Task peer,
        CancellationToken cancellationToken)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var answering = AnswerAsync(frame, connectionId, exchange.Token).AsTask();

        if (await Task.WhenAny(answering, peer).ConfigureAwait(false) == peer
            && !cancellationToken.IsCancellationRequested)
        {
            await exchange.CancelAsync().ConfigureAwait(false);
            await Settled(answering).ConfigureAwait(false);
            return null;
        }

        return await answering.ConfigureAwait(false);
    }

    private static async Task Settled(Task answering)
    {
        try
        {
            await answering.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The answer has nobody to go to; what matters is that deciding it has finished.
        }
    }

    private async ValueTask<byte[]?> AnswerAsync(byte[] frame, string connectionId, CancellationToken cancellationToken)
    {
        switch (ApproverProtocol.KindOf(frame))
        {
            case ApproverMessageKind.Attach when ApproverProtocol.TryDecode(frame, out AttachRequest? attach):
                return ApproverProtocol.Encode(
                    await _handler.AttachAsync(attach, connectionId, cancellationToken).ConfigureAwait(false));

            case ApproverMessageKind.Names when ApproverProtocol.TryDecode(frame, out NamesRequest? names):
                return ApproverProtocol.Encode(
                    await _handler.ListAsync(names, connectionId, cancellationToken).ConfigureAwait(false));

            case ApproverMessageKind.Credential when ApproverProtocol.TryDecode(frame, out CredentialRequest? credential):
                return ApproverProtocol.Encode(
                    await _handler.RequestAsync(credential, connectionId, cancellationToken).ConfigureAwait(false));

            default:
                return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Exchange(ref _pending, null)?.Dispose();
    }
}
