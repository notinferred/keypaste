namespace Keypaste.Core;

/// <summary>The copies a sync tool leaves beside a vault when two devices saved it at once.</summary>
public static class ConflictedCopies
{
    /// <summary>Finds them by the names Nextcloud, Dropbox and Syncthing give such a file.</summary>
    /// <param name="vaultPath">The vault.</param>
    /// <returns>Their full paths in ordinal order, none when the folder cannot be read.</returns>
    public static IReadOnlyList<string> Beside(string vaultPath)
    {
        ArgumentNullException.ThrowIfNull(vaultPath);

        var folder = Path.GetDirectoryName(Path.GetFullPath(vaultPath));
        var stem = Path.GetFileNameWithoutExtension(vaultPath);

        if (folder is null)
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder, "*.kdbx", SearchOption.TopDirectoryOnly)
                    .Where(path => IsConflictOf(stem, Path.GetFileNameWithoutExtension(path)))
                    .OrderBy(path => path, StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsConflictOf(string stem, string name) =>
        name.StartsWith(stem + ".sync-conflict-", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(')')
            && name.Contains("conflict", StringComparison.OrdinalIgnoreCase));
}
