using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The entry's ⋯ menu is a menu as every menu is: a press opens it, and Escape closes it and gives the keyboard back to its button.</summary>
public sealed class EntryMenuKeyboardTests
{
    [Fact]
    public Task A_press_opens_the_menu_and_Escape_returns_the_keyboard_to_its_button() =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-entry-menu-");
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();

            var button = shell.Named<Button>("EntryMenu");
            button.Focus(NavigationMethod.Tab);
            shell.Tap(button);
            var menu = (MenuFlyout)button.Flyout!;

            Assert.True(menu.IsOpen);

            shell.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            shell.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            RenderedShell.Drain();

            Assert.False(menu.IsOpen);
            Assert.True(button.IsFocused);
        });
}
