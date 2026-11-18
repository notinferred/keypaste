using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Keypaste.App;

/// <summary>
/// Every accent colour the app draws, derived from the system's accent for one palette, each moved only as far as it must be to read on its grounds.
/// </summary>
/// <param name="Accent">A filled primary button.</param>
/// <param name="Hover">The filled primary under the pointer.</param>
/// <param name="Pressed">The filled primary while pressed.</param>
/// <param name="OnAccent">Text and icons on the fill: white, or ink where the system's accent is too light to carry white.</param>
/// <param name="Text">The accent as text, an icon or a waiting dot.</param>
/// <param name="Ring">The focus ring and a focused field's border.</param>
/// <param name="Tint">A selected row's ground, opaque.</param>
internal sealed record AccentPalette(Color Accent, Color Hover, Color Pressed, Color OnAccent, Color Text, Color Ring, Color Tint)
{
    internal static readonly Color SystemBlueLight = Color.FromRgb(0x00, 0x7A, 0xFF);
    internal static readonly Color SystemBlueDark = Color.FromRgb(0x0A, 0x84, 0xFF);

    private static readonly Color _white = Colors.White;
    private static readonly Color _ink = Color.FromRgb(0x1D, 0x1D, 0x1F);

    /// <summary>The ink a fill carries where white on it starts under 3:1.</summary>
    internal static Color Ink => _ink;

    /// <summary>What Avalonia reports when the platform has no accent of its own, as headless and older desktops do.</summary>
    private static readonly Color _unreported = new PlatformColorValues().AccentColor1;

    /// <summary>Under this, white on the system's accent would need the fill darkened past recognition, so the fill carries ink instead.</summary>
    private const double _whiteCarries = 3;

    private const double _step = 0.01;

    /// <summary>Derives the palette for one set of grounds.</summary>
    /// <param name="system">The platform's accent, or null or the unreported default for system blue.</param>
    /// <param name="dark">Whether the grounds are dark.</param>
    /// <param name="highContrast">Whether the system asks for more contrast: text then keeps 7:1 and the ring 4.5:1.</param>
    /// <param name="grounds">Every ground accent text and the ring sit on.</param>
    /// <param name="window">The ground a selected row's tint mixes into.</param>
    /// <param name="onTint">The text a selected row keeps readable on its tint.</param>
    internal static AccentPalette Derive(Color? system, bool dark, bool highContrast, IReadOnlyList<Color> grounds, Color window, IReadOnlyList<Color> onTint)
    {
        var text = highContrast ? 7 : 4.5;
        var ring = highContrast ? 4.5 : 3;
        var seed = Reported(system) ?? (dark ? SystemBlueDark : SystemBlueLight);

        var onAccent = Contrast(_white, seed) >= _whiteCarries ? _white : _ink;
        var away = onAccent == _white ? -1 : 1;
        var fill = Shift(seed, away, colour => Contrast(onAccent, colour) >= text);
        var lighter = dark ? 1 : -1;

        return new AccentPalette(
            fill,
            Lightness(fill, away * 0.06),
            Lightness(fill, away * 0.12),
            onAccent,
            Shift(seed, lighter, colour => grounds.All(ground => Contrast(colour, ground) >= text)),
            Shift(seed, lighter, colour => grounds.All(ground => Contrast(colour, ground) >= ring)),
            Tinted(seed, window, dark ? 0.24 : 0.14, onTint));
    }

    /// <summary>The platform's accent, unless it reports none.</summary>
    internal static Color? Reported(Color? system) =>
        system is { A: > 0 } colour && colour != _unreported ? Color.FromRgb(colour.R, colour.G, colour.B) : null;

