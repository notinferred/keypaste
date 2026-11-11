using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The sidebar adds what belongs to the navigation: a + on the Groups and Projects headings and an Import row at its foot, each the menus' own command; Share is the open item's.</summary>
public sealed class SidebarAddTests
{
    [Fact]
    public Task The_groups_heading_adds_a_group_in_the_one_chosen() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-group-", vault => vault.AddEntry(new VaultEntry { GroupPath = "Work", Title = "wiki", Password = "w" }));
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "Work");
        RenderedShell.Drain();

        var add = HeadingAdd(shell, SidebarAddition.Group);
        Assert.Equal(Strings.NewGroupLabel, ToolTip.GetTip(add));
        Assert.Equal(Strings.NewGroupLabel, Avalonia.Automation.AutomationProperties.GetName(add));
        shell.Tap(add);
        RenderedShell.Drain();

        var items = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        Assert.True(items.IsCreatingGroup);
        items.DraftGroupName = "Servers";
        items.ConfirmCreateGroupCommand.Execute(null);

        Assert.Contains("Work/Servers", shell.Session.Unlocked!.ReadGroupPaths());
    });

    [Fact]
    public Task The_projects_heading_adds_a_project() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-project-");

        shell.Tap(HeadingAdd(shell, SidebarAddition.Project));
        RenderedShell.Drain();

        Assert.True(Assert.IsType<EnvSetsViewModel>(shell.Shell.Content).IsAdding);
    });

    [Fact]
    public Task A_press_on_a_heading_beside_its_plus_chooses_nothing() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-heading-press-");
        var chosen = shell.Shell.SelectedSidebarRow;
        var heading = shell.Window.GetVisualDescendants().OfType<ListBoxItem>().First(item => item.DataContext is SidebarHeading);

        shell.Tap(heading.GetVisualDescendants().OfType<TextBlock>().First());
        RenderedShell.Drain();

        Assert.Equal(chosen, shell.Shell.SelectedSidebarRow);
        Assert.False(heading.Focusable);
    });

    [Fact]
    public Task Import_sits_above_trash_and_settings_with_the_file_menus_two_imports() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-import-");
        var row = shell.Named<Button>("SidebarImport");
        var footer = shell.Named<ListBox>("FooterNav");
        Assert.True(row.IsEffectivelyVisible);
        Assert.True(row.TranslatePoint(default, shell.Window)!.Value.Y < footer.TranslatePoint(default, shell.Window)!.Value.Y);

        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RenderedShell.Drain();
        var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(row)!;

        Assert.Equal([Strings.ImportFromAnotherAppLabel, Strings.ImportEnvLabel], menu.Items.OfType<MenuItem>().Select(item => (string)item.Header!));
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries.OfType<AppCommand>();
        Assert.All(shell.Commands.SidebarImport.OfType<AppCommand>(), command => Assert.Contains(command, file));
        Assert.Contains(shell.Commands.NewGroup, file);
        Assert.Contains(shell.Commands.NewProject, file);
        menu.Hide();
    });

    [Fact]
    public Task Share_is_in_the_open_items_header_and_not_the_sidebar() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-header-share-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var sidebar = shell.Named<Border>("Sidebar");
        Assert.DoesNotContain(
            sidebar.GetVisualDescendants().OfType<Control>(),
            control => Equals(ToolTip.GetTip(control), Strings.Share) || control is MenuItem { Header: var header } && Equals(header, Strings.Share));

        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();

        var share = shell.Named<Button>("ShareItem");
        var edit = shell.Named<Button>("EditItem");
        var more = shell.Named<ToggleButton>("EntryMenu");
        Assert.True(share.IsEffectivelyVisible);
        Assert.Equal(Strings.Share, Avalonia.Automation.AutomationProperties.GetName(share));
        Assert.Equal(Strings.Share, ToolTip.GetTip(share));
        var x = share.TranslatePoint(default, shell.Window)!.Value.X;
        Assert.True(edit.TranslatePoint(default, shell.Window)!.Value.X < x && x < more.TranslatePoint(default, shell.Window)!.Value.X);

        shell.Tap(share);
        RenderedShell.Drain();

        Assert.True(shell.Shell.HasShare);
    });

    private static Button HeadingAdd(RenderedShell shell, SidebarAddition adds) =>
        shell.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "HeadingAdd" && button.DataContext is SidebarHeading heading && heading.Adds == adds);
}
