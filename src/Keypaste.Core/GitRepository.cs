namespace Keypaste.Core;

/// <summary>Whether a path sits inside a git working tree.</summary>
/// <remarks>
/// Deliberately not a <c>git</c> subprocess. A <c>.git</c> entry says the file is inside a
/// repository, not that it was ever committed, so every caller phrases the finding conditionally
/// and hands the user the command to check. Claiming history for a <c>.gitignore</c>d file would be
/// the kind of overclaim SECURITY.md exists to avoid, and a subprocess on the secret path would
/// need its own justification under docs/PRODUCT.md law 3.9.
/// </remarks>
public static class GitRepository
{
    /// <summary>The root of the repository containing <paramref name="path"/>, if any.</summary>
    /// <param name="path">A file's full path.</param>
    /// <returns>The directory holding the <c>.git</c> entry, or null when no directory above the file has one.</returns>
    /// <remarks>
    /// A worktree and a submodule carry a <c>.git</c> <em>file</em> rather than a directory, so
    /// both are checked; looking only for the directory would miss exactly the setups where the
    /// history is somebody else's to clean up.
    /// </remarks>
    public static string? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var directory = Path.GetDirectoryName(path);

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, ".git");
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }
}
