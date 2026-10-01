using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.App;

/// <summary>Lets the running app bring its window to the front for a second start the person made (D-0397).</summary>
/// <remarks>
/// Windows gives the foreground only to the process the person last used, which is the second start,
/// not the app it asks to show its window.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsForeground
{
    private const int _anyProcess = -1;

    /// <summary>Passes this process's right to the foreground on to any process, as <c>ASFW_ANY</c> does.</summary>
    internal static void LetAnyProcessTakeIt() => _ = AllowSetForegroundWindow(_anyProcess);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
