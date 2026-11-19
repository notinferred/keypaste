namespace Keypaste.App.ViewModels;

/// <summary>
/// A locked vault's place while another vault stays open: its unlock form in the list's room, asking only for that
/// vault, or the create form of a vault just added to the account.
/// </summary>
/// <remarks>Nothing of the vault is read until it unlocks; the form's password is dropped with it.</remarks>
internal sealed class LockedVaultViewModel : IDisposable
{
    /// <param name="vault">The vault's section.</param>
    /// <param name="unlock">The form, on the vault's own session, which this now owns.</param>
    internal LockedVaultViewModel(VaultSection vault, UnlockViewModel unlock)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(unlock);

        Vault = vault;
        Unlock = unlock;
    }

    internal VaultSection Vault { get; }

    internal UnlockViewModel Unlock { get; }

    public void Dispose() => Unlock.Dispose();
}
