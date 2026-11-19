using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The icon alone on the welcome screen, drawn in the palette's ink with its amber dot at the end; the unlocked app and the
/// lock screen show no mark (N.7, F.130).
/// </summary>
public sealed class BrandMarksTests
{
    public static TheoryData<string> Palettes => ["dark", "light"];

    [Theory]
    [MemberData(nameof(Palettes))]
    public Task The_unlocked_app_and_the_lock_screen_show_no_mark(string palette) => HeadlessSession.On(() =>
    {
        PlatformTheme.Set(palette == "light" ? PlatformThemeVariant.Light : PlatformThemeVariant.Dark);
        using var shell = new RenderedShell("keypaste-marks-");

        Assert.Empty(Visible<BrandWordmark>(shell.Window));
        Assert.Empty(Visible<BrandMark>(shell.Window));

        shell.PressLock();
        Assert.True(shell.IsLocked);

        Assert.Empty(Visible<BrandWordmark>(shell.Window));
        Assert.Empty(Visible<BrandMark>(shell.Window));
    });

    [Theory]
    [MemberData(nameof(Palettes))]
    public Task The_welcome_shows_the_icon(string palette) => HeadlessSession.On(() =>
    {
        PlatformTheme.Set(palette == "light" ? PlatformThemeVariant.Light : PlatformThemeVariant.Dark);
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });
        var window = new Window { Width = 960, Height = 720, Content = new UnlockView { DataContext = model } };
        window.Show();

        Assert.True(model.ShowsWelcome);
        var icon = Assert.Single(Visible<BrandMark>(window));
        Assert.Empty(Visible<BrandWordmark>(window));
        AssertDrawn(window, icon);
        window.Close();
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
        var (pixels, width, _) = AccentElements.Read(bitmap);
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
                else if (IsAmber(r, g, b))
                {
                    Assert.True(x >= dotFrom, $"{mark.GetType().Name} has amber at ({x},{y}), before its dot");
                    dotted++;
                }
            }
        }

        Assert.True(inked > 40, $"{mark.GetType().Name} drew {inked} pixels of its ink");
        Assert.True(dotted > 8, $"{mark.GetType().Name} drew {dotted} pixels of its dot");
    }

    /// <summary>The brand's amber and its anti-aliased edge: a hue of 30° to 48° well away from grey.</summary>
    private static bool IsAmber(byte r, byte g, byte b)
    {
        var (hue, chroma) = AccentElements.HueAndChroma(r, g, b);
        return chroma >= 48 && hue is >= 30 and <= 48;
    }
}
