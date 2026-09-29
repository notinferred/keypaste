using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// A status is often read as text, "Not opened yet" or "missing", so each status colour keeps WCAG's
/// 4.5:1 for body text against every surface it sits on, in both palettes (N.7).
/// </summary>
public sealed class StatusColourContrastTests
{
    private const double _bodyText = 4.5;

    public static TheoryData<string, string, string> Pairs()
    {
        var pairs = new TheoryData<string, string, string>();

        foreach (var palette in new[] { "Dark", "Light" })
        {
            foreach (var status in new[] { "KpOk", "KpDanger", "KpInfo" })
            {
                foreach (var ground in new[] { "KpBgApp", "KpBgPanel", "KpBgCard", "KpBgHover" })
                {
                    pairs.Add(palette, status, ground);
                }
            }
        }

        return pairs;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public Task A_status_reads_on_every_surface(string palette, string status, string ground) => HeadlessSession.On(() =>
    {
        var variant = palette == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var ratio = Contrast(Colour(status, variant), Colour(ground, variant));

        Assert.True(ratio >= _bodyText, $"{status} on {ground} in the {palette} palette is {ratio:F2}:1, under {_bodyText}:1");
    });

    private static Color Colour(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), $"{key} is not a {variant} resource");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static double Contrast(Color a, Color b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));
    }
}
