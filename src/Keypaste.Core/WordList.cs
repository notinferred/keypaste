namespace Keypaste.Core;

/// <summary>The vendored word list a passphrase is drawn from.</summary>
/// <remarks>
/// <para>
/// <b>The list is the entropy claim.</b> A passphrase's strength is
/// <see cref="BitsPerWord"/> times its word count and nothing else, and for a share file that
/// figure plus Argon2 is the only protection there is (THREATS.md T-26). So the list is not
/// merely shipped, it is <i>pinned</i>: the bytes are hashed at first use and a mismatch throws.
/// A generator whose list is wrong must produce nothing rather than something whose entropy
/// nobody can state.
/// </para>
/// <para>
/// <b>The digest is written once, here.</b> third_party/eff-large-wordlist/UPSTREAM.md names this
/// symbol instead of restating the hex, because a pin written twice is a pin that can disagree
/// with itself (DECISIONS.md D-0203, D-0207) — and because the check has to hold inside a shipped
/// binary, which has no repository to read (D-0235).
/// </para>
/// <para>
/// <b><see cref="BitsPerWord"/> is computed, never typed.</b> D-0036 asks that a published claim
/// have something that can hold it. Deriving the figure from <see cref="Count"/> means the
/// documentation, the CLI's stderr line and the desktop's label cannot drift from the list; a
/// re-vendored list of a different size moves all three at once.
/// </para>
/// </remarks>
public static class WordList
{
    /// <summary>How many words the list holds.</summary>
    /// <remarks>
    /// 7776 is 6^5, which is why upstream addresses each word with five dice digits. The count is
    /// implied by <see cref="Digest"/> and checked anyway: it is the number every stated entropy
    /// figure rests on, and a load-bearing number is cheaper stated than assumed.
    /// </remarks>
    public const int Count = 7776;

    /// <summary>SHA-256 of the vendored file, lowercase hex. The only place this pin is written.</summary>
    public const string Digest = "addd35536511597a02fa0a9ff1e5284677b8883b83e986e43f15a3db996b903e";

    /// <summary>What to call the list where a person reads it.</summary>
    public const string Provenance = "EFF long list";

    private const string _resource = "Keypaste.Core.eff_large_wordlist.txt";

    private static readonly Lazy<string[]> _words =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>How much entropy one uniformly drawn word carries.</summary>
    public static double BitsPerWord { get; } = Math.Log2(Count);

    /// <summary>The words, in the order the vendored file lists them.</summary>
    /// <exception cref="InvalidOperationException">
    /// The embedded list is absent, is not the pinned one, or does not parse.
    /// </exception>
    public static ReadOnlySpan<string> Words => _words.Value;

    /// <summary>Whether the list holds <paramref name="candidate"/> anywhere in any word.</summary>
    /// <param name="candidate">The character to look for.</param>
    /// <returns><see langword="true"/> when some word contains it.</returns>
    /// <remarks>
    /// Asked by <see cref="PassphraseRecipe.TryValidate"/> rather than answered by a written-down
    /// character set: this list holds four hyphenated words, so <c>-</c> would make a six-word
    /// passphrase unsplittable, and a list re-vendored with different punctuation in it must
    /// narrow the separator rule by itself (D-0239).
    /// </remarks>
    public static bool Contains(char candidate)
    {
        foreach (var word in Words)
        {
            if (word.Contains(candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads, verifies and parses the embedded list.</summary>
    /// <remarks>
    /// The digest is compared over its bytes with <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// out of habit rather than need — there is no attacker on this path, but a hash comparison
    /// that is sometimes constant-time and sometimes not is a habit worth not having.
    /// </remarks>
    private static string[] Load()
    {
        var bytes = Read();
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(Digest)))
        {
            throw new InvalidOperationException(
                $"the word list is not the pinned one: expected {Digest}, found {actual}");
        }

        var words = Parse(bytes);

        if (words.Length != Count)
        {
            throw new InvalidOperationException(
                $"the word list holds {words.Length} words, expected {Count}");
        }

        return words;
    }

    private static byte[] Read()
    {
        using var stream = typeof(WordList).Assembly.GetManifestResourceStream(_resource)
            ?? throw new InvalidOperationException($"{_resource} is not embedded in Keypaste.Core");

        using var held = new MemoryStream();
        stream.CopyTo(held);

        return held.ToArray();
    }

    /// <summary>Takes the word after each line's tab, ignoring upstream's dice digits.</summary>
    private static string[] Parse(byte[] bytes)
    {
        var words = new List<string>(Count);

        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var tab = line.IndexOf('\t', StringComparison.Ordinal);

            if (tab < 0)
            {
                throw new InvalidOperationException($"the word list line '{line}' has no tab in it");
            }

            words.Add(line[(tab + 1)..]);
        }

        return [.. words];
    }
}
