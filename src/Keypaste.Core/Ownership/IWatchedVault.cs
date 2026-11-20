namespace Keypaste.Core.Ownership;

/// <summary>An approver's open vault, as <see cref="VaultFileWatch"/> watches its file for another program's save.</summary>
public interface IWatchedVault
{
    /// <summary>The clock the poll and the settling run on.</summary>
    TimeProvider Clock { get; }

    /// <summary>The open copy, or null while the vault is locked.</summary>
    Vault? Open { get; }

    /// <summary>
    /// Whether another program's save of the open copy's file is merged now: false while nothing merges for this unlock, or while
    /// the file still holds the bytes the last merge refused, which are not tried again until the file changes.
    /// </summary>
    bool MergesAnotherSave { get; }

    /// <summary>Merges another program's save with the open copy, blocking for its key derivation and a hardware key's touch.</summary>
    void MergeAnotherSave();

    /// <summary>Raised once a vault is open, so its folder is watched.</summary>
    event EventHandler? Opened;

    /// <summary>Raised once the open vault is locked, so its folder is no longer watched.</summary>
    event EventHandler? Locked;
}
