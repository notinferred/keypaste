using System.Runtime.InteropServices;

namespace Keypaste.App;

/// <summary>Gives the app its Dock icon on macOS only while its window is open (D-0385).</summary>
/// <remarks>
/// Avalonia sets the activation policy once, at start; this changes it while the app runs. A regular app has a Dock
/// icon and its own menu bar, an accessory app has neither and keeps its windows and its menu bar icon.
/// </remarks>
internal static class MacDock
{
    private const string _objc = "/usr/lib/libobjc.A.dylib";
    private const nint _regular = 0;
    private const nint _accessory = 1;

    /// <summary>Shows the Dock icon and brings the app forward, or takes the icon away.</summary>
    /// <param name="shown">Whether the main window is open.</param>
    internal static void Show(bool shown)
    {
        var app = Send(GetClass("NSApplication\0"u8.ToArray()), Selector("sharedApplication\0"u8.ToArray()));

        if (app == IntPtr.Zero)
        {
            return;
        }

        _ = Send(app, Selector("setActivationPolicy:\0"u8.ToArray()), shown ? _regular : _accessory);

        // A newly regular app is not active, so its menu bar would stay another app's until clicked.
        if (shown)
        {
            _ = Send(app, Selector("activateIgnoringOtherApps:\0"u8.ToArray()), 1);
        }
    }

    [DllImport(_objc, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(byte[] name);

    [DllImport(_objc, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(byte[] name);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern byte Send(IntPtr receiver, IntPtr selector, nint argument);
}
