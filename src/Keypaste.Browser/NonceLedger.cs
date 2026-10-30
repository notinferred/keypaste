namespace Keypaste.Browser;

/// <summary>
/// The nonces a relay has used: each request's, and the one its reply is sealed under, the request's incremented (D-0625).
/// </summary>
/// <remarks>
/// A box key is the same both ways, the host's secret with the extension's public key and the extension's secret with the
/// host's, so a reply sealed under a nonce a request used, or a nonce used twice, would encrypt two messages under one key and
/// nonce. KeePassXC answers a repeated nonce; the relay refuses it, and any request whose reply would reuse one.
/// </remarks>
internal sealed class NonceLedger
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    /// <summary>Takes a request's nonce and its reply's, unless the relay has used either in this session.</summary>
    /// <param name="request">The request's nonce.</param>
    /// <param name="reply">The nonce its reply is sealed under, when taken.</param>
    /// <returns>Whether both were unused, and are now taken.</returns>
    internal bool TryTake(ReadOnlySpan<byte> request, out byte[] reply)
    {
        reply = Incremented(request);

        var asked = Convert.ToBase64String(request);
        var answered = Convert.ToBase64String(reply);

        if (_used.Contains(asked) || _used.Contains(answered))
        {
            return false;
        }

        _used.Add(asked);
        _used.Add(answered);
        return true;
    }

    /// <summary>The nonce after <paramref name="nonce"/>, as libsodium's <c>sodium_increment</c> and the extension count: a little-endian number plus one, wrapping.</summary>
    /// <param name="nonce">The nonce.</param>
    /// <returns>A new array.</returns>
    internal static byte[] Incremented(ReadOnlySpan<byte> nonce)
    {
        var next = nonce.ToArray();
        var carry = 1;

        for (var index = 0; index < next.Length; index++)
        {
            carry += next[index];
            next[index] = (byte)carry;
            carry >>= 8;
        }

        return next;
    }
}
