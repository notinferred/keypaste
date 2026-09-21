namespace Keypaste.Core;

/// <summary>
/// Which of an entry's addressable fields a query matched.
/// </summary>
/// <remarks>
/// <para>
/// <b>A field name, never a value.</b> This is the whole of what a search tells a caller about the
/// inside of an entry, and it is a fixed vocabulary of four: nothing that came out of the vault can
/// travel in it. A front end can say why a row is in a result without becoming a surface that
/// displays a username, which is the line <see cref="VaultEntry"/> draws for a listing and
/// <c>keypaste ls</c> draws for a terminal.
/// </para>
/// <para>
/// <b>There is no member for a password or a note, and that is not an omission.</b>
/// <see cref="Vault.Search"/> does not read those fields at all — a match on them is not suppressed
/// on the way out, it never happens. docs/STEPS.md V.5b requires that secret values are never
/// indexed or matched, and a flag for one would be the first step to indexing it.
/// </para>
/// </remarks>
[Flags]
public enum MatchedFields
{
    /// <summary>Nothing was compared. What an empty query reports for every entry.</summary>
    None = 0,

    /// <summary>The entry's title.</summary>
    Title = 1,

    /// <summary>The group path the entry was found at, joined as a listing joins it.</summary>
    GroupPath = 2,

    /// <summary>The entry's username.</summary>
    Username = 4,

    /// <summary>The entry's URL.</summary>
    Url = 8,
}

/// <summary>One entry a query found, and which of its fields the query was in.</summary>
/// <param name="Name">What addresses the entry: its group and its title.</param>
/// <param name="Fields">Every field that matched, or <see cref="MatchedFields.None"/>.</param>
/// <remarks>
/// The identity rather than the joined path, for the reason <see cref="EntryName"/> gives: joining
/// is lossy, and a caller that looked a result up by its path could reach a different entry than the
/// one that matched.
/// </remarks>
public sealed record EntryMatch(EntryName Name, MatchedFields Fields);
