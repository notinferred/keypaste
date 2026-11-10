using System.Runtime.InteropServices;

namespace Keypaste.App.Globalization;

/// <summary>Starts the app in invariant globalization on a Linux machine with no ICU, where .NET would otherwise end the process at its first use of a culture.</summary>
internal static class IcuFallback
{
    internal const string InvariantSwitch = "System.Globalization.Invariant";

    // The range .NET's own search for ICU covers.
    private const int _oldestIcu = 50;
    private const int _newestIcu = 255;

    /// <summary>Must run first in Main: reading any culture, even to format a number here, fixes the mode.</summary>
    internal static void Apply()
    {
        if (OperatingSystem.IsLinux() && !IcuLoads())
        {
            AppContext.SetSwitch(InvariantSwitch, true);
        }
    }

    internal static bool IcuLoads()
    {
        for (var major = _newestIcu; major >= _oldestIcu; major--)
        {
            var version = major >= 100
                ? new string([Digit(major / 100), Digit(major / 10 % 10), Digit(major % 10)])
                : new string([Digit(major / 10), Digit(major % 10)]);

            if (NativeLibrary.TryLoad(string.Concat("libicuuc.so.", version), out var common)
                && NativeLibrary.TryLoad(string.Concat("libicui18n.so.", version), out var internationalization))
            {
                NativeLibrary.Free(internationalization);
                NativeLibrary.Free(common);
                return true;
            }
        }

        return false;
    }

    private static char Digit(int value) => (char)('0' + value);
}
