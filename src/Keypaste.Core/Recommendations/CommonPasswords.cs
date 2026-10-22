using System.IO.Compression;

namespace Keypaste.Core.Recommendations;

/// <summary>The vendored list of the most common passwords, which KeePass's estimate rates each password against so one on it is weak (D-NEW-C5).</summary>
/// <remarks>
/// <para>
/// SecLists' <c>10k-most-common.txt</c>, embedded gzipped and pinned by the SHA-256 of the bytes it
/// decompresses to, which are upstream's file byte for byte. The pin is checked when the list is first
/// read and a mismatch throws, so no password is rated against a list nobody can vouch for, as the word
/// list is pinned (D-0235); third_party/seclists-common-passwords/UPSTREAM.md names this symbol and does
/// not restate the hex.
/// </para>
/// <para>
/// It is read in the process holding the vault, once, before the first password is rated, and needs no
/// network.
/// </para>
/// </remarks>
internal static class CommonPasswords
{
    /// <summary>How many passwords the list holds, one to a line.</summary>
    internal const int Count = 10_001;

    /// <summary>SHA-256 of the decompressed list, lowercase hex. The only place this pin is written.</summary>
    internal const string Digest = "68782d6a4a19a4768d5f15dd66bd534e7a33055cc755411e33f16d18c50fdcce";

    private const string _resource = "Keypaste.Core.10k-most-common.txt.gz";

    /// <summary>The list as upstream published it, one password to a line.</summary>
    /// <returns>Its bytes, which hold no secret of anyone's vault.</returns>
    /// <exception cref="InvalidOperationException">The embedded list is absent or is not the pinned one.</exception>
    internal static byte[] Read()
    {
        using var stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream(_resource)
            ?? throw new InvalidOperationException($"{_resource} is not embedded in Keypaste.Core");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var held = new MemoryStream();
        gzip.CopyTo(held);

        var bytes = held.ToArray();
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));

        return string.Equals(actual, Digest, StringComparison.Ordinal)
            ? bytes
            : throw new InvalidOperationException($"the common-password list is not the pinned one: expected {Digest}, found {actual}");
    }
}
