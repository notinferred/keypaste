namespace Keypaste.Core;

/// <summary>Whether a merge of the open vault with its file was built, or why not.</summary>
public enum VaultMergeOutcome
{
    /// <summary>The merged copy was built.</summary>
    Merged = 0,

    /// <summary>The file's root group is not the open vault's, so it holds another vault and is not merged (D-0543).</summary>
    AnotherVault = 1,

    /// <summary>A copy holds two objects under one UUID, which KeePass's format forbids, so it is not merged (D-0541).</summary>
    DuplicateUuids = 2,
}

/// <summary>What became of a merge of another program's save with the open vault and the merged copy's save (D-0543).</summary>
public enum VaultMergeSaveOutcome
{
    /// <summary>The merged copy took something from the open copy and was saved over the file, which was kept as a backup.</summary>
    Saved = 0,

    /// <summary>The merged copy took nothing from the open copy, so it is the file's copy and nothing was written.</summary>
    Updated = 1,

    /// <summary>The file holds what the open copy last read or wrote, so there was nothing to merge.</summary>
    Unchanged = 2,

    /// <summary>The unlock's key no longer opens the file: another program changed what unlocks it.</summary>
    KeyRefused = 3,

    /// <summary>The hardware key was not connected or not touched; the merge is tried again at the next change.</summary>
    HardwareKeyUnanswered = 4,

    /// <summary>The file is missing, unreadable, not KDBX, truncated or fails its HMAC; the merge is tried again at the next change.</summary>
    Unreadable = 5,

    /// <summary>The file is below KDBX 4, which keypaste does not merge (D-0541).</summary>
    BelowKdbx4 = 6,

    /// <summary>The file's root group is not the open vault's, so it holds another vault and is not merged (D-0543).</summary>
    AnotherVault = 7,

    /// <summary>A copy holds two objects under one UUID, which KeePass's format forbids, so it is not merged (D-0541).</summary>
    DuplicateUuids = 8,

    /// <summary>Another save landed before each of the merged copy's saves, as often as one detection allows; the merge waits for the next change.</summary>
    ChangedAgain = 9,

    /// <summary>The merged copy could not be saved.</summary>
    SaveFailed = 10,

    /// <summary>The session ended before the merged copy was in its place, so nothing is served from it.</summary>
    Locked = 11,
}

/// <summary>What a merge of another program's save with the open vault gave, or why it gave no merged copy (D-0543).</summary>
/// <param name="Outcome">What became of it.</param>
/// <param name="Reason">Why there is no merged copy, as a sentence; empty when there is one.</param>
public sealed record VaultMergeSave(VaultMergeSaveOutcome Outcome, string Reason)
{
    /// <summary>
    /// The merged copy when <see cref="Outcome"/> is <see cref="VaultMergeSaveOutcome.Saved"/> or <see cref="VaultMergeSaveOutcome.Updated"/>,
    /// which the caller puts in the open copy's place and then owns; otherwise null.
    /// </summary>
    public Vault? Copy { get; init; }

    /// <summary>What the merge did, by name and never by value, once it merged.</summary>
    public VaultMergeReport? Report { get; init; }

    /// <summary>The file the merged copy's save replaced, kept whatever the backup floor says (D-0543).</summary>
    public VaultBackup? Kept { get; init; }

    /// <summary>Whether the owner's audit line of a merge it saved was written (D-0545).</summary>
    public bool Recorded { get; init; }
}

/// <summary>What a merge of the open vault with another program's save did, by name and never by value, for the notice and the audit line (D-0542, D-0545, D-0603).</summary>
/// <param name="TookFromOpenCopy">Whether the merged copy holds anything the file does not, so it must be saved; otherwise it is the file as it is (D-0543).</param>
/// <param name="Conflicts">Each item both copies changed, the version not current kept in its history.</param>
/// <param name="Kept">Each item one copy deleted after the other changed it, kept with that change.</param>
/// <param name="RenamedGroups">Each group both copies changed under different names, whose losing name KDBX keeps no history of.</param>
/// <param name="Agents">Each agent whose record in the merged copy differs from the open copy's, by name, in ordinal order.</param>
/// <remarks>Items in keypaste's own groups are named only as the agents whose records they are.</remarks>
public sealed record VaultMergeReport(
    bool TookFromOpenCopy,
    IReadOnlyList<MergeConflict> Conflicts,
    IReadOnlyList<MergeKeptItem> Kept,
    IReadOnlyList<MergeRenamedGroup> RenamedGroups,
    IReadOnlyList<string> Agents);

/// <summary>An item both copies changed.</summary>
/// <param name="Item">Its name in the merged copy.</param>
/// <param name="OpenCopyCurrent">Whether the open copy's version is current and the file's in its history; otherwise the reverse.</param>
public sealed record MergeConflict(EntryName Item, bool OpenCopyCurrent);

/// <summary>An item one copy deleted without seeing the other's change to it, kept with that change.</summary>
/// <param name="Item">Its name where the copy that changed it held it.</param>
/// <param name="DeletedInFile">Whether the file deleted it; otherwise the open copy did.</param>
/// <param name="Recycled">Whether it is kept in the recycle bin rather than where that copy held it.</param>
public sealed record MergeKeptItem(EntryName Item, bool DeletedInFile, bool Recycled);

/// <summary>A group both copies changed under different names.</summary>
/// <param name="GroupPath">Its path in the merged copy.</param>
/// <param name="LostName">The name the other version gave it.</param>
public sealed record MergeRenamedGroup(string GroupPath, string LostName);
