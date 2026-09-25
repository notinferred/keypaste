using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Projects;

/// <summary>Which project a directory belongs to, from the mappings in <c>projects.json</c>.</summary>
/// <remarks>
/// The mapping a person saved decides, never a file in the directory: a cloned repository can hold
/// any file it likes, and cannot write this machine's <c>projects.json</c> (THREATS.md T-31).
/// </remarks>
public static class ProjectInference
{
    /// <summary>Why no project was inferred when no mapping covers the directory.</summary>
    public const string NotMapped = "this directory is not mapped to a project";

    /// <summary>The project mapped to a directory or its nearest mapped ancestor, for one vault.</summary>
    /// <param name="mappings">The mappings.</param>
    /// <param name="vaultPath">The vault in use; mappings of other vaults are ignored.</param>
    /// <param name="directory">The directory, as a full path.</param>
    /// <param name="project">The project, when this returns true.</param>
    /// <param name="error">Why none, in words a front end prefixes with its own verb.</param>
    /// <returns>Whether exactly one project is mapped there.</returns>
    public static bool TryInfer(
        IReadOnlyList<ProjectMapping> mappings,
        string vaultPath,
        string directory,
        [NotNullWhen(true)] out string? project,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(vaultPath);
        ArgumentNullException.ThrowIfNull(directory);

        project = null;

        var vault = Path.GetFullPath(vaultPath);
        var here = Normal(directory);

        var candidates = mappings
            .Where(mapping => string.Equals(Path.GetFullPath(mapping.Vault), vault, PathIdentity.Comparison))
            .Select(mapping => (mapping.Project, Directory: Normal(mapping.Directory)))
            .Where(mapping => Contains(mapping.Directory, here))
            .ToList();

        if (candidates.Count == 0)
        {
            error = NotMapped;
            return false;
        }

        var nearest = candidates.Max(mapping => mapping.Directory.Length);
        var projects = candidates
            .Where(mapping => mapping.Directory.Length == nearest)
            .Select(mapping => mapping.Project)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (projects.Count > 1)
        {
            error = $"this directory is mapped to more than one project ({string.Join(", ", projects)}); name one";
            return false;
        }

        project = projects[0];
        error = string.Empty;
        return true;
    }

    private static string Normal(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    private static bool Contains(string ancestor, string directory)
    {
        if (string.Equals(ancestor, directory, PathIdentity.Comparison))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(ancestor) ? ancestor : ancestor + Path.DirectorySeparatorChar;
        return directory.StartsWith(prefix, PathIdentity.Comparison);
    }
}
