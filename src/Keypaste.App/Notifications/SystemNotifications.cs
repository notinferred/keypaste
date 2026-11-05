using Tmds.DBus.Protocol;

namespace Keypaste.App.Notifications;

/// <summary>Chooses this process's notifications.</summary>
internal static class SystemNotifications
{
    /// <summary>
    /// What this platform offers: UserNotifications in a macOS bundle, a toast in the Windows build on Windows 10 2004 or
    /// later, whose earlier releases post nothing, and the desktop's notification server on a Linux session bus. None of
    /// them is asked anything until a notice is posted.
    /// </summary>
    internal static ISystemNotifications ForThisProcess()
    {
#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return new WindowsNotifications(WindowsNotifications.AppId);
        }
#endif
        if (OperatingSystem.IsMacOS() && MacNotifications.InBundle())
        {
            return new MacNotifications();
        }

        if (OperatingSystem.IsLinux() && DBusAddress.Session is { } bus)
        {
            return new LinuxNotifications(bus);
        }

        return new NoSystemNotifications();
    }
}
