namespace Keypaste.Core;

/// <summary>An entry as a listing names it to an agent: where it is, which fields may be asked for and its project tags (D-0422).</summary>
/// <remarks>
/// Every member is a name, so like <see cref="EntryName"/> it has nowhere to put a value (D-0022).
/// </remarks>
/// <param name="Name">The entry, unsanitized.</param>
/// <param name="Fields">The fields that hold something and may be asked for: the standard ones first, in <see cref="Approval.CredentialFields.All"/> order, then custom ones, ordinally.</param>
/// <param name="Tags">Its own project tags as written, each one <see cref="ProjectTag"/> reads as a member.</param>
public sealed record ListedEntry(EntryName Name, IReadOnlyList<string> Fields, IReadOnlyList<string> Tags)
{
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
}
