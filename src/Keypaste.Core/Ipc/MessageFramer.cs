using System.Buffers;

namespace Keypaste.Core.Ipc;

/// <summary>
/// Newline-delimited frames over a duplex stream, with a hard ceiling on how big one can be.
/// </summary>
/// <remarks>
/// <para>
/// One JSON object per line, UTF-8, no BOM — the same shape as the audit log, for the same reason:
/// it is trivially correct to write, trivially correct to resynchronise after, and readable by eye
/// when something goes wrong. The default <see cref="System.Text.Json"/> encoder escapes control
/// characters, so a newline can never appear inside a frame and the delimiter cannot be forged.
/// </para>
/// <para>
/// <b>The ceiling is a denial-of-service bound, not a formatting preference.</b> Without it, a peer
/// that opens a connection and sends bytes without ever sending a newline grows this buffer until
/// the approver — the process holding the unlocked vault — runs out of memory. A frame over the
/// limit ends the connection rather than being truncated, because a truncated frame is a message
/// that says something other than what was sent.
/// </para>
/// </remarks>
public sealed class MessageFramer : IDisposable
{
    /// <summary>The largest frame either side will send or accept.</summary>
    public const int MaximumFrameBytes = 64 * 1024;

    internal const byte Delimiter = (byte)'\n';
    internal const int ReadChunkBytes = 4096;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private byte[] _pending = [];
    private int _pendingLength;
    private bool _disposed;

    /// <summary>Wraps a duplex stream.</summary>
    /// <param name="stream">The connection. Must be readable and writable.</param>
    /// <param name="ownsStream">Whether disposing this should dispose the stream too.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public MessageFramer(Stream stream, bool ownsStream = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
        _ownsStream = ownsStream;
    }

    /// <summary>Sends one frame.</summary>
    /// <param name="payload">The frame's bytes, which must contain no newline.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the frame has been flushed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The frame is over <see cref="MaximumFrameBytes"/>.</exception>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (payload.Length + 1 > MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                $"a frame of {payload.Length} bytes is over the {MaximumFrameBytes}-byte limit");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(payload.Length + 1);

        try
        {
            payload.Span.CopyTo(buffer);
            buffer[payload.Length] = Delimiter;

            // One write and one flush, so a frame reaches the peer whole rather than in pieces
            // that a reader has to guess the end of.
            await _stream.WriteAsync(buffer.AsMemory(0, payload.Length + 1), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Reads the next frame.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The frame's bytes without its delimiter, or null once the peer has gone away.</returns>
    /// <exception cref="InvalidOperationException">The peer sent more than <see cref="MaximumFrameBytes"/> without a delimiter.</exception>
    public async ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            var end = Array.IndexOf(_pending, Delimiter, 0, _pendingLength);

            if (end >= 0)
            {
                var frame = _pending.AsSpan(0, end).ToArray();
                Consume(end + 1);
                return frame;
            }

            if (_pendingLength >= MaximumFrameBytes)
            {
                throw new InvalidOperationException(
                    $"the peer sent {_pendingLength} bytes with no frame delimiter, over the {MaximumFrameBytes}-byte limit");
            }

            var chunk = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);

            try
            {
                var read = await _stream.ReadAsync(chunk.AsMemory(0, ReadChunkBytes), cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    // End of stream. A trailing partial frame is discarded rather than delivered:
                    // half a message is not a message.
                    return null;
                }

                Append(chunk.AsSpan(0, read));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
            }
        }
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        if (_pendingLength + bytes.Length > _pending.Length)
        {
            var grown = new byte[Math.Max(ReadChunkBytes, (_pendingLength + bytes.Length) * 2)];
            _pending.AsSpan(0, _pendingLength).CopyTo(grown);
            Array.Clear(_pending);
            _pending = grown;
        }

        bytes.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength += bytes.Length;
    }

    private void Consume(int count)
    {
        _pending.AsSpan(count, _pendingLength - count).CopyTo(_pending);
        _pendingLength -= count;

        // Zero what is now past the end. A reply frame carries a credential, and leaving its bytes
        // in the tail of this buffer would keep a copy alive for as long as the connection.
        Array.Clear(_pending, _pendingLength, _pending.Length - _pendingLength);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Array.Clear(_pending);

        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }
}
