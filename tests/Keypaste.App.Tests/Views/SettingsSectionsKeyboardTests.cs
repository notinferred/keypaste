using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Settings' sections, those drawn by a screen of their own too, are reached from one list with the keyboard alone.</summary>
public sealed class SettingsSectionsKeyboardTests
{
    [Fact]
    public Task A_section_with_a_screen_of_its_own_opens_and_leaves_from_the_section_list() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-sections-keys-");
        shell.Show<SettingsViewModel>(DestinationKind.Settings);

        Press(shell, PhysicalKey.End);
        Assert.IsType<DiagnosticsViewModel>(shell.Shell.Content);
        Assert.False(shell.Shell.HasBack);

        Press(shell, PhysicalKey.Home);
        Assert.Equal(SettingsSectionChoice.General, Assert.IsType<SettingsViewModel>(shell.Shell.Content).Section);
    });

    /// <summary>Puts the keyboard in the section list in view and presses a key there.</summary>
    private static void Press(RenderedShell shell, PhysicalKey key)
    {
        var list = shell.Window.GetVisualDescendants().OfType<ListBox>().Single(box => box is { Name: "SettingsSections", IsEffectivelyVisible: true });
        var row = (ListBoxItem)list.ContainerFromItem(list.SelectedItem!)!;
        Assert.True(row.Focus(NavigationMethod.Tab), "the section list took no focus");
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }
}
