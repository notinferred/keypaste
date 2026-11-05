using System.Globalization;

namespace Keypaste.Core.Settings;

/// <summary>Whether the desktop app's window fills its screen when it opens.</summary>
public enum WindowMode
{
    /// <summary>At its own bounds.</summary>
    Normal = 0,

    /// <summary>Filling the screen's working area.</summary>
    Maximized = 1,

    /// <summary>Filling the whole screen.</summary>
    FullScreen = 2,
}

/// <summary>How the person left the desktop app's window, as <c>[[window]]</c> in <c>app.toml</c> keeps it.</summary>
/// <param name="X">The left edge of the window at its own bounds, in the platform's screen units, negative left of the main screen.</param>
/// <param name="Y">The top edge, likewise.</param>
/// <param name="Width">The width at its own bounds, in device-independent pixels.</param>
/// <param name="Height">The height, likewise.</param>
/// <param name="Mode">Whether it fills the screen, over those bounds.</param>
/// <param name="ListShare">The list's share of the height it divides with the preview, in thousandths, or null until the divider is moved.</param>
/// <param name="SidebarHidden">Whether View › Hide sidebar is in force.</param>
public sealed record WindowLayout(int X, int Y, int Width, int Height, WindowMode Mode, int? ListShare, bool SidebarHidden)
{
    private const int _largestSide = 100_000;

    /// <summary>The bounds as one string, because the file's numbers cannot be negative and a screen left of the main one has negative positions.</summary>
    internal string BoundsText =>
        string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height}");

    internal string ModeText => Mode switch
    {
        WindowMode.Maximized => "maximized",
        WindowMode.FullScreen => "fullscreen",
        _ => "normal",
    };

    /// <summary>Reads what the file holds; bounds that make no window read as none, and the app opens where it would with no file.</summary>
    internal static WindowLayout? Read(string bounds, string? mode, int? listShare, bool sidebarHidden)
    {
        var parts = bounds.Split(',');

        if (parts.Length != 4
            || !TryReadPosition(parts[0], out var x)
            || !TryReadPosition(parts[1], out var y)
            || !TryReadSide(parts[2], out var width)
            || !TryReadSide(parts[3], out var height))
        {
            return null;
        }

        return new WindowLayout(x, y, width, height, ModeOf(mode), listShare is > 0 and < 1000 ? listShare : null, sidebarHidden);
    }

    private static WindowMode ModeOf(string? text) => text switch
    {
        "maximized" => WindowMode.Maximized,
        "fullscreen" => WindowMode.FullScreen,
        _ => WindowMode.Normal,
    };

    private static bool TryReadPosition(string text, out int value) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && Math.Abs((long)value) <= _largestSide;

    private static bool TryReadSide(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is > 0 and <= _largestSide;
}
