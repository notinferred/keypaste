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
                catch (Exception)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
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
        try
        {
            using var framer = new MessageFramer(pipe);

            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await framer.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (frame is null)
                {
                    return;
                }

                var reply = await AnswerAsync(frame, connectionId, cancellationToken).ConfigureAwait(false);

                if (reply is null)
                {
                    // Unparseable, or a kind this version does not serve. Ending the connection is
                    // the whole response: replying to a message we could not read would mean
                    // guessing what it asked for.
                    return;
                }

                await framer.WriteAsync(reply, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // A peer that went away, a frame over the limit, a pipe torn down under us. One
            // connection's problem, and never the approver's.
        }
        finally
        {
            _handler.Disconnected(connectionId);
        }
    }

    private async ValueTask<byte[]?> AnswerAsync(byte[] frame, string connectionId, CancellationToken cancellationToken)
    {
        switch (ApproverProtocol.KindOf(frame))
        {
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
