using Avalonia;

namespace Keypaste.App.Views;

/// <summary>
/// The app draws its own 40px titlebar in the window's title area and keeps the platform's caption
/// buttons: on the right on Windows and Linux, the traffic lights on the left on macOS.
/// </summary>
internal static class WindowChrome
{
    public const double TitleBarHeight = 40;

    /// <summary>Room the titlebar's content leaves for the caption buttons.</summary>
    /// <remarks>
    /// macOS needs none here: its traffic lights sit over the empty column above the sidebar.
    /// Windows' three caption buttons are 46px each.
    /// </remarks>
    public static Thickness CaptionInset { get; } =
        OperatingSystem.IsMacOS() ? default : new Thickness(0, 0, 140, 0);
}
