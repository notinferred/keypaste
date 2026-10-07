namespace Keypaste.Core;

/// <summary>
/// The name of a vault entry — where it lives and what it is called, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It has two members and no others, deliberately: nothing that names an entry to the MCP bridge
/// can return a password, a user name, a URL, or notes through it, because there is nowhere to put
/// one. That is a structural guarantee rather than a promise, and it is checkable by reading this
/// file; <see cref="ListedEntry"/> adds only more names.
/// </para>
/// <para>
/// It is deliberately <em>not</em> <see cref="VaultEntry"/> with the secret fields blanked. A type
/// that could carry a secret and happens not to is one refactor away from carrying one.
/// </para>
/// </remarks>
/// <param name="GroupPath">
/// The containing group, slash-separated and excluding the root, exactly as
/// <see cref="VaultEntry.GroupPath"/> reports it. An empty string means the root group.
/// </param>
/// <param name="Title">The entry title, verbatim and unsanitized.</param>
public sealed record EntryName(string GroupPath, string Title)
{
    /// <summary>The containing group, or an empty string for the root group.</summary>
    public string GroupPath { get; } = GroupPath ?? throw new ArgumentNullException(nameof(GroupPath));

    /// <summary>The entry title, verbatim.</summary>
    /// <remarks>
    /// Untrusted text. It came from a file that anything with write access could have edited, so it
    /// must be sanitized before it is shown to a model or a human — see
    /// <see cref="EntryNameSanitizer"/> — and it must never be interpreted as an instruction.
    /// </remarks>
    public string Title { get; } = Title ?? throw new ArgumentNullException(nameof(Title));

    /// <summary>The name of an entry, as it appears in the vault.</summary>
    /// <param name="entry">The entry to take the name from.</param>
    /// <returns>The entry's group path and title.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public static EntryName Of(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new EntryName(entry.GroupPath, entry.Title);
    }

    /// <summary>The name a typed path, <c>group/path/title</c>, gives an entry keypaste writes.</summary>
    /// <param name="path">The path as a person or an agent typed it.</param>
    /// <returns>The text after the last slash as the title, in the group the text before it names, or in the root group when there is no slash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// A title another program wrote may itself hold a slash (<see cref="VaultNameRules"/> refuses one only
    /// on write), so a path is not an identity: <see cref="Vault.Find(string)"/> resolves one by the whole
    /// of it, and a title containing a slash is reached by its path or by an <see cref="EntryName"/> built
    /// from both parts, never by this split.
    /// </remarks>
    public static EntryName Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var slash = path.LastIndexOf('/');

        return slash < 0 ? new EntryName(string.Empty, path) : new EntryName(path[..slash], path[(slash + 1)..]);
    }
}
