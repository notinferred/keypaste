using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Settings › Advanced and the screens under it are reached, and left by Back, with the keyboard alone.</summary>
public sealed class SettingsAdvancedKeyboardTests
{
    [Fact]
    public Task Advanced_and_a_screen_under_it_are_opened_and_left_from_the_keyboard() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-advanced-keys-");
        shell.Show<SettingsViewModel>(DestinationKind.Settings);

        Enter(shell, "OpenAdvanced");
        Assert.IsType<AdvancedViewModel>(shell.Shell.Content);

        Enter(shell, "OpenDiagnostics");
        Assert.IsType<DiagnosticsViewModel>(shell.Shell.Content);

        Enter(shell, "Back");
        Assert.IsType<AdvancedViewModel>(shell.Shell.Content);

        Enter(shell, "Back");
        Assert.IsType<SettingsViewModel>(shell.Shell.Content);
        Assert.False(shell.Shell.HasBack);
    });

    /// <summary>Focuses a button as Tab would and presses Enter on it.</summary>
    private static void Enter(RenderedShell shell, string name)
    {
        var button = shell.Named<Button>(name);
        Assert.True(button is { IsEffectivelyVisible: true, IsTabStop: true, Focusable: true }, $"{name} is not a visible tab stop");
        Assert.True(button.Focus(NavigationMethod.Tab), $"{name} took no focus");
        shell.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        RenderedShell.Drain();
    }
}
