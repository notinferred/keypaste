namespace Keypaste.Core;

/// <summary>One live entry as project resolution reads it.</summary>
/// <param name="Entry">Its standard fields and place.</param>
/// <param name="Tags">Its own tags, never its group's.</param>
/// <param name="Fields">
/// Its custom fields <see cref="EnvConvention.IsEnvNamedField"/> accepts, with their values, in
/// ordinal order of name, read only when a project tag names the entry.
/// </param>
internal sealed record EnvEntry(VaultEntry Entry, IReadOnlyList<string> Tags, IReadOnlyList<KeyValuePair<string, string>> Fields)
{
    /// <summary>The entry's name.</summary>
    public EntryName Name => EntryName.Of(Entry);

    /// <summary>Whether one of its own tags puts it in a project's environment.</summary>
    /// <param name="project">The project.</param>
    /// <param name="environment">The environment.</param>
    /// <returns><see langword="true"/> for a member tag naming both.</returns>
    public bool IsTaggedInto(string project, string environment) =>
        Tags.Select(ProjectTag.Read).Any(tag => tag.Kind == ProjectTagKind.Member
            && string.Equals(tag.Project, project, StringComparison.Ordinal)
            && string.Equals(tag.Environment, environment, StringComparison.Ordinal));

    /// <summary>Whether any of its own tags puts it in any project.</summary>
    public bool IsTagged => Tags.Any(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member);
}

/// <summary>Every live entry and group path of a vault, read together, for resolving projects.</summary>
/// <param name="Entries">Every entry outside the recycle bin, depth-first.</param>
/// <param name="GroupPaths">Every group path outside the recycle bin.</param>
internal sealed record EnvSnapshot(IReadOnlyList<EnvEntry> Entries, IReadOnlyList<string> GroupPaths);
