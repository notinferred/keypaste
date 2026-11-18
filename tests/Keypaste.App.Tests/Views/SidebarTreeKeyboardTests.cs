using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The sidebar answers the keyboard: Right unfolds the chosen vault, Left folds it, and the account's heading is a label
/// the arrow keys and the pointer pass over.
/// </summary>
public sealed class SidebarTreeKeyboardTests
{
    [Fact]
    public Task Left_folds_the_chosen_vaults_places() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tree-vault-keys-", secondVault: "acme");
        var nav = shell.Named<ListBox>("Nav");
        var acme = shell.Shell.SidebarRows.OfType<VaultRow>().Single(row => row.Title == "acme");
        shell.Shell.SelectedSidebarRow = acme;
        RenderedShell.Drain();
        Assert.IsType<ListBoxItem>(nav.ContainerFromItem(shell.Shell.SelectedSidebarRow!)).Focus(NavigationMethod.Tab);
        RenderedShell.Drain();

        Press(shell, PhysicalKey.ArrowLeft);
        Assert.DoesNotContain(shell.Shell.SidebarRows, row => row is PlaceRow { Vault: var vault } && vault == acme.Vault);

        Press(shell, PhysicalKey.ArrowRight);
        Assert.Contains(shell.Shell.SidebarRows, row => row is PlaceRow { Place: ItemPlace.All, Vault: var vault } && vault == acme.Vault);
    });

    [Fact]
    public Task The_accounts_heading_cannot_be_chosen_and_the_keyboard_passes_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tree-heading-", secondVault: "acme");
        var nav = shell.Named<ListBox>("Nav");
        var heading = shell.Shell.SidebarRows.OfType<AccountRow>().Single();

        Assert.Equal(AccountSection.LocalTitle, heading.Title);
        Assert.False(Assert.IsType<ListBoxItem>(nav.ContainerFromItem(heading)).Focusable);
    });

    [Fact]
    public Task One_vault_shows_its_places_with_no_heading_to_pass() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tree-one-vault-");
        var nav = shell.Named<ListBox>("Nav");

        Assert.DoesNotContain(shell.Shell.SidebarRows, row => row is AccountRow or VaultRow);
        Assert.DoesNotContain("accounts", nav.Classes);
        Assert.Equal(IconX(shell, nav, ItemPlace.Favorites), IconX(shell, nav, ItemPlace.All));
    });

    private static double IconX(RenderedShell shell, ListBox nav, ItemPlace place)
    {
        var item = Assert.IsType<ListBoxItem>(nav.ContainerFromItem(shell.Shell.SidebarRows.OfType<PlaceRow>().Single(row => row.Place == place)));
        return item.GetVisualDescendants().OfType<KpIcon>().First().TranslatePoint(default, shell.Window)!.Value.X;
    }

    private static void Press(RenderedShell shell, PhysicalKey key)
    {
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }
}
