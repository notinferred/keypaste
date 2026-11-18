using Keypaste.App.Session;

namespace Keypaste.App.ViewModels;

/// <summary>One choice of a screen's vault picker: an open vault, or every one of them.</summary>
/// <param name="Title">What the picker says.</param>
/// <param name="Vault">The vault's session, or null for every unlocked vault.</param>
internal sealed record VaultChoice(string Title, AppVaultSession? Vault)
{
    public override string ToString() => Title;
}
