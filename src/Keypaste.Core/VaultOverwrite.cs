namespace Keypaste.Core;

/// <summary>Which vault a file about to be written would replace, if any.</summary>
public enum VaultOverwrite
{
    /// <summary>None: the file may be written.</summary>
    None = 0,

    /// <summary>The vault being read, named by the same path.</summary>
    TheVault = 1,

    /// <summary>The vault being read, named by another path to the same file.</summary>
    TheVaultElsewhere = 2,

    /// <summary>Another KeePass vault.</summary>
    AnotherVault = 3,
}

/// <summary>keypaste never writes a file over a vault: not the one it reads, by any path, and not any other KeePass vault.</summary>
public static class VaultOverwriteRule
{
    /// <summary>Which vault writing <paramref name="targetPath"/> would replace; nothing lifts a refusal.</summary>
    /// <param name="vaultPath">The vault being read.</param>
    /// <param name="targetPath">The full path about to be written.</param>
    /// <returns><see cref="VaultOverwrite.None"/> when the file may be written.</returns>
    /// <exception cref="ArgumentNullException">A path is null.</exception>
    public static VaultOverwrite Check(string vaultPath, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(vaultPath);
        ArgumentNullException.ThrowIfNull(targetPath);

        if (PathIdentity.SameFile(vaultPath, targetPath))
        {
            return string.Equals(targetPath, vaultPath, StringComparison.Ordinal) ? VaultOverwrite.TheVault : VaultOverwrite.TheVaultElsewhere;
        }

        return File.Exists(targetPath) && KdbxHeader.IsVaultFile(targetPath) ? VaultOverwrite.AnotherVault : VaultOverwrite.None;
    }
}
