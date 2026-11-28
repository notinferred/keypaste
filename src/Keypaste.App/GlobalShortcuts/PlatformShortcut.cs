namespace Keypaste.App.GlobalShortcuts;

/// <summary>Picks this platform's system-wide chord.</summary>
internal static class PlatformShortcut
{
    /// <summary>What Settings says under Wayland.</summary>
    internal const string WaylandReason =
        "Wayland doesn't let an app take a key from the other apps, so keypaste has no shortcut on this desktop.";

    /// <summary>What Settings says with no X display to register on.</summary>
    internal const string NoDisplayReason =
        "keypaste found no X11 display to register a shortcut with, so it has none on this desktop.";

    /// <summary>macOS's hot keys, Windows's hot keys, X11's key grabs, or none under Wayland.</summary>
    internal static IGlobalShortcut ForThisProcess()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacGlobalShortcut();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsGlobalShortcut();
        }

        if (OperatingSystem.IsLinux())
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY");

            if (LinuxUnavailable(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), display) is { } reason)
            {
                return new NoGlobalShortcut(reason);
            }

            return X11GlobalShortcut.Open();
        }

        return new NoGlobalShortcut(NoDisplayReason);
    }

    /// <summary>
    /// Why a Linux session gives keypaste no system-wide chord: under Wayland an X11 grab reaches keypaste only while
    /// another X11 app has the keyboard, so it would seem to work and then not; with no display there is nothing to grab on.
    /// </summary>
    /// <param name="sessionType">The session's <c>XDG_SESSION_TYPE</c>.</param>
    /// <param name="waylandDisplay">The session's <c>WAYLAND_DISPLAY</c>.</param>
    /// <param name="display">The session's <c>DISPLAY</c>.</param>
    /// <returns>Settings' words, or null on X11.</returns>
    internal static string? LinuxUnavailable(string? sessionType, string? waylandDisplay, string? display) =>
        string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(waylandDisplay) ? WaylandReason
        : string.IsNullOrEmpty(display) ? NoDisplayReason
        : null;
}
