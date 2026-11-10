using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// Enabled text keeps 4.5:1, and focus rings, control edges and status marks 3:1, on the grounds they sit on, in each palette (DESIGN rule 7, Q.8).
/// </summary>
public sealed class TokenContrastTests
{
    private const double _text = 4.5;
    private const double _edge = 3;
    private const double _visibleChange = 1.3;

    private static readonly string[] _everyGround = ["KpBgApp", "KpBgPanel", "KpBgCard", "KpBgHover", "KpBgActive", "KpBgActiveHover"];
    private static readonly string[] _raisedAtMost = ["KpBgApp", "KpBgPanel", "KpBgCard", "KpBgHover"];
    private static readonly string[] _unraised = ["KpBgApp", "KpBgPanel", "KpBgCard"];
    private static readonly string[] _terminal = ["KpTerminal"];

    /// <summary>The Fluent theme's copies of the control edge, at rest and under the pointer, which stock fields and choices draw.</summary>
    private static readonly (string AtRest, string PointerOver)[] _controlEdges =
    [
        ("TextControlBorderBrush", "TextControlBorderBrushPointerOver"),
        ("ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver"),
        ("CalendarDatePickerBorderBrush", "CalendarDatePickerBorderBrushPointerOver"),
        ("CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver"),
        ("RadioButtonOuterEllipseStroke", "RadioButtonOuterEllipseStrokePointerOver"),
        ("ToggleSwitchStrokeOff", "ToggleSwitchStrokeOffPointerOver"),
    ];

    /// <summary>Token, its grounds and its minimum. Muted text stays off raised grounds: there a style takes the secondary colour.</summary>
    private static readonly (string Token, string[] Grounds, double Minimum)[] _pairs =
    [
        ("KpTextPrimary", _everyGround, _text),
        ("KpTextSecondary", _everyGround, _text),
        ("KpTextMuted", _unraised, _text),
        ("KpAccentText", _raisedAtMost, _text),
        ("KpOk", _raisedAtMost, _text),
        ("KpDanger", _raisedAtMost, _text),
        ("KpInfo", _raisedAtMost, _text),
        ("KpOnAccent", ["KpAccent", "KpAccentHover", "KpAccentPress"], _text),
        ("KpAccentContrast", ["KpAccent"], _text),
        ("KpGrey3", _terminal, _text),
        ("KpGrey5", _terminal, _text),
        ("KpPaper", _terminal, _text),
        ("KpAmber", _terminal, _text),

        ("KpBorderControl", _raisedAtMost, _edge),
        ("KpFocusRing", _raisedAtMost, _edge),
        ("KpWarn", _raisedAtMost, _edge),
        ("KpIdle", _raisedAtMost, _edge),

        ("TextControlBorderBrushFocused", _raisedAtMost, _edge),
        ("SystemControlFocusVisualPrimaryBrush", _raisedAtMost, _edge),
        ("TextControlPlaceholderForeground", _unraised, _text),
        .. _controlEdges.Select(edge => (edge.AtRest, _raisedAtMost, _edge)),
        .. _controlEdges.Select(edge => (edge.PointerOver, _raisedAtMost, _edge)),
    ];

    /// <summary>Decorative lines around cards, tables, dividers, chips and read-only values, which WCAG 1.4.11 exempts and the Delight principle keeps quiet.</summary>
    private static readonly (string Token, string[] Grounds)[] _decorative =
    [
        ("KpBorderSubtle", _raisedAtMost),
        ("KpBorderDefault", _raisedAtMost),
        ("KpBorderStrong", _raisedAtMost),
        ("KpLine1", _terminal),
        ("KpLine2", _terminal),
        ("KpLine3", _terminal),
        ("KpTerminalDivider", _terminal),
    ];

    /// <summary>Disabled text, which WCAG 1.4.3 exempts.</summary>
    private static readonly (string Token, string[] Grounds)[] _disabled =
    [
        ("KpTextDisabled", _unraised),
        ("KpGrey7", _terminal),
        ("TextControlForegroundDisabled", _unraised),
        ("ButtonForegroundDisabled", _unraised),
    ];

    /// <summary>Surfaces, tints and marks, which nothing is measured against this table for: a ground is measured from the other side.</summary>
    private static readonly HashSet<string> _notTextOrEdge =
    [
        "KpBgApp", "KpBgPanel", "KpBgCard", "KpBgHover", "KpBgActive", "KpBgActiveHover", "KpSurface0", "KpSurface1", "KpSurface2", "KpSurface3",
        "KpInk0", "KpInk1", "KpInk2", "KpInk3", "KpInk4", "KpInk5", "KpTerminal", "KpBackdrop", "KpFocusGap",
        "KpAccent", "KpAccentHover", "KpAccentPress", "KpAccentTint", "KpAccentTintStrong", "KpAccentHalo", "KpAccentQuiet",
        "KpDangerTint", "KpDangerTintHover", "KpMarkInk", "KpMarkDot",
    ];

