using Avalonia;

namespace Keypaste.App.Views;

/// <summary>
/// The app keeps a 40px area in the window's title, empty but for the vault's foot while the sidebar is away, and keeps the platform's caption
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

    /// <summary>Under this width the sidebar collapses to the rail.</summary>
    public const double NarrowBelow = 840;

    /// <summary>Under this width the content is as tight as it is beside the rail: the log's table needs 744 to reach its Result, with a margin for a longer word.</summary>
    public const double ContentTightBelow = 768;

    /// <summary>Where toasts sit: under the titlebar and the row of Items' search and New.</summary>
    public static Thickness ToastMargin { get; } = new(20, TitleBarHeight + 96, 20, 20);

    /// <summary>The sidebar's width, and the titlebar's first column while the sidebar shows.</summary>
    public const double SidebarWidth = 232;

    /// <summary>The width of the rail of icons the sidebar collapses to.</summary>
    public const double RailWidth = 56;

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
