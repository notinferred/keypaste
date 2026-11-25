using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The operating system's light or dark setting as the headless platform reports it, which the app
/// follows while no theme is chosen.
/// </summary>
/// <remarks>
/// Raised the way a platform raises it, through the settings' own change notification, so the
/// application takes it up by the path a real desktop's setting takes. The session starts dark, as
/// the app's screens were drawn before the theme followed the system.
/// </remarks>
internal static class PlatformTheme
{
    /// <param name="variant">Light or dark.</param>
    /// <param name="contrast">The contrast preference.</param>
    /// <param name="accent">The system's accent, or null for a platform that reports none.</param>
    internal static void Set(PlatformThemeVariant variant, ColorContrastPreference contrast = ColorContrastPreference.NoPreference, Color? accent = null)
    {
        var settings = Application.Current?.PlatformSettings;
        Assert.True(settings is not null, "the application has no platform settings yet");

        var raise = settings.GetType().GetMethod(
            "OnColorValuesChanged",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(PlatformColorValues)]);

        Assert.True(raise is not null, "Avalonia's platform settings no longer raise OnColorValuesChanged; raise the change another way");
        var values = new PlatformColorValues { ThemeVariant = variant, ContrastPreference = contrast };
        raise.Invoke(settings, [accent is { } reported ? values with { AccentColor1 = reported } : values]);
    }
}
