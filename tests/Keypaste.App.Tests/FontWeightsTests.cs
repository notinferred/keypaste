using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Keypaste.App.Theme;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// Pinning the app's weights copies no font file, since a synthesized face's copy crashes the process when a collection runs during it.
/// </summary>
public sealed class FontWeightsTests
{
    [Fact]
    public Task Every_pinned_weight_resolves_to_a_face_the_system_has() =>
        HeadlessSession.On(() =>
        {
            // The app pinned its own font manager while starting, so the order is checked against one that has resolved nothing yet.
            var platform = typeof(FontManager)
                .GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(FontManager.Current) as IFontManagerImpl;
            Assert.True(platform is not null, "Avalonia's font manager no longer exposes PlatformImpl; build a fresh font manager another way");
            using var fonts = new FontManager(platform);

            var resolved = FontWeights.Pin(Application.Current!, fonts);

            Assert.NotEmpty(resolved);
            Assert.All(resolved, face => Assert.Equal(FontSimulations.None, face.FontSimulations));
        });
}
