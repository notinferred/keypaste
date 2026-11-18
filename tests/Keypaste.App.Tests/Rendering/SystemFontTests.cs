using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The app draws in the system's own faces: its interface face for words and its monospace for what a machine reads, none of them
/// embedded, and each weight the design uses draws as its own weight.
/// </summary>
public sealed class SystemFontTests
{
    private const string _sample = "keypaste Secrets DATABASE_URL";

    [Fact]
    public Task Neither_face_is_embedded() => HeadlessSession.On(() =>
    {
        Assert.DoesNotContain("avares:", Sans.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("avares:", Mono.ToString(), StringComparison.Ordinal);
    });

    [Fact]
    public Task Bold_draws_as_its_own_weight() => HeadlessSession.On(() =>
    {
        var weights = new[] { FontWeight.Normal, FontWeight.Bold };
        var widths = weights.Select(weight => Width(_sample, new Typeface(Sans, FontStyle.Normal, weight))).ToArray();
        var faces = weights.Select(weight => FontManager.Current.TryGetGlyphTypeface(new Typeface(Sans, FontStyle.Normal, weight), out var face) ? $"{face.FamilyName} {face.Weight}" : "none");

        Assert.True(widths[0] < widths[1], $"widths {string.Join(", ", widths)} from {string.Join(", ", faces)}");
    });

    [Fact]
    public Task Machine_readable_text_is_monospaced_and_words_are_not() => HeadlessSession.On(() =>
    {
        Assert.Equal(Width("iiii", new Typeface(Mono)), Width("WWWW", new Typeface(Mono)), 1);
        Assert.True(Width("iiii", new Typeface(Sans)) < Width("WWWW", new Typeface(Sans)) / 2);
    });

    [Fact]
    public Task Text_in_a_window_is_the_interface_face_unless_it_says_otherwise() => HeadlessSession.On(() =>
    {
        var block = new TextBlock { Text = _sample };
        var window = new Window { Content = block };
        window.Show();

        Assert.Equal(Sans, block.FontFamily);
        window.Close();
    });

    private static FontFamily Sans => (FontFamily)Application.Current!.FindResource("KpSans")!;

    private static FontFamily Mono => (FontFamily)Application.Current!.FindResource("KpMono")!;

    private static double Width(string text, Typeface typeface) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, Brushes.White).Width;
}
