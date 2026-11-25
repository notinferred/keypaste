namespace Keypaste.Core;

/// <summary>An entry as a listing names it to an agent: where it is, which fields may be asked for and its project tags.</summary>
/// <remarks>
/// Every member is a name, so like <see cref="EntryName"/> it has nowhere to put a value.
/// </remarks>
/// <param name="Name">The entry, unsanitized.</param>
/// <param name="Fields">The fields that hold something and may be asked for: the standard ones first, in <see cref="Approval.CredentialFields.All"/> order, then <c>totp</c> when a field a TOTP is read from holds anything, then custom ones, ordinally.</param>
/// <param name="Tags">Its own project tags as written, each one <see cref="ProjectTag"/> reads as a member.</param>
public sealed record ListedEntry(EntryName Name, IReadOnlyList<string> Fields, IReadOnlyList<string> Tags)
{
    /// <summary>Its own project tags as written, each one <see cref="ProjectTag"/> reads as a member.</summary>
    public IReadOnlyList<string> Tags
    {
        get;
        init
        {
            field = value;
            Projects = ProjectsOf(value);
        }
    } = Tags;

    /// <summary>The projects its own tags make it a member of, ordinally and each once.</summary>
    /// <remarks>Read once from <see cref="Tags"/> and again whenever they are set, since a search reads it for every pair of entries.</remarks>
    internal IReadOnlyList<string> Projects { get; private init; } = ProjectsOf(Tags);

    /// <summary>This entry as a reach shows it: the fields the reach covers.</summary>
    /// <param name="reach">How much of the entry an exposure reaches.</param>
    /// <returns>The entry with only those fields.</returns>
    public ListedEntry Within(ExposureReach reach) =>
        this with { Fields = [.. Fields.Where(field => EntryExposure.Permits(reach, field))] };

    /// <summary>Whether another names the same entry, fields and tags, in the same order.</summary>
    /// <param name="other">The other entry.</param>
    /// <returns><see langword="true"/> when every name matches ordinally.</returns>
    public bool Equals(ListedEntry? other) =>
        other is not null
        && Name == other.Name
        && Fields.SequenceEqual(other.Fields, StringComparer.Ordinal)
        && Tags.SequenceEqual(other.Tags, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => Name.GetHashCode();

    private static IReadOnlyList<string> ProjectsOf(IReadOnlyList<string> tags) =>
        [.. tags.Select(ProjectTag.Read).Select(tag => tag.Project).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
