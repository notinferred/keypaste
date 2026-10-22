using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The vendored common-password list, and the pin that makes rating against it mean something (D-NEW-C5).
/// </summary>
/// <remarks>
/// The digest and the line count are read from the file in the repository, never from
/// <see cref="CommonPasswords.Read"/>, so the test is not asking the code whether the code is right;
/// the shipped resource is then held to the same bytes.
/// </remarks>
public sealed class CommonPasswordsTests
{
    private static string ListPath =>
        Path.Combine(VendoredWordList.RepoRoot(), "third_party", "seclists-common-passwords", "10k-most-common.txt.gz");

    [Fact]
    public void The_vendored_list_decompresses_to_the_pinned_bytes_and_holds_every_password_once()
    {
        var bytes = Decompressed(File.ReadAllBytes(ListPath));
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(CommonPasswords.Digest, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(CommonPasswords.Count, lines.Length);
        Assert.Equal(lines.Length, lines.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("password", lines);
    }

    [Fact]
    public void The_shipped_list_is_the_vendored_one()
    {
        Assert.Equal(Decompressed(File.ReadAllBytes(ListPath)), CommonPasswords.Read());
    }

    private static byte[] Decompressed(byte[] compressed)
    {
        using var source = new MemoryStream(compressed);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var held = new MemoryStream();
        gzip.CopyTo(held);
        return held.ToArray();
    }
}
