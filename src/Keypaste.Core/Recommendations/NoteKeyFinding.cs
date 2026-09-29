namespace Keypaste.Core.Recommendations;

/// <summary>How a key was found in an entry's notes.</summary>
public enum NoteKeyKind
{
    /// <summary>A line <c>KEY=value</c> or <c>export KEY=value</c> whose key is env-named.</summary>
    Assignment,

    /// <summary>A line that is exactly one well-known token (<see cref="TokenPrefixes"/>).</summary>
    Token,
}

/// <summary>One key left in an entry's notes, named by entry and key, never by value (C.2).</summary>
/// <remarks>
/// It holds where the key is and which field it would become. The value stays in the notes; the
/// move reads it again under the vault's lock, and only while the notes are still what
/// <see cref="NoteKeyCheck"/> saw.
/// </remarks>
public sealed class NoteKeyFinding
{
    internal NoteKeyFinding(NoteKeyCheck check, EntryName entry, string entryUuid, ParsedNoteKey key, bool isRepeated, byte[] stamp)
    {
        Check = check;
        Entry = entry;
        EntryUuid = entryUuid;
        Field = key.Field;
        Kind = key.Kind;
        TokenKind = key.Token?.Name;
        Line = key.Line;
        IsRepeated = isRepeated;
        Stamp = stamp;
    }

    /// <summary>The entry whose notes hold the key.</summary>
    public EntryName Entry { get; }

    /// <summary>The entry's KDBX identifier as hex, which survives a rename.</summary>
    public string EntryUuid { get; }

    /// <summary>The field the value would be written to: the key, or the token kind's field.</summary>
    public string Field { get; }

    /// <summary>How the key was found.</summary>
    public NoteKeyKind Kind { get; }

    /// <summary>The token kind's name, such as "GitHub token", for a <see cref="NoteKeyKind.Token"/> finding.</summary>
    public string? TokenKind { get; }

    /// <summary>The zero-based line of the notes the key is on.</summary>
    public int Line { get; }

    /// <summary>Whether another line of the same notes names the same field, which the move refuses.</summary>
    public bool IsRepeated { get; }

    internal NoteKeyCheck Check { get; }

    internal byte[] Stamp { get; }

    /// <summary>The entry and the field, never the value.</summary>
    /// <returns>A description for logs and debuggers.</returns>
    public override string ToString() =>
        $"{(Entry.GroupPath.Length == 0 ? Entry.Title : Entry.GroupPath + "/" + Entry.Title)} {Field}";
}

/// <summary>Why a move of keys out of notes was refused. Nothing was changed.</summary>
public enum NoteKeyRefusalReason
{
    /// <summary>The entry is no longer in the vault under that name.</summary>
    EntryGone,

    /// <summary>The entry's notes changed after they were checked.</summary>
    NotesChanged,

    /// <summary>The entry already has that field, holding a different value.</summary>
    FieldHoldsOtherValue,

    /// <summary>The notes set the same field on more than one line.</summary>
    KeyRepeated,
}

/// <summary>One finding a move refused, and why.</summary>
/// <param name="Finding">The finding.</param>
/// <param name="Reason">Why.</param>
public sealed record NoteKeyRefusal(NoteKeyFinding Finding, NoteKeyRefusalReason Reason);

/// <summary>What <see cref="Vault.MoveNoteKeys"/> did.</summary>
/// <param name="Moved">Whether the keys were moved. A refusal of any finding refuses them all.</param>
/// <param name="Entries">The entries that changed, one revision each.</param>
/// <param name="Refusals">Why nothing was moved, when <paramref name="Moved"/> is false.</param>
public sealed record NoteKeyMove(bool Moved, IReadOnlyList<EntryName> Entries, IReadOnlyList<NoteKeyRefusal> Refusals);

/// <summary>A key found on one line, with its value, held only while a move is planned.</summary>
internal sealed record ParsedNoteKey(int Line, int Start, int Length, string Field, NoteKeyKind Kind, TokenKind? Token, string Value)
{
    public override string ToString() => Field;
}

/// <summary>One live entry's notes, as the check reads them.</summary>
internal sealed record EntryNotes(EntryName Entry, string Uuid, string Notes)
{
    public override string ToString() => Entry.Title;
}
