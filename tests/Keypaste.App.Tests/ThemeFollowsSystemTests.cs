using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// V-N.7: with no <c>app.toml</c> the app paints in the system's light or dark setting, as it
/// changes, and a choice in Settings wins over either.
/// </summary>
/// <remarks>Read from the pixels of the app's own window, composed by the method launch runs.</remarks>
public sealed class ThemeFollowsSystemTests
{
    private static readonly (byte R, byte G, byte B) _dark = (0x11, 0x12, 0x14);
    private static readonly (byte R, byte G, byte B) _light = (0xF7, 0xF7, 0xF5);

    [Fact]
    public Task With_no_settings_file_the_app_follows_the_system_until_Settings_chooses() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        Assert.False(File.Exists(KeypasteHome.SettingsPath(fixture.Home)));

        var app = Assert.IsType<App>(Application.Current);
        var preferences = new DesktopPreferences(fixture.Home);
        var window = new MainWindow { Width = 960, Height = 520 };

        try
        {
            using var session = app.Compose(preferences, new ManualClock(AppClock.Start));
            window.Show();

            // The session starts on a dark system (PlatformTheme); the first frame is drawn before the switch.
            Assert.Equal(_dark, Background(window));

            PlatformTheme.Set(PlatformThemeVariant.Light);
            Assert.Equal(_light, Background(window));

            PlatformTheme.Set(PlatformThemeVariant.Dark);
            Assert.Equal(_dark, Background(window));

            using var settings = new SettingsViewModel(session, fixture.Home, preferences, app.ApplyTheme);

            settings.Theme = AppTheme.Light;
            Assert.Equal(_light, Background(window));

            settings.Theme = AppTheme.Dark;
            PlatformTheme.Set(PlatformThemeVariant.Light);
            Assert.Equal(_dark, Background(window));

            settings.Theme = AppTheme.System;
            Assert.Equal(_light, Background(window));
        }
        finally
        {
            window.Close();
            PlatformTheme.Set(PlatformThemeVariant.Dark);
            app.ApplyTheme(AppTheme.System);
        }
    });

    /// <summary>The colour the window paints at its centre, which nothing covers while it shows no screen.</summary>
    private static (byte R, byte G, byte B) Background(MainWindow window)
    {
        WindowInput.Drain();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        WindowInput.Drain();

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var (pixels, width, height) = AmberElements.Read(frame);
        var centre = (((height / 2) * width) + (width / 2)) * 4;
        return (pixels[centre + 2], pixels[centre + 1], pixels[centre]);
    }
}
