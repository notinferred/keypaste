namespace Keypaste.Core;

/// <summary>Whether an open vault's file holds what the vault holds.</summary>
public enum VaultSaveStatus
{
    /// <summary>The file holds exactly what is open.</summary>
    Saved = 0,

    /// <summary>A change made here is not saved yet.</summary>
    Unsaved = 1,

    /// <summary>Something else wrote the file since this vault read or wrote it.</summary>
    ChangedOnDisk = 2,

    /// <summary>The file could not be read, so it could not be compared.</summary>
    Unreadable = 3,
}

/// <summary>An open vault's save state and when its file was last written by it.</summary>
/// <param name="Status">Whether the file holds what is open.</param>
/// <param name="SavedAt">The file's write time when this vault last opened or saved it, or null.</param>
public sealed record VaultSaveState(VaultSaveStatus Status, DateTimeOffset? SavedAt);

/// <summary>When an entry was created and last modified.</summary>
/// <param name="Created">When it was created.</param>
/// <param name="Modified">When any field of it last changed.</param>
public sealed record EntryTimes(DateTimeOffset Created, DateTimeOffset Modified);
