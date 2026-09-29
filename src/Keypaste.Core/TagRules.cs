namespace Keypaste.Core;

/// <summary>One entry's own tags, as <see cref="Vault.ReadTags"/> lists them.</summary>
/// <param name="Name">The entry.</param>
/// <param name="Tags">Its tags, never its group's.</param>
public sealed record EntryTags(EntryName Name, IReadOnlyList<string> Tags);

/// <summary>The tags keypaste is willing to write on an entry.</summary>
/// <remarks>
/// KeePass splits a tag list on <c>,</c> and <c>;</c> and trims each tag, so a tag holding either,
/// or starting or ending with whitespace, would not read back as the tag that was written. Enforced
/// on write only, as <see cref="VaultNameRules"/> is: a tag KeePassXC wrote is always read.
/// </remarks>
public static class TagRules
{
    /// <summary>Whether keypaste will write this tag.</summary>
    /// <param name="tag">The tag to check.</param>
    /// <param name="error">A message naming the problem, or empty when the tag is valid.</param>
    /// <returns><see langword="true"/> if the tag is valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    public static bool IsValid(string tag, out string error)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Length == 0)
        {
            error = "a tag cannot be empty";
            return false;
        }

        if (tag.IndexOfAny([',', ';']) >= 0)
        {
            error = "a tag cannot contain ',' or ';', which separate tags";
            return false;
        }

        if (tag.Any(char.IsControl))
        {
            error = "a tag cannot contain a tab or another control character";
            return false;
        }

        if (tag.Trim().Length != tag.Length)
        {
            error = "a tag cannot begin or end with whitespace";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
