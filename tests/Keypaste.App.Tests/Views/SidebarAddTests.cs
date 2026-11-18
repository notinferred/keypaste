using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The sidebar adds what belongs to the navigation: New at its top, a + on a vault's Groups row and Add vault… at its foot, each the menus' own command; Share is the open item's.</summary>
public sealed class SidebarAddTests
{
    [Fact]
    public Task The_groups_heading_adds_a_group_in_the_one_chosen() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-group-", vault => vault.AddEntry(new VaultEntry { GroupPath = "Work", Title = "wiki", Password = "w" }));
        shell.Shell.SelectedSidebarRow = shell.Shell.GroupRowAt("Work");
        RenderedShell.Drain();

        var add = HeadingAdd(shell);
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
    public Task New_at_the_sidebars_top_opens_new_items_templates_from_any_place() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-new-");
        shell.Show<TrashViewModel>(DestinationKind.Trash);
        var add = shell.Named<Button>("SidebarNew");
        Assert.Equal(Strings.New, Avalonia.Automation.AutomationProperties.GetName(add));

        shell.Tap(add);
        RenderedShell.Drain();

        Assert.True(Assert.IsType<EntriesViewModel>(shell.Shell.Content).NewItem?.IsChoosingTemplate);
    });

    [Fact]
    public Task A_press_on_the_accounts_heading_chooses_nothing() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-heading-press-", secondVault: "acme");
        var chosen = shell.Shell.SelectedSidebarRow;
        var heading = shell.Window.GetVisualDescendants().OfType<ListBoxItem>().First(item => item.DataContext is AccountRow);

        shell.Tap(heading.GetVisualDescendants().OfType<TextBlock>().First());
        RenderedShell.Drain();

        Assert.Equal(chosen, shell.Shell.SelectedSidebarRow);
        Assert.False(heading.Focusable);
    });

    [Fact]
    public Task Add_vault_at_the_sidebars_foot_opens_or_creates_a_vault_as_the_file_menu_adds_one() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-add-vault-");
        var add = shell.Named<Button>("AddVault");
        Assert.Equal(Strings.AddVaultLabel, Avalonia.Automation.AutomationProperties.GetName(add));
        Assert.True(add.IsEffectivelyVisible);

        shell.Tap(add);
        RenderedShell.Drain();

        var menu = Assert.IsType<MenuFlyout>(add.Flyout);
        Assert.Equal([Strings.OpenAVault, Strings.CreateAVault], menu.Items.OfType<MenuItem>().Select(item => item.Header));
        Assert.Same(shell.Shell.OpenVaultCommand, menu.Items.OfType<MenuItem>().First().Command);
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries.OfType<AppCommand>();
        Assert.Contains(file, command => command.Header == Strings.AddVaultLabel);
    });

    [Fact]
    public Task The_file_menu_holds_new_group_new_project_and_both_imports() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sidebar-import-");
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries.OfType<AppCommand>().ToList();

        Assert.Contains(file, command => command.Header == Strings.ImportFromAnotherAppLabel);
        Assert.Contains(file, command => command.Header == Strings.ImportEnvLabel);
        Assert.Contains(shell.Commands.NewGroup, file);
        Assert.Contains(shell.Commands.NewProject, file);
    });

    [Fact]
    public Task Share_is_under_the_open_items_more_actions_and_not_the_sidebar() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-header-share-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var sidebar = shell.Named<Border>("Sidebar");
        Assert.DoesNotContain(
            sidebar.GetVisualDescendants().OfType<Control>(),
            control => Equals(ToolTip.GetTip(control), Strings.Share) || control is MenuItem { Header: var header } && Equals(header, Strings.Share));

        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();

        var copy = shell.Named<Button>("CopyPassword");
        var edit = shell.Named<Button>("EditItem");
        var more = shell.Named<ToggleButton>("EntryMenu");
        Assert.True(copy.TranslatePoint(default, shell.Window)!.Value.X < edit.TranslatePoint(default, shell.Window)!.Value.X);
        Assert.True(edit.TranslatePoint(default, shell.Window)!.Value.X < more.TranslatePoint(default, shell.Window)!.Value.X);

        shell.Tap(more);
        RenderedShell.Drain();
        var share = shell.Named<Button>("ShareEntry");
        Assert.True(share.IsEffectivelyVisible);
        Assert.Equal(Strings.Share, Avalonia.Automation.AutomationProperties.GetName(share));
        shell.Tap(share);
        RenderedShell.Drain();

        Assert.True(shell.Shell.HasShare);
    });

    private static Button HeadingAdd(RenderedShell shell) =>
        shell.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "HeadingAdd" && button.DataContext is GroupsRow);
}
