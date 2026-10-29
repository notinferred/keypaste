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
