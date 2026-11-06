using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;

namespace Keypaste.App;

/// <summary>Whether the system wants scroll bars hidden until scrolling, which Avalonia does not read.</summary>
/// <remarks>
/// macOS says through <c>NSScroller</c>'s preferred style, Windows through <c>UISettings.AutoHideScrollBars</c> in its own build,
/// GNOME through the settings portal's <c>overlay-scrolling</c>. Another Linux desktop has no standard for it, and hiding stays the default there.
/// </remarks>
internal static class SystemScrollBars
{
    private const string _objc = "/usr/lib/libobjc.A.dylib";
    private const nint _overlayStyle = 1;
    private const string _service = "org.freedesktop.portal.Desktop";
    private const string _path = "/org/freedesktop/portal/desktop";
    private const string _interface = "org.freedesktop.portal.Settings";

    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(2);

    /// <summary>Reads the system's setting.</summary>
    /// <param name="linuxBus">The session bus on Linux, or null.</param>
    /// <returns>Whether scroll bars hide until scrolling; true where the system does not say.</returns>
    internal static async Task<bool> AutoHideAsync(string? linuxBus)
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacAutoHide();
        }

#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            return new global::Windows.UI.ViewManagement.UISettings().AutoHideScrollBars;
        }
#endif

        return !OperatingSystem.IsLinux() || linuxBus is null || await LinuxAutoHideAsync(linuxBus).ConfigureAwait(false);
    }

    private static bool MacAutoHide()
    {
        var scroller = GetClass("NSScroller\0"u8.ToArray());
        return scroller == IntPtr.Zero || Send(scroller, Selector("preferredScrollerStyle\0"u8.ToArray())) == _overlayStyle;
    }

    private static async Task<bool> LinuxAutoHideAsync(string address)
    {
        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            return await OverlayScrollingAsync(connection).WaitAsync(_bound).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or ObjectDisposedException or IOException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            return true;
        }
    }

    private static Task<bool> OverlayScrollingAsync(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_service, _path, _interface, "Read", "ss");
        writer.WriteString("org.gnome.desktop.interface");
        writer.WriteString("overlay-scrolling");

        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return ReadBool(reader.ReadVariantValue());
            },
            null);
    }

    private static bool ReadBool(VariantValue value) =>
        value.Type == VariantValueType.Variant ? ReadBool(value.GetVariantValue()) : value.GetBool();

    [DllImport(_objc, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(byte[] name);

    [DllImport(_objc, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(byte[] name);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(IntPtr receiver, IntPtr selector);
}
