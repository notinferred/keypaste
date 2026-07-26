namespace Keypaste.Core;

/// <summary>
/// A stable, injection-proof way for an agent to name an entry back to keypaste.
/// </summary>
/// <remarks>
/// <para>
/// The listing an agent sees is <em>sanitized</em> (<see cref="EntryNameSanitizer"/>), and
/// sanitizing is lossy, so the displayed title cannot be used to address the entry it came from. A
/// handle solves that: it is derived from the real name, contains no attacker-controlled bytes, and
/// is therefore safe to echo anywhere.
/// </para>
/// <para>
/// <b>The NUL separator is the whole point.</b> <see cref="VaultEntry.Path"/> is
/// <c>GroupPath + "/" + Title</c> with no escaping, so an entry titled <c>b/c</c> in group <c>a</c>
/// and an entry titled <c>c</c> in group <c>a/b</c> have the same path and are genuinely
/// indistinguishable by it. Hashing the two parts with a separator that cannot occur in either
/// gives them different handles, so each remains uniquely addressable. Addressing by path stays
/// available and stays ambiguous — a colliding path is refused rather than guessed at (CORE.md law
/// 3.7).
/// </para>
/// <para>
/// <b>What a handle is not.</b> It is not a secret, and knowing one is not evidence of
/// authorization. It is a stable name and nothing more; every request that carries one is still
/// subject to the same approval. It is also not a privacy measure — it happens to hash a name, but
/// the reason is addressability, and law 3.5 is satisfied by the audit log never leaving the
/// machine rather than by anything here.
/// </para>
/// </remarks>
public static class EntryHandle
{
    /// <summary>
    /// The version marker every handle starts with, so a future change to how handles are derived
    /// is unambiguous rather than silently wrong.
    /// </summary>
    public const string Prefix = "k1_";

    /// <summary>The number of hex characters after <see cref="Prefix"/>.</summary>
    public const int DigestLength = 16;

    /// <summary>The exact length of a handle.</summary>
    public const int Length = 3 + DigestLength;

    /// <summary>Derives the handle for an entry name.</summary>
    /// <param name="name">The real, unsanitized name.</param>
    /// <returns>A handle such as <c>k1_9f3a1c02b7d54e60</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static string For(EntryName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var groupBytes = Encoding.UTF8.GetByteCount(name.GroupPath);
        var titleBytes = Encoding.UTF8.GetByteCount(name.Title);

        var buffer = new byte[groupBytes + 1 + titleBytes];
        Encoding.UTF8.GetBytes(name.GroupPath, buffer);
        buffer[groupBytes] = 0;
        Encoding.UTF8.GetBytes(name.Title, buffer.AsSpan(groupBytes + 1));

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(buffer, digest);

        return Prefix + Convert.ToHexStringLower(digest[..(DigestLength / 2)]);
    }

    /// <summary>Whether a string is shaped like a handle.</summary>
    /// <param name="value">The argument an agent supplied.</param>
    /// <returns>
    /// <see langword="true"/> if it has the right prefix, length, and alphabet. This says nothing
    /// about whether any entry actually has that handle.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <remarks>
    /// Used to decide whether to resolve an <c>entry</c> argument as a handle or as a path, and to
    /// record which form was used in the audit log. A vault entry could in principle be titled
    /// <c>k1_0123456789abcdef</c>; resolution therefore tries handles first and falls back to an
    /// exact path match, so such an entry is still reachable.
    /// </remarks>
    public static bool LooksLikeHandle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != Length || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = Prefix.Length; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsAsciiDigit(c) && c is < 'a' or > 'f')
            {
                return false;
            }
        }

        return true;
    }
}