    /// <summary>The palette for each of the app's four variants, as the resources the theme and Fluent's controls read.</summary>
    /// <param name="system">The platform's accent.</param>
    /// <param name="resources">Where each variant's grounds and text colours are found.</param>
    internal static Dictionary<ThemeVariant, ResourceDictionary> Resources(Color? system, IResourceNode resources)
    {
        var variants = new Dictionary<ThemeVariant, ResourceDictionary>();

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light, HighContrast.Dark, HighContrast.Light })
        {
            Color Read(string key) => resources.TryGetResource(key, variant, out var value) && value is ISolidColorBrush brush ? brush.Color : Colors.Transparent;

            var palette = Derive(
                system,
                dark: variant == ThemeVariant.Dark || variant.InheritVariant == ThemeVariant.Dark,
                highContrast: variant == HighContrast.Dark || variant == HighContrast.Light,
                grounds: [Read("KpBgApp"), Read("KpBgPanel"), Read("KpBgCard"), Read("KpBgHover"), Read("KpBgSidebar"), Read("KpBgRail")],
                window: Read("KpBgApp"),
                onTint: [Read("KpTextPrimary"), Read("KpTextSecondary")]);

            variants[variant] = palette.ToResources();
        }

        return variants;
    }

    private ResourceDictionary ToResources()
    {
        var glow = new BoxShadows(new BoxShadow { Spread = 3, Color = WithAlpha(Ring, 0x47) });

        return new ResourceDictionary
        {
            ["KpAccent"] = new SolidColorBrush(Accent),
            ["KpAccentHover"] = new SolidColorBrush(Hover),
            ["KpAccentPress"] = new SolidColorBrush(Pressed),
            ["KpOnAccent"] = new SolidColorBrush(OnAccent),
            ["KpAccentText"] = new SolidColorBrush(Text),
            ["KpFocusRing"] = new SolidColorBrush(Ring),
            ["KpAccentTint"] = new SolidColorBrush(Tint),
            ["KpAccentHalo"] = new SolidColorBrush(WithAlpha(Text, 0x40)),
            ["KpFieldGlow"] = glow,
            ["KpWaitingHalo"] = new BoxShadows(new BoxShadow { Spread = 3, Color = WithAlpha(Text, 0x40) }),

            ["SystemAccentColor"] = Accent,
            ["SystemAccentColorDark1"] = Lightness(Accent, -0.08),
            ["SystemAccentColorDark2"] = Lightness(Accent, -0.16),
            ["SystemAccentColorDark3"] = Lightness(Accent, -0.24),
            ["SystemAccentColorLight1"] = Lightness(Accent, 0.08),
            ["SystemAccentColorLight2"] = Lightness(Accent, 0.16),
            ["SystemAccentColorLight3"] = Lightness(Accent, 0.24),
            ["AccentButtonBackground"] = new SolidColorBrush(Accent),
            ["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(Hover),
            ["AccentButtonBackgroundPressed"] = new SolidColorBrush(Pressed),
            ["AccentButtonForeground"] = new SolidColorBrush(OnAccent),
            ["AccentButtonForegroundPointerOver"] = new SolidColorBrush(OnAccent),
            ["AccentButtonForegroundPressed"] = new SolidColorBrush(OnAccent),
            ["ToggleButtonBackgroundChecked"] = new SolidColorBrush(Accent),
            ["ToggleButtonBackgroundCheckedPointerOver"] = new SolidColorBrush(Hover),
            ["ToggleButtonBackgroundCheckedPressed"] = new SolidColorBrush(Pressed),
            ["ToggleButtonForegroundChecked"] = new SolidColorBrush(OnAccent),
            ["ToggleButtonForegroundCheckedPointerOver"] = new SolidColorBrush(OnAccent),
            ["ToggleButtonForegroundCheckedPressed"] = new SolidColorBrush(OnAccent),
            ["TextControlBorderBrushFocused"] = new SolidColorBrush(Ring),
            ["SystemControlFocusVisualPrimaryBrush"] = new SolidColorBrush(Ring),
            ["ComboBoxBorderBrushPressed"] = new SolidColorBrush(Ring),
            ["TextControlSelectionHighlightColor"] = new SolidColorBrush(WithAlpha(Accent, 0x66)),

            // A checked box, a chosen radio and a switch that is on take the accent, as macOS draws them; the one-accent rule counts only filled buttons.
            ["CheckBoxCheckBackgroundFillChecked"] = new SolidColorBrush(Accent),
            ["CheckBoxCheckBackgroundFillCheckedPointerOver"] = new SolidColorBrush(Hover),
            ["CheckBoxCheckBackgroundFillCheckedPressed"] = new SolidColorBrush(Pressed),
            ["CheckBoxCheckBackgroundStrokeChecked"] = new SolidColorBrush(Accent),
            ["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = new SolidColorBrush(Hover),
            ["CheckBoxCheckBackgroundStrokeCheckedPressed"] = new SolidColorBrush(Pressed),
            ["CheckBoxCheckGlyphForegroundChecked"] = new SolidColorBrush(OnAccent),
            ["CheckBoxCheckGlyphForegroundCheckedPointerOver"] = new SolidColorBrush(OnAccent),
            ["CheckBoxCheckGlyphForegroundCheckedPressed"] = new SolidColorBrush(OnAccent),
            ["RadioButtonOuterEllipseCheckedStroke"] = new SolidColorBrush(Accent),
            ["RadioButtonOuterEllipseCheckedStrokePointerOver"] = new SolidColorBrush(Hover),
            ["RadioButtonOuterEllipseCheckedStrokePressed"] = new SolidColorBrush(Pressed),
            ["RadioButtonOuterEllipseCheckedFill"] = new SolidColorBrush(Accent),
            ["RadioButtonOuterEllipseCheckedFillPointerOver"] = new SolidColorBrush(Hover),
            ["RadioButtonOuterEllipseCheckedFillPressed"] = new SolidColorBrush(Pressed),
            ["RadioButtonCheckGlyphFill"] = new SolidColorBrush(OnAccent),
            ["RadioButtonCheckGlyphFillPointerOver"] = new SolidColorBrush(OnAccent),
            ["RadioButtonCheckGlyphFillPressed"] = new SolidColorBrush(OnAccent),
            ["ToggleSwitchFillOn"] = new SolidColorBrush(Accent),
            ["ToggleSwitchFillOnPointerOver"] = new SolidColorBrush(Hover),
            ["ToggleSwitchFillOnPressed"] = new SolidColorBrush(Pressed),
        };
    }

    /// <summary>Moves a colour's lightness one way, a step at a time, until it reads; the end of the range if nothing short of it does.</summary>
    private static Color Shift(Color colour, int direction, Func<Color, bool> reads)
    {
        var hsl = colour.ToHsl();

        for (var lightness = hsl.L; lightness is >= 0 and <= 1; lightness += direction * _step)
        {
            var moved = HslColor.ToRgb(hsl.H, hsl.S, lightness);

            if (reads(moved))
            {
                return moved;
            }
        }

        return direction > 0 ? _white : Colors.Black;
    }

    /// <summary>The accent mixed into the window, thinned until the row's text keeps 4.5:1 on it.</summary>
    private static Color Tinted(Color accent, Color window, double most, IReadOnlyList<Color> text)
    {
        for (var share = most; share > 0; share -= _step)
        {
            var tint = Mix(accent, window, share);

            if (text.All(colour => Contrast(colour, tint) >= 4.5))
            {
                return tint;
            }
        }

        return window;
    }

    private static Color Lightness(Color colour, double by)
    {
        var hsl = colour.ToHsl();
        return HslColor.ToRgb(hsl.H, hsl.S, Math.Clamp(hsl.L + by, 0, 1));
    }

    private static Color Mix(Color over, Color under, double share)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round((a * share) + (b * (1 - share)));
        return Color.FromRgb(Channel(over.R, under.R), Channel(over.G, under.G), Channel(over.B, under.B));
    }

    private static Color WithAlpha(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);

    /// <summary>WCAG 2's contrast ratio.</summary>
    internal static double Contrast(Color a, Color b)
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
