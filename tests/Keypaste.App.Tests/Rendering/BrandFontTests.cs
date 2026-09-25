using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The embedded brand fonts are what the app draws with, and each weight the design uses is its
/// own face rather than a synthesized one.
/// </summary>
public sealed class BrandFontTests
{
    private const string Sample = "keypaste Secrets DATABASE_URL";

    [Theory]
    [InlineData(FontWeight.Normal)]
    [InlineData(FontWeight.Medium)]
    [InlineData(FontWeight.SemiBold)]
    public Task Each_interface_weight_resolves_to_its_own_Instrument_Sans_face(FontWeight weight) => HeadlessSession.On(() =>
    {
        var face = Face(Sans, weight);

        Assert.Equal("Instrument Sans", face.FamilyName);
        Assert.Equal(weight, face.Weight);
    });

    [Fact]
    public Task The_three_weights_draw_differently() => HeadlessSession.On(() =>
    {
        var widths = new[] { FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold }
            .Select(weight => Width(new Typeface(Sans, FontStyle.Normal, weight)))
            .ToArray();

        Assert.True(widths[0] < widths[1] && widths[1] < widths[2], $"widths {string.Join(", ", widths)}");
    });

    [Fact]
    public Task Machine_readable_text_is_Fragment_Mono() => HeadlessSession.On(() =>
    {
        var mono = (FontFamily)Application.Current!.FindResource("KpMono")!;

        Assert.Equal("Fragment Mono", Face(mono, FontWeight.Normal).FamilyName);
    });

    [Fact]
    public Task Text_in_a_window_is_Instrument_Sans_unless_it_says_otherwise() => HeadlessSession.On(() =>
    {
        var block = new TextBlock { Text = Sample };
        var window = new Window { Content = block };
        window.Show();

        Assert.Equal("Instrument Sans", Face(block.FontFamily, FontWeight.Normal).FamilyName);
        window.Close();
    });

    private static FontFamily Sans => (FontFamily)Application.Current!.FindResource("KpSans")!;

    private static GlyphTypeface Face(FontFamily family, FontWeight weight)
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var face));
        return face;
    }

    private static double Width(Typeface typeface) =>
        new FormattedText(Sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White).Width;
}
