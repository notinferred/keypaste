namespace Keypaste.Core.Tests;

/// <summary>
/// The word list as the repository holds it, read independently of the code under test.
/// </summary>
/// <remarks>
/// One oracle, shared by <see cref="WordListTests"/> and <see cref="PassphraseGeneratorTests"/>.
/// Both could reach for <see cref="WordList.Words"/> and both would then be asking the
/// implementation whether the implementation is right; reading the vendored file is what makes
/// the digest, the size and the per-word entropy figure checkable rather than self-consistent.
/// </remarks>
internal static class VendoredWordList
{
    /// <summary>The directory the list and its provenance live in.</summary>
    internal static string Directory => Path.Combine(RepoRoot(), "third_party", "eff-large-wordlist");

    /// <summary>The vendored file itself.</summary>
    internal static string ListPath => Path.Combine(Directory, "eff_large_wordlist.txt");

    /// <summary>The words, taken from the column after each line's tab.</summary>
    internal static string[] Words() =>
    [
        .. File.ReadAllLines(ListPath)
            .Where(line => line.Length > 0)
            .Select(line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..]),
    ];

    /// <summary>
    /// The checkout this test runs inside.
    /// </summary>
    /// <remarks>
    /// Walks up from the output directory rather than using <c>CallerFilePath</c>: the root props
    /// set <c>ContinuousIntegrationBuild</c> on CI, which rewrites compile-time paths to
    /// <c>/_/…</c>, so on CI and only on CI a caller path names a directory that never existed.
    /// Same reasoning as CompatGateIsPermanentTests.
    /// </remarks>
    internal static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Could not locate keypaste.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
