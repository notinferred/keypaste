namespace Keypaste.Core;

/// <summary>What a delete did to the entry.</summary>
/// <remarks>
/// A vault either has a recycle bin or does not, and the answer is the vault's rather than
/// keypaste's — see <see cref="Vault.RecyclesDeletedEntries"/>. A caller that must word a
/// confirmation before the act reads that property; this says what actually happened.
/// </remarks>
public enum DeletionOutcome
{
    /// <summary>No entry answered to that name. Nothing was changed.</summary>
    NothingMatched = 0,

    /// <summary>The entry was moved to the recycle bin and can be restored.</summary>
    Recycled = 1,

    /// <summary>The entry and its history were removed. Nothing can restore them.</summary>
    DeletedPermanently = 2,
}

/// <summary>What a restore did with the entry.</summary>
public enum RestoreOutcome
{
    /// <summary>No recycled entry has that identity. Nothing was changed.</summary>
    NothingMatched = 0,

    /// <summary>The entry is back in the group it was deleted from.</summary>
    Restored = 1,

    /// <summary>
    /// The entry is back, in the root group, because the group it was deleted from is gone or is
    /// itself recycled.
    /// </summary>
    RestoredToRoot = 2,

    /// <summary>
    /// Another entry already answers to the name the restore would produce, so nothing was
    /// changed. Two entries of one name is the ambiguity every resolver in keypaste refuses
    /// (DECISIONS.md D-0091); a recovery must not create it.
    /// </summary>
    DestinationOccupied = 3,
}

/// <summary>
/// The identity of a recycled entry: stable across a save, a reopen and a reordering of the
/// trash, and opaque.
/// </summary>
/// <remarks>
/// <para>
/// keypaste addresses entries by name (DECISIONS.md D-0091), and a recycled entry cannot be:
/// deleting two entries called <c>TOKEN</c> from two projects puts two entries called
/// <c>TOKEN</c> in one bin. A position in the trash listing cannot serve either — D-0229 settled
/// that an index addresses one reading and is never a persistent identifier.
/// </para>
/// <para>
/// So this is the one durable address, and it is deliberately opaque: what is inside it is the
/// KDBX UUID, and no caller is told that. A second general-purpose address form beside the name
/// and the handle is exactly what D-0091 warned against, and this one reaches only the trash.
/// </para>
/// </remarks>
public readonly record struct RecycledEntryId
{
    private readonly string? _value;

    private RecycledEntryId(string value) => _value = value;

    /// <summary>The number of characters <see cref="ToString"/> produces.</summary>
    public const int Length = 32;

    /// <summary>Reads an identity back from the text <see cref="ToString"/> produced.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="id">The identity, when this returns true.</param>
    /// <returns>Whether <paramref name="text"/> was an identity.</returns>
    /// <remarks>
    /// Exists so an identity can survive a process boundary — the compatibility gate's driver
    /// lists the trash in one invocation and restores in the next. Nothing about a refusal here
    /// says whether the vault holds such an entry; that is <see cref="Vault.RestoreRecycled"/>'s
    /// answer, and it is <see cref="RestoreOutcome.NothingMatched"/>.
    /// </remarks>
    public static bool TryParse(string? text, out RecycledEntryId id)
    {
        id = default;

        if (text is null || text.Length != Length)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        id = new RecycledEntryId(text.ToUpperInvariant());
        return true;
    }

    /// <summary>The identity as text, or an empty string when there is no identity.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>The identity of an entry whose KDBX UUID is this hex string.</summary>
    /// <remarks>
    /// Internal because the mapping to a UUID is the part callers must not know. The only
    /// producer is the vault itself.
    /// </remarks>
    internal static RecycledEntryId FromUuidHex(string hex)
    {
        ArgumentException.ThrowIfNullOrEmpty(hex);

        return new RecycledEntryId(hex.ToUpperInvariant());
    }

    /// <summary>The KDBX UUID this identity stands for, as hex.</summary>
    internal string UuidHex => _value ?? string.Empty;
}

/// <summary>
/// One entry in the recycle bin — enough to recognize it and put it back, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This carries no <c>Password</c>, <c>Username</c>, <c>Url</c> or <c>Notes</c>, for the reason
/// <see cref="EntryName"/> gives: a listing type that could hold a secret is one refactor away
/// from holding one, and a trash view holds its whole list for as long as it is open. A restored
/// entry is read back through <see cref="Vault.Find(EntryName)"/> like any other.
/// </para>
/// <para>
/// The rule is held by a test rather than by this paragraph —
/// <c>SecretHygieneTests</c> walks every string these rows expose and fails on a sentinel value.
/// </para>
/// </remarks>
/// <param name="Id">The identity to restore or purge by.</param>
/// <param name="Title">The entry title, verbatim and unsanitized.</param>
/// <param name="OriginalGroupPath">
/// The group the entry was deleted from, or null when the vault does not record one — an entry
/// another program put in the bin, or one recycled before the original location was kept.
/// </param>
/// <param name="DeletedUtc">When the entry was moved to the bin.</param>
public sealed record RecycledEntry(
    RecycledEntryId Id,
    string Title,
    string? OriginalGroupPath,
    DateTime DeletedUtc)
{
    /// <summary>The entry title, verbatim.</summary>
    /// <remarks>
    /// Untrusted text, exactly as <see cref="EntryName.Title"/> is: it came from a file anything
    /// with write access could have edited, so it is sanitized before it is shown.
    /// </remarks>
    public string Title { get; } = Title ?? throw new ArgumentNullException(nameof(Title));
}
