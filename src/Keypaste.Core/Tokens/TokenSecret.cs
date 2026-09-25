using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Tokens;

/// <summary>
/// A scoped token's text, <c>kpt_&lt;id&gt;_&lt;secret&gt;</c>, and what is derived from its secret.
/// </summary>
/// <remarks>
/// <para>
/// The id is eight lowercase hex characters and the secret is 32 random bytes in base64url, so a
/// token is always 56 characters. It is read by position, never by splitting on <c>_</c>, because
/// the secret itself holds <c>_</c> or <c>-</c> in about half of all tokens, and only its one
/// canonical spelling parses.
/// </para>
/// <para>
/// HKDF rather than a slow KDF: the secret is 256 uniform random bits, so stretching adds nothing.
/// The verifier and the bundle key use different <c>info</c> strings, so the verifier a vault stores
/// cannot open a bundle.
/// </para>
/// </remarks>
public static class TokenSecret
{
    /// <summary>What every token starts with.</summary>
    public const string Prefix = "kpt_";

    /// <summary>The length of every token.</summary>
    public const int Length = 56;

    /// <summary>The length of a token's id.</summary>
    public const int IdLength = 8;

    /// <summary>The number of random bytes in a token's secret.</summary>
    public const int SecretBytes = 32;

    internal const int SeparatorIndex = 12;

    /// <summary>Mints a token.</summary>
    /// <param name="id">The token's id.</param>
    /// <param name="secret">The secret's bytes; the caller zeroes them.</param>
    /// <returns>The token, which is shown once and never stored.</returns>
    public static string New(out string id, out byte[] secret)
    {
        id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdLength / 2));
        secret = RandomNumberGenerator.GetBytes(SecretBytes);

        return Prefix + id + "_" + Base64Url.EncodeToString(secret);
    }

    /// <summary>Reads a token's id and secret, accepting only the exact spelling <see cref="New"/> produces.</summary>
    /// <param name="token">The text given as a token.</param>
    /// <param name="id">The id, when it parsed.</param>
    /// <param name="secret">The secret's bytes, when it parsed; the caller zeroes them.</param>
    /// <returns>Whether <paramref name="token"/> is a well-formed token.</returns>
    public static bool TryParse(string token, [NotNullWhen(true)] out string? id, [NotNullWhen(true)] out byte[]? secret)
    {
        id = null;
        secret = null;

        if (token is null
            || token.Length != Length
            || !token.StartsWith(Prefix, StringComparison.Ordinal)
            || token[SeparatorIndex] != '_')
        {
            return false;
        }

        var hex = token.AsSpan(Prefix.Length, IdLength);
        foreach (var c in hex)
        {
            if (!char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        var text = token.AsSpan(SeparatorIndex + 1);
        var bytes = new byte[SecretBytes];

        if (!Base64Url.IsValid(text, out var length)
            || length != SecretBytes
            || !Base64Url.TryDecodeFromChars(text, bytes, out var written)
            || written != SecretBytes
            || !text.SequenceEqual(Base64Url.EncodeToString(bytes)))
        {
            CryptographicOperations.ZeroMemory(bytes);
            return false;
        }

        id = hex.ToString();
        secret = bytes;
        return true;
    }

    /// <summary>What a vault stores for a token: proof of its secret that cannot be turned back into it.</summary>
    /// <param name="id">The token's id, which salts the derivation.</param>
    /// <param name="secret">The token's secret.</param>
    /// <returns>32 bytes as lowercase hex.</returns>
    public static string Verifier(string id, ReadOnlySpan<byte> secret)
    {
        ArgumentNullException.ThrowIfNull(id);

        Span<byte> verifier = stackalloc byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            secret,
            verifier,
            Encoding.UTF8.GetBytes("keypaste token " + id),
            "keypaste token verifier v1"u8);

        return Convert.ToHexStringLower(verifier);
    }

    /// <summary>The key a bundle made for this token is sealed under.</summary>
    /// <param name="secret">The token's secret.</param>
    /// <param name="salt">The bundle's own random salt.</param>
    /// <returns>A 256-bit key; the caller zeroes it.</returns>
    public static byte[] BundleKey(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> salt)
    {
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, key, salt, "keypaste bundle v1"u8);
        return key;
    }

    /// <summary>How a token is shown once it has been handed over: its id and never its secret.</summary>
    /// <param name="id">The token's id.</param>
    /// <returns><c>kpt_&lt;id&gt;…</c>.</returns>
    public static string Display(string id) => $"{Prefix}{id}…";
}
