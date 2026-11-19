using Avalonia;

namespace Keypaste.App.Views;

/// <summary>
/// The app draws its own 52px title bar, Outlook's way, holding the idle countdown and the save state, and keeps the platform's caption
/// buttons: on the right on Windows and Linux, the traffic lights on the left on macOS.
/// </summary>
internal static class WindowChrome
{
    public const double TitleBarHeight = 52;

    /// <summary>The size the window opens at with nothing kept.</summary>
    public const double DefaultWidth = 1000;

    /// <inheritdoc cref="DefaultWidth"/>
    public const double DefaultHeight = 680;

    /// <summary>The smallest the window goes: half of a common laptop's screen wide, so two windows sit side by side.</summary>
    public const double MinimumWidth = 720;

    /// <inheritdoc cref="MinimumWidth"/>
    public const double MinimumHeight = 520;

    /// <summary>Under this width the sidebar hides, leaving the rail, the list and the item, and opens over the content from View › Show sidebar.</summary>
    public const double NarrowBelow = 900;

    /// <summary>Under this width the content is as tight as it is beside the rail: the log's table needs 744 to reach its Result, with a margin for a longer word.</summary>
    public const double ContentTightBelow = 768;

    /// <summary>Where toasts sit: in the band of Items' list header and the open item's title, above its actions.</summary>
    public static Thickness ToastMargin { get; } = new(20, TitleBarHeight + 52, 20, 20);

    /// <summary>The sidebar's width.</summary>
    public const double SidebarWidth = 240;

    /// <summary>The width of the rail of places, at every width.</summary>
    public const double RailWidth = 52;

    /// <summary>Room the titlebar's content leaves for the caption buttons, which the platform draws and no zoom scales.</summary>
    /// <remarks>
    /// macOS needs none here: its traffic lights sit in the title bar's first column.
    /// Windows' three caption buttons are 46px each.
    /// </remarks>
    public static Thickness CaptionInset(double zoom) =>
        OperatingSystem.IsMacOS() ? default : new Thickness(0, 0, 140 / zoom, 0);

    /// <summary>The title bar's first column: room for the traffic lights on macOS, which no zoom scales, and the menu button elsewhere, which zooms with it.</summary>
    public static double LeadingInset(double zoom) => OperatingSystem.IsMacOS() ? 80 / zoom : 48;
}
