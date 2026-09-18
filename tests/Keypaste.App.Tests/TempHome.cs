using System.Security.Cryptography;

namespace Keypaste.App.Tests;

/// <summary>
/// A <c>KEYPASTE_HOME</c> with nothing in it: no vault, no <c>recent.toml</c>.
/// </summary>
/// <remarks>
/// <see cref="TempVault"/> always writes a vault, which is the wrong starting point for the one
/// thing 4.8 is about — somebody who has just installed the app and owns no vault at all.
/// </remarks>
internal sealed class TempHome : IDisposable
{
    internal const string Password = "correct-horse-battery-staple";

    internal TempHome() =>
        Path = Directory.CreateTempSubdirectory("keypaste-home-tests-").FullName;

    /// <summary>The directory, handed to a view model as its <c>home</c>.</summary>
    internal string Path { get; }

    /// <summary>A path inside it that no file occupies.</summary>
    internal string FreeVaultPath => System.IO.Path.Combine(Path, "new.kdbx");

    /// <summary>
    /// Every file under the home, as path, length and digest.
    /// </summary>
    /// <remarks>
    /// Compared before and after, this is what makes "nothing was written" mean more than
    /// <c>File.Exists</c> on one path. It catches a <c>recent.toml</c> that should not be there, a
    /// stranded <c>vault.kdbx.tmp</c> from a half-run save, and a zero-byte file left by a picker.
    /// </remarks>
    internal IReadOnlyList<string> Snapshot() =>
        [.. Directory
            .EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .Select(file =>
                $"{System.IO.Path.GetRelativePath(Path, file).Replace('\\', '/')} " +
                $"{new FileInfo(file).Length} " +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
            .Order(StringComparer.Ordinal)];

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }
}
