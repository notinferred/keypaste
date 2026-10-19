using Keypaste.Core.Internal;

namespace Keypaste.Core;

/// <summary>Everything an edit can change in one open vault, as it was before the edit, for <see cref="Vault.Restore"/>.</summary>
public sealed class VaultCheckpoint
{
    internal VaultCheckpoint(Vault vault, KeePassInterop.VaultSnapshot taken, bool wasPending, DateTimeOffset? savedAt)
    {
        Vault = vault;
        Taken = taken;
        WasPending = wasPending;
        SavedAt = savedAt;
    }

    /// <summary>The vault it was taken of.</summary>
    internal Vault Vault { get; }

    /// <summary>What it holds.</summary>
    internal KeePassInterop.VaultSnapshot Taken { get; }

    /// <summary>Whether the vault held a change its file did not when it was taken.</summary>
    internal bool WasPending { get; }

    /// <summary>When the vault was last saved when it was taken.</summary>
    internal DateTimeOffset? SavedAt { get; }
}
