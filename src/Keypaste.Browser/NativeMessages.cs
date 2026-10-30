using System.Runtime.InteropServices;

namespace Keypaste.Browser;

/// <summary>
/// Native messaging's framing: each message is UTF-8 JSON after its length as an unsigned 32-bit number in native byte order,
/// little-endian on every target keypaste builds (D-0625).
/// </summary>
internal static class NativeMessages
{
    /// <summary>
    /// The longest message either side may send: what Chrome accepts from a host, and what KeePassXC reads from the extension.
    /// </summary>
    internal const int MaximumBytes = 1024 * 1024;

    /// <summary>Reads the next message.</summary>
    /// <param name="input">The browser's end, standard input.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The message, or null once the browser has closed standard input, mid-message included.</returns>
    /// <exception cref="InvalidDataException">The message is over <see cref="MaximumBytes"/>, which is refused before any of it is read.</exception>
    internal static async Task<byte[]?> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(uint)];

        if (!await FilledAsync(input, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = MemoryMarshal.Read<uint>(header);

        if (length > MaximumBytes)
        {
            throw new InvalidDataException($"the browser sent a message of {length} bytes, and a message is at most {MaximumBytes}");
        }

        var message = new byte[length];

        return await FilledAsync(input, message, cancellationToken).ConfigureAwait(false) ? message : null;
    }

    /// <summary>Writes one message whole, then flushes it.</summary>
    /// <param name="output">The browser's end, standard output.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="InvalidOperationException">The message is over <see cref="MaximumBytes"/>.</exception>
    internal static async Task WriteAsync(Stream output, ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        if (message.Length > MaximumBytes)
        {
            throw new InvalidOperationException($"a message of {message.Length} bytes is over the {MaximumBytes} the browser accepts");
        }

        var frame = new byte[sizeof(uint) + message.Length];
        MemoryMarshal.Write(frame, (uint)message.Length);
        message.Span.CopyTo(frame.AsSpan(sizeof(uint)));

        await output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> FilledAsync(Stream input, byte[] buffer, CancellationToken cancellationToken) =>
        buffer.Length == 0
        || await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) == buffer.Length;
}
