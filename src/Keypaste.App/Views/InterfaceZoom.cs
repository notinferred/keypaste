using Keypaste.Core.Settings;

namespace Keypaste.App.Views;

/// <summary>How large every window draws the interface, as View › Zoom in, Zoom out and Actual size set it. Window › Zoom is another thing: it fills the screen.</summary>
internal static class InterfaceZoom
{
    private static readonly int[] _steps = [WindowLayout.ActualSizePercent, 125, 150, 175, WindowLayout.LargestZoomPercent];

    internal static int Percent { get; private set; } = WindowLayout.ActualSizePercent;

    internal static double Factor => Percent / 100d;

    internal static bool CanEnlarge => Percent < WindowLayout.LargestZoomPercent;

    internal static bool CanReduce => Percent > WindowLayout.ActualSizePercent;

    /// <summary>Raised after the zoom changes, on the UI thread.</summary>
    internal static event Action? Changed;

    /// <summary>Takes a percentage, clamped into range; one a hand edit put between two steps is kept until the next step.</summary>
    internal static void Set(int percent)
    {
        var clamped = Math.Clamp(percent, WindowLayout.ActualSizePercent, WindowLayout.LargestZoomPercent);

        if (clamped != Percent)
        {
            Percent = clamped;
            Changed?.Invoke();
        }
    }

    internal static void Enlarge() => Set(_steps.FirstOrDefault(step => step > Percent, Percent));

    internal static void Reduce() => Set(_steps.LastOrDefault(step => step < Percent, Percent));

    internal static void ActualSize() => Set(WindowLayout.ActualSizePercent);
}
