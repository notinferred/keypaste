using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The sidebar's group tree answers the keyboard as KeePassXC's does: Right unfolds the chosen group,
/// Left folds it, and the Projects heading is a label the arrow keys and the pointer pass over (D-0376).
/// </summary>
public sealed class SidebarTreeKeyboardTests
{
    [Fact]
    public Task Right_unfolds_the_chosen_group_and_Left_folds_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tree-keys-");
        var nav = shell.Named<ListBox>("Nav");

        Choose(shell, nav, Row(shell.Shell, "env"));
        Assert.DoesNotContain(shell.Shell.SidebarRows, row => row is GroupRow { Path: "env/billing" });

        Press(shell, PhysicalKey.ArrowRight);
        Assert.True(Row(shell.Shell, "env").IsExpanded);
        Assert.Contains(shell.Shell.SidebarRows, row => row is GroupRow { Path: "env/billing" });

        Press(shell, PhysicalKey.ArrowLeft);
        Assert.False(Row(shell.Shell, "env").IsExpanded);
        Assert.DoesNotContain(shell.Shell.SidebarRows, row => row is GroupRow { Path: "env/billing" });
    });

    [Fact]
    public Task The_projects_heading_cannot_be_chosen() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tree-heading-");
        var nav = shell.Named<ListBox>("Nav");
        var heading = Assert.Single(shell.Shell.SidebarRows.OfType<SidebarHeading>());
        var container = Assert.IsType<ListBoxItem>(nav.ContainerFromItem(heading));

        Assert.False(container.IsEnabled);
        Assert.False(container.Focusable);
        Assert.Equal("Projects", heading.Title);
    });

    /// <summary>
    /// A right-click on a group's row opens its menu there, with Items showing that group, and a choice
    /// opens Items' form for it with the keyboard in it; a press elsewhere closes the menu.
    /// </summary>
    [Fact]
    public Task A_right_click_on_a_group_opens_its_menu_and_a_choice_opens_its_form() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tree-menu-");
        var nav = shell.Named<ListBox>("Nav");

        RightClick(shell, Assert.IsType<ListBoxItem>(nav.ContainerFromItem(Row(shell.Shell, "env"))));

        Assert.Equal("env", shell.Shell.GroupMenu?.Path);
        Assert.Equal("env", Assert.IsType<EntriesViewModel>(shell.Shell.Content).SelectedGroup?.Path);
        Assert.True(shell.Named<Border>("GroupMenuPanel").IsEffectivelyVisible);

        shell.Press(shell.Named<TextBox>("ShellSearch"));
        shell.Release(shell.Named<TextBox>("ShellSearch"));
        Assert.False(shell.Shell.HasGroupMenu);

        RightClick(shell, Assert.IsType<ListBoxItem>(nav.ContainerFromItem(Row(shell.Shell, "env"))));
        var rename = shell.Named<Button>("GroupMenuRename");
        shell.Press(rename);
        shell.Release(rename);
        RenderedShell.Drain();

        Assert.False(shell.Shell.HasGroupMenu);
        Assert.True(Assert.IsType<EntriesViewModel>(shell.Shell.Content).IsRenamingGroup);
        Assert.True(shell.Named<TextBox>("RenameGroupName").IsFocused);
    });

    private static void RightClick(RenderedShell shell, Control control)
    {
        shell.Frame();
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), shell.Window)!.Value;
        shell.Window.MouseDown(centre, MouseButton.Right);
        shell.Window.MouseUp(centre, MouseButton.Right);
        RenderedShell.Drain();
    }

    private static GroupRow Row(ShellViewModel shell, string path) =>
        shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == path);

    private static void Choose(RenderedShell shell, ListBox nav, GroupRow row)
    {
        shell.Shell.SelectedSidebarRow = row;
        RenderedShell.Drain();
        Assert.IsType<ListBoxItem>(nav.ContainerFromItem(shell.Shell.SelectedSidebarRow!)).Focus(NavigationMethod.Tab);
        RenderedShell.Drain();
    }

    private static void Press(RenderedShell shell, PhysicalKey key)
    {
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }
}
