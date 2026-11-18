using Keypaste.App.ViewModels;

namespace Keypaste.App.Tests;

/// <summary>What a person does in the sidebar before a test's step: unfold the vault's Groups, open its Environments or choose one of its places.</summary>
internal static class SidebarSteps
{
    /// <summary>Unfolds the Groups row of the vault in view, so its group tree is in the sidebar.</summary>
    internal static ShellViewModel ShowGroups(this ShellViewModel shell)
    {
        if (shell.SidebarRows.OfType<GroupsRow>().First(row => row.Vault == shell.VaultInView) is { IsExpanded: false } groups)
        {
            shell.Fold(groups, open: true);
        }

        return shell;
    }

    /// <summary>The group row at <paramref name="path"/> of the vault in view, its Groups unfolded first.</summary>
    internal static GroupRow GroupRowAt(this ShellViewModel shell, string path) =>
        shell.ShowGroups().SidebarRows.OfType<GroupRow>().Single(row => row.Path == path);

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
