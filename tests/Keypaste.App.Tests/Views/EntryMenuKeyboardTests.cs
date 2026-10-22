using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The entry's ⋯ menu behaves as a popover: it opens with focus on its first item and Escape closes it.</summary>
public sealed class EntryMenuKeyboardTests
{
    [Fact]
    public Task The_menu_takes_focus_when_opened_and_Escape_returns_it_to_its_button() =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-entry-menu-");
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();

            var toggle = shell.Named<ToggleButton>("EntryMenu");
            shell.Press(toggle);
            shell.Release(toggle);
            RenderedShell.Drain();

            Assert.True(toggle.IsChecked);
            var focused = Assert.IsAssignableFrom<Button>(shell.Window.FocusManager?.GetFocusedElement());
            Assert.Contains("menu-item", focused.Classes);

            shell.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            shell.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            RenderedShell.Drain();

            Assert.False(toggle.IsChecked);
            Assert.True(toggle.IsFocused);
        });

    /// <summary>
    /// A sidebar group's menu behaves the same way from the keyboard: Shift+F10 on the chosen group opens it
    /// with focus on its first item, Escape closes it and gives the keyboard back to the group's row, and
    /// Enter on an item opens that form with the keyboard in it.
    /// </summary>
    [Fact]
    public Task The_group_menu_opens_from_the_keyboard_and_Escape_returns_to_the_row() =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-group-menu-");
            var nav = shell.Named<ListBox>("Nav");
            var env = shell.Shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "env");
            shell.Shell.SelectedSidebarRow = env;
            RenderedShell.Drain();
            var container = Assert.IsType<ListBoxItem>(nav.ContainerFromItem(env));
            container.Focus(NavigationMethod.Tab);
            RenderedShell.Drain();

            OpenFromTheKeyboard(shell);

            Assert.Equal("env", shell.Shell.GroupMenu?.Path);
            var focused = Assert.IsAssignableFrom<Button>(shell.Window.FocusManager?.GetFocusedElement());
            Assert.Equal("GroupMenuNewGroup", focused.Name);

            shell.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            shell.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            RenderedShell.Drain();

            Assert.False(shell.Shell.HasGroupMenu);
            Assert.True(Assert.IsType<ListBoxItem>(nav.ContainerFromItem(shell.Shell.SelectedSidebarRow!)).IsFocused);

            OpenFromTheKeyboard(shell);
            shell.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            shell.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            RenderedShell.Drain();

            Assert.False(shell.Shell.HasGroupMenu);
            Assert.True(Assert.IsType<EntriesViewModel>(shell.Shell.Content).IsCreatingGroup);
            Assert.True(shell.Named<TextBox>("NewGroupName").IsFocused);
        });

    private static void OpenFromTheKeyboard(RenderedShell shell)
    {
        shell.Window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
        shell.Window.KeyReleaseQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
        RenderedShell.Drain();
    }
}
