using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The wordmark alone at the top of the sidebar and the icon alone on the lock screen, never both
/// (BRAND), each drawn in the palette's ink with its amber dot at the end (N.7).
/// </summary>
public sealed class BrandMarksTests
{
    public static TheoryData<string> Palettes => ["dark", "light"];

    [Theory]
    [MemberData(nameof(Palettes))]
    public Task The_sidebar_shows_the_wordmark_and_the_lock_screen_the_icon(string palette) => HeadlessSession.On(() =>
    {
        PlatformTheme.Set(palette == "light" ? PlatformThemeVariant.Light : PlatformThemeVariant.Dark);
        using var shell = new RenderedShell("keypaste-marks-");

        var wordmark = Assert.Single(Visible<BrandWordmark>(shell.Window));
        Assert.Empty(Visible<BrandMark>(shell.Window));
        AssertDrawn(shell.Window, wordmark);

        shell.PressLock();
        Assert.True(shell.IsLocked);

        var icon = Assert.Single(Visible<BrandMark>(shell.Window));
        Assert.Empty(Visible<BrandWordmark>(shell.Window));
        AssertDrawn(shell.Window, icon);
    });

    private static IEnumerable<T> Visible<T>(TopLevel window)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);

    /// <summary>The mark's bounds hold its ink and, in their last fifth, its amber dot.</summary>
    private static void AssertDrawn(TopLevel window, BrandOutline mark)
    {
        RenderedShell.Drain();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);

        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        var (pixels, width, _) = AmberElements.Read(bitmap);
        var ink = Assert.IsAssignableFrom<ISolidColorBrush>(mark.InkBrush).Color;

        var origin = mark.TranslatePoint(default, window) ?? throw new InvalidOperationException("the mark is not in the window");
        var box = PixelRect.FromRect(new Rect(origin, mark.Bounds.Size), window.RenderScaling);
        int inked = 0, dotted = 0, dotFrom = box.X + (box.Width * 4 / 5);

        for (var y = box.Y; y < box.Bottom; y++)
        {
            for (var x = box.X; x < box.Right; x++)
            {
                var i = ((y * width) + x) * 4;
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                if (Math.Abs(r - ink.R) + Math.Abs(g - ink.G) + Math.Abs(b - ink.B) < 24)
                {
                    inked++;
                }
                else if (AmberElements.IsAmber(r, g, b))
                {
                    Assert.True(x >= dotFrom, $"{mark.GetType().Name} has amber at ({x},{y}), before its dot");
                    dotted++;
                }
            }
        }

        Assert.True(inked > 40, $"{mark.GetType().Name} drew {inked} pixels of its ink");
        Assert.True(dotted > 8, $"{mark.GetType().Name} drew {dotted} pixels of its dot");
    }
}
