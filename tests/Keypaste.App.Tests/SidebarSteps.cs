using Keypaste.App.ViewModels;

namespace Keypaste.App.Tests;

/// <summary>What a person does in the sidebar before a test's step: open the vault's Environments or choose one of its places.</summary>
internal static class SidebarSteps
{
    /// <summary>One of the vault in view's places.</summary>
    internal static PlaceRow Place(this ShellViewModel shell, ItemPlace place) =>
        shell.SidebarRows.OfType<PlaceRow>().Single(row => row.Vault == shell.VaultInView && row.Place == place);

    /// <summary>Opens the vault's Environments, which lists its projects in the sidebar under it.</summary>
    internal static ShellViewModel ShowEnvironments(this ShellViewModel shell)
    {
        shell.SelectedSidebarRow = shell.Place(ItemPlace.Environments);
        return shell;
    }

    /// <summary>The project row named <paramref name="name"/>, Environments opened first.</summary>
    internal static ProjectRow ProjectRowNamed(this ShellViewModel shell, string name) =>
        shell.ShowEnvironments().SidebarRows.OfType<ProjectRow>().Single(row => row.Name == name);
}
