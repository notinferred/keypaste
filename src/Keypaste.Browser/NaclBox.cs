namespace Keypaste.Browser;

/// <summary>
/// NaCl's box, X25519 with XSalsa20 and Poly1305, as KeePassXC-Browser's TweetNaCl.js seals and opens each message after its
/// key exchange: a key pair, a seal and an open, through libsodium and nothing else (D-0611).
/// </summary>
internal static class NaclBox
{
    /// <summary>The length of a public or a secret key.</summary>
    internal const int KeyLength = 32;

    /// <summary>The length of a nonce.</summary>
    internal const int NonceLength = 24;

    /// <summary>The length of the Poly1305 tag a box begins with.</summary>
    internal const int TagLength = 16;

    /// <summary>A new key pair from libsodium's random source; disposing it zeroes the secret key.</summary>
    internal static BoxKeyPair NewKeyPair()
    {
        var publicKey = new byte[KeyLength];
        var secretKey = GC.AllocateArray<byte>(KeyLength, pinned: true);

        if (Sodium.BoxKeyPair(publicKey, secretKey) != 0)
        {
            CryptographicOperations.ZeroMemory(secretKey);
            throw new CryptographicException("libsodium did not make a key pair");
        }

        return new BoxKeyPair(publicKey, secretKey);
    }

    /// <summary>The box of <paramref name="message"/> from the holder of <paramref name="secretKey"/> to the holder of <paramref name="publicKey"/>: its tag, then its ciphertext.</summary>
    /// <exception cref="CryptographicException">The public key is one of small order, whose shared key would be zero.</exception>
    internal static byte[] Seal(ReadOnlySpan<byte> message, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        RequireLengths(nonce, publicKey, secretKey);

        var box = new byte[TagLength + message.Length];

        return Sodium.BoxEasy(box, message, nonce, publicKey, secretKey) == 0
            ? box
            : throw new CryptographicException("libsodium refused to seal a box to a public key of small order");
    }

    /// <summary>The message <paramref name="box"/> holds, or <see langword="null"/> when its tag does not verify under these keys and nonce.</summary>
    internal static byte[]? Open(ReadOnlySpan<byte> box, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        RequireLengths(nonce, publicKey, secretKey);

        if (box.Length < TagLength)
        {
            return null;
        }

        var message = new byte[box.Length - TagLength];

        return Sodium.BoxOpenEasy(message, box, nonce, publicKey, secretKey) == 0 ? message : null;
    }

    private static void RequireLengths(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException($"a nonce is {NonceLength} bytes, not {nonce.Length}", nameof(nonce));
        }

        if (publicKey.Length != KeyLength)
        {
            throw new ArgumentException($"a public key is {KeyLength} bytes, not {publicKey.Length}", nameof(publicKey));
        }

        if (secretKey.Length != KeyLength)
        {
            throw new ArgumentException($"a secret key is {KeyLength} bytes, not {secretKey.Length}", nameof(secretKey));
        }
    }
}
