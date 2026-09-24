namespace Keypaste.Core;

/// <summary>Whether an open vault still holds exactly what its file holds.</summary>
public enum SavedRead
{
    /// <summary>It does, and the entries were read.</summary>
    Current = 0,

    /// <summary>It holds a change no save has written yet, or a write is in progress.</summary>
    Unsaved = 1,

    /// <summary>Something else wrote the file since this vault last read or wrote it.</summary>
    ChangedOnDisk = 2,

    /// <summary>The file could not be read, so it could not be confirmed as unchanged.</summary>
    Unreadable = 3,
}

/// <summary>Which entries one change to an open vault touched.</summary>
/// <remarks>
/// A rename or a move touches the name it left and the name it took, because a grant is held under
/// a name and either could otherwise answer with a value the vault no longer holds there (D-0318).
/// </remarks>
public sealed class VaultEdit
{
    private VaultEdit(IReadOnlyList<EntryName> entries, bool everything)
    {
        Entries = entries;
        IsEverything = everything;
    }

    /// <summary>A change after which nothing released from the vault before it may be reused.</summary>
    public static VaultEdit Everything { get; } = new([], true);

    /// <summary>The names touched, unless <see cref="IsEverything"/>.</summary>
    public IReadOnlyList<EntryName> Entries { get; }

    /// <summary>Whether the change reaches every entry, as an access change does.</summary>
    public bool IsEverything { get; }

    /// <summary>A change touching these names; nulls are skipped.</summary>
    /// <param name="names">The names, in any order.</param>
    /// <returns>The edit.</returns>
    public static VaultEdit Of(params IEnumerable<EntryName?> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return new VaultEdit([.. names.OfType<EntryName>().Distinct()], false);
    }
}
