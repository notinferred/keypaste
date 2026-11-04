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
}
