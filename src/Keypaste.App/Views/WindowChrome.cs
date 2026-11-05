using Avalonia;

namespace Keypaste.App.Views;

/// <summary>
/// The app draws its own 40px titlebar in the window's title area and keeps the platform's caption
/// buttons: on the right on Windows and Linux, the traffic lights on the left on macOS.
/// </summary>
internal static class WindowChrome
{
    public const double TitleBarHeight = 40;

    /// <summary>The size the window opens at with nothing kept.</summary>
    public const double DefaultWidth = 1000;

    /// <inheritdoc cref="DefaultWidth"/>
    public const double DefaultHeight = 680;

    /// <summary>The smallest the window goes: half of a common laptop's screen wide, so two windows sit side by side.</summary>
    public const double MinimumWidth = 720;

    /// <inheritdoc cref="MinimumWidth"/>
    public const double MinimumHeight = 520;

    /// <summary>Under this width the sidebar collapses and Items drops its Username and URL columns; from here up, beside the sidebar, those columns keep room for a few characters.</summary>
    public const double NarrowBelow = 840;

    /// <summary>The sidebar's width, and the titlebar's first column while the sidebar shows.</summary>
    public const double SidebarWidth = 232;

    /// <summary>Room the titlebar's content leaves for the caption buttons, which the platform draws and no zoom scales.</summary>
    /// <remarks>
    /// macOS needs none here: its traffic lights sit over the empty column above the sidebar.
    /// Windows' three caption buttons are 46px each.
    /// </remarks>
    public static Thickness CaptionInset(double zoom) =>
        OperatingSystem.IsMacOS() ? default : new Thickness(0, 0, 140 / zoom, 0);

    /// <summary>The titlebar's first column with no sidebar under it: room for the traffic lights on macOS, which no zoom scales, and the menu button elsewhere, which zooms with it.</summary>
    public static double LeadingInset(double zoom) => OperatingSystem.IsMacOS() ? 80 / zoom : 48;
}
