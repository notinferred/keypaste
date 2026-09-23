using Keypaste.Core.Audit;

namespace Keypaste.Core.Ownership;

/// <summary>One vault as every keypaste process names it, so they agree on who holds it.</summary>
/// <remarks>
/// The key folds case where <see cref="PathIdentity.Comparison"/> does, so two spellings of one file
/// meet at one claim and one endpoint. The residuals <see cref="PathIdentity"/> states apply here too.
/// </remarks>
public sealed class VaultIdentity
{
    /// <summary>The number of hex characters in <see cref="Key"/>.</summary>
    public const int KeyLength = 16;

    private VaultIdentity(string path, string key)
    {
        Path = path;
        Key = key;
    }

    /// <summary>The vault's canonical path.</summary>
    public string Path { get; }

    /// <summary>A non-secret discriminator over the user, keypaste's home and the vault.</summary>
    public string Key { get; }

    /// <summary>Identifies a vault for one keypaste home.</summary>
    /// <param name="home">From <see cref="KeypasteHome.Resolve"/>.</param>
    /// <param name="vaultPath">The vault file, which need not exist yet.</param>
    /// <returns>The identity.</returns>
    public static VaultIdentity Of(string home, string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var path = PathIdentity.Canonical(vaultPath);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var material = string.Join('\n', profile, Folded(PathIdentity.Canonical(home)), Folded(path));

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), digest);

        return new VaultIdentity(path, Convert.ToHexStringLower(digest[..(KeyLength / 2)]));
    }

    /// <summary>Whether a path names this vault.</summary>
    /// <param name="vaultPath">The path another process named.</param>
    /// <returns><see langword="true"/> when both canonicalise to one file.</returns>
    public bool Names(string vaultPath) =>
        !string.IsNullOrEmpty(vaultPath)
        && string.Equals(PathIdentity.Canonical(vaultPath), Path, PathIdentity.Comparison);

    private static string Folded(string path) =>
        PathIdentity.Comparison == StringComparison.Ordinal ? path : path.ToUpperInvariant();
}
