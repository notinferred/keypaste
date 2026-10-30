using System.IO.Pipes;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Ipc;

/// <summary>
/// The browser relay's connection to the vault's owner: one exchange at a time, and the owner's next frame always being read
/// between them, so the end of the connection, which each lock and exit of the owner brings (D-0392), is seen while the relay
/// waits on the browser (D-0628).
/// </summary>
/// <remarks>
/// <see cref="ApproverClient"/> reads only while it waits for a reply, so a bridge learns of a lock at its next request; the
/// relay must tell the extension when the vault locks, so this one keeps a read pending. The owner sends nothing it was not
/// asked for, so a frame read between exchanges ends the link as a hang-up does. Every failure is a null answer, and a link
/// that failed once is spent.
/// </remarks>
public sealed class BrowserLink : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly MessageFramer _framer;
    private Task<byte[]?> _next;
    private int _exchanging;
    private bool _spent;
    private bool _disposed;

    private BrowserLink(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _framer = new MessageFramer(pipe, ownsStream: false);
        _next = ReadNext();
    }

    /// <summary>Completes once the owner has ended the connection, or sent a frame nobody asked for; read between exchanges.</summary>
    public Task Ended => _next;

    /// <summary>Whether nothing more can be exchanged on this link: the owner ended it, or an exchange on it failed.</summary>
    public bool IsSpent => _spent || _disposed || (Volatile.Read(ref _exchanging) == 0 && _next.IsCompleted);

    /// <summary>Connects to the owner's pipe, or reports that nothing answered.</summary>
    /// <param name="pipeName">The pipe, from <see cref="ApproverEndpoint.Resolve"/>.</param>
    /// <param name="timeout">How long to wait for the owner to answer the door.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>A connected link, or null when nothing listens.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is null.</exception>
    public static async ValueTask<BrowserLink?> TryConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipeName);

        return await ApproverClient.ConnectPipeAsync(pipeName, timeout, cancellationToken).ConfigureAwait(false) is { } pipe
            ? new BrowserLink(pipe)
            : null;
    }

    /// <summary>Attaches this connection to the session holding the vault, as a browser's attachment.</summary>
    /// <param name="request">The vault and the browser's build.</param>
    /// <param name="cancellationToken">Cancels the exchange, which spends the link.</param>
    /// <returns>The reply, or null when the owner could not be reached or understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<AttachReply?> AttachAsync(AttachRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var frame = await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false);

        return frame is not null && ApproverProtocol.TryDecode(frame, out AttachReply? reply) ? reply : null;
    }

    /// <summary>Asks the owner to pair this browser.</summary>
    /// <param name="request">The identification key, and the attachment it is made under.</param>
    /// <param name="cancellationToken">Cancels the exchange, which spends the link.</param>
    /// <returns>The reply, or null when the owner could not be reached or understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<BrowserReply?> PairAsync(BrowserPairRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Asks the owner whether a pairing is this browser's.</summary>
    /// <param name="request">The pairing and its key, and the attachment it is made under.</param>
    /// <param name="cancellationToken">Cancels the exchange, which spends the link.</param>
    /// <returns>The reply, or null when the owner could not be reached or understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<BrowserReply?> CheckAsync(BrowserCheckRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Asks the owner for a page's logins.</summary>
    /// <param name="request">The pairing, its key and the page, and the attachment it is made under.</param>
    /// <param name="cancellationToken">Cancels the exchange, which spends the link.</param>
    /// <returns>The reply, or null when the owner could not be reached or understood, or the request would not fit one frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<BrowserFillReply?> FillAsync(BrowserFillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var frame = await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false);

        return frame is not null && ApproverProtocol.TryDecode(frame, out BrowserFillReply? reply) ? reply : null;
    }

    /// <summary>Asks the owner to lock now.</summary>
    /// <param name="request">The attachment it is made under.</param>
    /// <param name="cancellationToken">Cancels the exchange, which spends the link.</param>
    /// <returns>The reply, or null when the owner could not be reached or understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async ValueTask<BrowserReply?> LockAsync(BrowserLockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await ExchangeAsync(ApproverProtocol.Encode(request), cancellationToken).ConfigureAwait(false));
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
        await _pipe.DisposeAsync().ConfigureAwait(false);

        // Closing the pipe ends the read still pending; its outcome is nobody's to act on.
        _ = _next.ContinueWith(static read => read.Exception, TaskScheduler.Default);
    }

    private static BrowserReply? Reply(byte[]? frame) =>
        frame is not null && ApproverProtocol.TryDecode(frame, out BrowserReply? reply) ? reply : null;

    private Task<byte[]?> ReadNext() => _framer.ReadAsync(CancellationToken.None).AsTask();

    private async ValueTask<byte[]?> ExchangeAsync(byte[] request, CancellationToken cancellationToken)
    {
        // Refused before anything is written, so a request too large leaves the link as it was.
        if (IsSpent || request.Length > MessageFramer.MaximumPayloadBytes)
        {
            return null;
        }

        // Outside the try below: interleaving two exchanges' frames is a broken caller, not a failed exchange.
        if (Interlocked.Exchange(ref _exchanging, 1) == 1)
        {
            throw new InvalidOperationException("two exchanges were started on one browser link; the relay makes one at a time");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _framer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            if (await _next.WaitAsync(cancellationToken).ConfigureAwait(false) is not { } reply)
            {
                _spent = true;
                return null;
            }

            _next = ReadNext();
            return reply;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // A reply not read is still on its way, so nothing more is exchanged on this stream.
            _spent = true;
            return null;
        }
        finally
        {
            Volatile.Write(ref _exchanging, 0);
        }
    }
}
