using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The operating system's light or dark setting as the headless platform reports it, which the app
/// follows while no theme is chosen (N.7).
/// </summary>
/// <remarks>
/// Raised the way a platform raises it, through the settings' own change notification, so the
/// application takes it up by the path a real desktop's setting takes. The session starts dark, as
/// the app's screens were drawn before the theme followed the system.
/// </remarks>
internal static class PlatformTheme
{
    internal static void Set(PlatformThemeVariant variant)
    {
        var settings = Application.Current?.PlatformSettings;
        Assert.True(settings is not null, "the application has no platform settings yet");

        var raise = settings.GetType().GetMethod(
            "OnColorValuesChanged",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(PlatformColorValues)]);

        Assert.True(raise is not null, "Avalonia's platform settings no longer raise OnColorValuesChanged; raise the change another way");
        raise.Invoke(settings, [new PlatformColorValues { ThemeVariant = variant }]);
    }
}