    public static TheoryData<string, string, string, double> Pairs()
    {
        var data = new TheoryData<string, string, string, double>();

        foreach (var palette in Palettes.Keys)
        {
            foreach (var (token, grounds, minimum) in _pairs)
            {
                foreach (var ground in grounds)
                {
                    data.Add(palette, token, ground, minimum);
                }
            }
        }

        return data;
    }

    private static Dictionary<string, ThemeVariant> Palettes => new()
    {
        ["Dark"] = ThemeVariant.Dark,
        ["Light"] = ThemeVariant.Light,
        ["HighContrastDark"] = HighContrast.Dark,
        ["HighContrastLight"] = HighContrast.Light,
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public Task A_token_reads_on_its_ground(string palette, string token, string ground, double minimum) => HeadlessSession.On(() =>
    {
        var variant = Palettes[palette];
        var ratio = Contrast(Colour(token, variant), Colour(ground, variant));

        Assert.True(ratio >= minimum, $"{token} on {ground} in {palette} is {ratio:F2}:1, under {minimum}:1");
    });

    [Fact]
    public Task A_selected_rows_tint_keeps_its_text_readable() => HeadlessSession.On(() =>
    {
        foreach (var (palette, variant) in Palettes)
        {
            foreach (var ground in _raisedAtMost)
            {
                var tinted = Over(Colour("KpAccentTint", variant), Colour(ground, variant));

                foreach (var token in new[] { "KpTextPrimary", "KpTextSecondary" })
                {
                    var ratio = Contrast(Colour(token, variant), tinted);
                    Assert.True(ratio >= _text, $"{token} on a selected row over {ground} in {palette} is {ratio:F2}:1");
                }
            }
        }
    });

    [Fact]
    public Task A_control_edge_is_the_control_token_and_changes_under_the_pointer() => HeadlessSession.On(() =>
    {
        foreach (var (palette, variant) in Palettes)
        {
            foreach (var (rest, pointerOver) in _controlEdges)
            {
                Assert.Equal(Colour("KpBorderControl", variant), Colour(rest, variant));

                var change = Contrast(Colour(rest, variant), Colour(pointerOver, variant));
                Assert.True(change >= _visibleChange, $"{pointerOver} in {palette} is only {change:F2}:1 from {rest}");
            }
        }
    });

    [Theory]
    [InlineData("HighContrastDark", "Dark")]
    [InlineData("HighContrastLight", "Light")]
    public Task High_contrast_lowers_no_pair(string high, string normal) => HeadlessSession.On(() =>
    {
        var held = _pairs.Select(pair => (pair.Token, pair.Grounds)).Concat(_decorative).Concat(_disabled);

        foreach (var (token, grounds) in held)
        {
            foreach (var ground in grounds)
            {
                var raised = Contrast(Colour(token, Palettes[high]), Colour(ground, Palettes[high]));
                var plain = Contrast(Colour(token, Palettes[normal]), Colour(ground, Palettes[normal]));

                Assert.True(raised >= plain, $"{token} on {ground} is {raised:F2}:1 in {high} and {plain:F2}:1 in {normal}");
            }
        }
    });

    [Theory]
    [InlineData("HighContrastDark", "Dark")]
    [InlineData("HighContrastLight", "Light")]
    public Task High_contrast_raises_the_terminal_lines(string high, string normal) => HeadlessSession.On(() =>
    {
        foreach (var line in new[] { "KpLine1", "KpLine2", "KpLine3", "KpTerminalDivider" })
        {
            var raised = Contrast(Colour(line, Palettes[high]), Colour("KpTerminal", Palettes[high]));
            var plain = Contrast(Colour(line, Palettes[normal]), Colour("KpTerminal", Palettes[normal]));

            Assert.True(raised > plain && raised >= _edge, $"{line} on KpTerminal is {raised:F2}:1 in {high} and {plain:F2}:1 in {normal}");
        }
    });

    [Fact]
    public Task Every_brush_in_the_tokens_is_measured_or_exempt_or_named_as_a_ground() => HeadlessSession.On(() =>
    {
        var tokens = Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(new Uri("avares://keypaste-app/Theme/Tokens.axaml")));
        var keys = tokens.Where(pair => pair.Value is SolidColorBrush).Select(pair => (string)pair.Key).ToList();

        foreach (var palette in tokens.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            keys.AddRange(palette.Where(pair => pair.Value is SolidColorBrush).Select(pair => (string)pair.Key));
        }

        var measured = _pairs.Select(pair => pair.Token).Concat(_decorative.Select(pair => pair.Token)).Concat(_disabled.Select(pair => pair.Token)).ToHashSet();
        var unmeasured = keys.Distinct().Where(key => !measured.Contains(key) && !_notTextOrEdge.Contains(key)).ToList();
        Assert.True(unmeasured.Count == 0, string.Join(", ", unmeasured));
    });

    private static Color Colour(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), $"{key} is not a {variant} resource");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static Color Over(Color tint, Color ground)
    {
        var alpha = tint.A / 255d;
        byte Mix(byte over, byte under) => (byte)Math.Round((over * alpha) + (under * (1 - alpha)));
        return Color.FromRgb(Mix(tint.R, ground.R), Mix(tint.G, ground.G), Mix(tint.B, ground.B));
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
