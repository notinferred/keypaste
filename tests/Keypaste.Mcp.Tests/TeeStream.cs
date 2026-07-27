namespace Keypaste.Mcp.Tests;

/// <summary>
/// Passes writes through and keeps a copy, so a test can look at the bytes that actually left the
/// server rather than at an object that was parsed out of them.
/// </summary>
/// <remarks>
/// The distinction matters for exactly one kind of claim. "The result did not contain the secret"
/// asserted against a deserialized <c>CallToolResult</c> only proves that the field the test looked
/// at was empty; a secret that leaked through some other field, or through a part of the frame the
/// client's parser discards, would sail past it. Asserting against the raw transcript is the only
/// version of that claim with nothing behind it.
/// </remarks>
internal sealed class TeeStream(Stream inner) : Stream
{
    private readonly Lock _gate = new();
    private readonly MemoryStream _copy = new();

    /// <summary>Everything written so far, as bytes.</summary>
    internal byte[] Written
    {
        get
        {
            lock (_gate)
            {
                return _copy.ToArray();
            }
        }
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Record(buffer);
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Record(buffer.Span);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Record(ReadOnlySpan<byte> buffer)
    {
        lock (_gate)
        {
            _copy.Write(buffer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The inner stream stays owned by the harness, which also owns the read end of the pipe
            // it belongs to. Only the copy is ours.
            _copy.Dispose();
        }

        base.Dispose(disposing);
    }
}
