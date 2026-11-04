using Avalonia.Styling;

namespace Keypaste.App;

/// <summary>The palettes for the system's contrast preference, each inheriting every resource it does not change from light or dark.</summary>
internal static class HighContrast
{
    public static readonly ThemeVariant Dark = new("HighContrastDark", ThemeVariant.Dark);
    public static readonly ThemeVariant Light = new("HighContrastLight", ThemeVariant.Light);
}
