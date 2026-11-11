using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The sidebar's add makes what belongs to the navigation, through the commands the menus already run, and offers nothing that is not built.</summary>
public sealed class SidebarAddMenuTests
{
    [Fact]
    public Task The_sidebar_add_offers_a_group_a_project_the_imports_and_share() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-");
        var menu = Open(shell);

        Assert.Equal(
            [Strings.NewGroupLabel, Strings.NewProjectLabel, "-", Strings.ImportFromAnotherAppLabel, Strings.ImportEnvLabel, "-", Strings.Share],
            menu.Items.Select(item => item is MenuItem entry ? (string)entry.Header! : "-"));

        // Each entry is the menu bar's own command, so a chord and the File menu reach the same act.
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries.OfType<AppCommand>();
        Assert.All(shell.Commands.SidebarAdd.OfType<AppCommand>().Take(4), command => Assert.Contains(command, file));
        menu.Hide();
    });

    [Fact]
    public Task New_group_goes_in_the_group_chosen_in_the_sidebar() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-group-", vault => vault.AddEntry(new VaultEntry { GroupPath = "Work", Title = "wiki", Password = "w" }));
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "Work");
        RenderedShell.Drain();

        var menu = Open(shell);
        Press(menu, Strings.NewGroupLabel);

        var items = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        Assert.True(items.IsCreatingGroup);
        items.DraftGroupName = "Servers";
        items.ConfirmCreateGroupCommand.Execute(null);

        Assert.Contains("Work/Servers", shell.Session.Unlocked!.ReadGroupPaths());
    });

    [Fact]
    public Task Share_is_on_only_while_an_item_is_open() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-share-");
        var share = shell.Commands.SidebarAdd.OfType<AppCommand>().Single(command => command.Header == Strings.Share);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        Assert.False(share.CanRun());

        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();

        Assert.True(share.CanRun());
    });

    private static MenuFlyout Open(RenderedShell shell)
    {
        var button = shell.Named<Button>("SidebarAdd");
        Assert.True(button.IsEffectivelyVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RenderedShell.Drain();
        return (MenuFlyout)FlyoutBase.GetAttachedFlyout(button)!;
    }

    private static void Press(MenuFlyout menu, string header)
    {
        var item = menu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, header));
        Assert.True(item.Command!.CanExecute(null));
        menu.Hide();
        item.Command.Execute(null);
        RenderedShell.Drain();
    }
}
