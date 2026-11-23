namespace Keypaste.App.Session;

/// <summary>What a quick unlock asks on this machine.</summary>
internal static class MachineQuickUnlock
{
    /// <summary>What the person knows Touch ID as.</summary>
    internal const string TouchIdName = "Touch ID";

    /// <summary>
    /// Windows Hello in the app's Windows build on Windows 10 2004 or later (D-0549), Touch ID on macOS (D-0681), and
    /// nothing elsewhere.
    /// </summary>
    /// <param name="home">The keypaste home, whose app alone keeps Touch ID's keys under it.</param>
    /// <param name="unexpected">Keeps a report of a failure the keys do not report as an outcome, at the site it names, or null (D-0596).</param>
    /// <remarks>Touch ID deletes the keys an earlier run under this home left, so the app composes it once.</remarks>
    internal static IQuickUnlockKeys? Keys(string home, Action<Exception, string>? unexpected = null)
    {
#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return new WindowsHello(unexpected);
        }
#endif
        if (OperatingSystem.IsMacOS())
        {
            return new TouchId(home, unexpected);
        }

        return null;
    }
}
