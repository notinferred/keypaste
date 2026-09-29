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
