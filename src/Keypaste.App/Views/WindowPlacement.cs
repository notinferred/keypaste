using Avalonia;
using Keypaste.Core.Settings;

namespace Keypaste.App.Views;

/// <summary>A screen's working area, and how many of its units make one device-independent pixel.</summary>
internal readonly record struct ScreenArea(PixelRect WorkingArea, double Scaling);

/// <summary>Where a kept window goes on the screens there are now.</summary>
internal static class WindowPlacement
{
    /// <summary>Puts the window wholly inside the working area it most overlaps, or the first one when it overlaps none.</summary>
    /// <param name="saved">The window as it was left.</param>
    /// <param name="minimum">The window's minimum size, in device-independent pixels.</param>
    /// <param name="areas">The screens, the main one first; at least one.</param>
    /// <returns>Its position in screen units and its size in device-independent pixels.</returns>
    internal static (PixelPoint Position, Size Size) Fit(WindowLayout saved, Size minimum, IReadOnlyList<ScreenArea> areas)
    {
        // MaxBy keeps the first of equals, so a window on no screen goes to the main one.
        var (area, scaling) = areas.MaxBy(area => Overlap(saved, area));

        var width = Math.Min(Math.Max(saved.Width, minimum.Width), area.Width / scaling);
        var height = Math.Min(Math.Max(saved.Height, minimum.Height), area.Height / scaling);
        var spanX = (int)Math.Ceiling(width * scaling);
        var spanY = (int)Math.Ceiling(height * scaling);

        return (
            new PixelPoint(
                Math.Clamp(saved.X, area.X, Math.Max(area.X, area.Right - spanX)),
                Math.Clamp(saved.Y, area.Y, Math.Max(area.Y, area.Bottom - spanY))),
            new Size(width, height));
    }

    private static long Overlap(WindowLayout saved, ScreenArea screen)
    {
        var area = screen.WorkingArea;
        var right = saved.X + (long)Math.Ceiling(saved.Width * screen.Scaling);
        var bottom = saved.Y + (long)Math.Ceiling(saved.Height * screen.Scaling);
        var across = Math.Min(right, area.Right) - Math.Max(saved.X, area.X);
        var down = Math.Min(bottom, area.Bottom) - Math.Max(saved.Y, area.Y);

        return across > 0 && down > 0 ? across * down : 0;
    }
}
