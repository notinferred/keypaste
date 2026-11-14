using System.Runtime.InteropServices;

namespace Keypaste.Infrastructure;

/// <summary>
/// Keeps the system from writing this process's memory, where secrets may be, to a core dump.
/// </summary>
/// <remarks>
/// <para>
/// On Linux and macOS the soft <c>RLIMIT_CORE</c> becomes zero, which the kernel and systemd-coredump honour. The hard
/// limit stays, so a child that inherits the soft limit can raise it again. Only <see cref="TryPreventAttach"/> clears
/// <c>PR_SET_DUMPABLE</c>: in the desktop it would also stop xdg-desktop-portal reading the app's <c>/proc</c> entry, and
/// with it the desktop's portal calls.
/// </para>
/// <para>
/// Windows writes a local dump only where an administrator configured Windows Error Reporting's LocalDumps for the
/// machine, and nothing here overrides that.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives: every signature is
/// blittable, and the generator would need unsafe code project-wide.
/// </para>
/// </remarks>
public static class MemoryDumps
{
    private const int _rlimitCore = 4;
    private const int _prSetDumpable = 4;

    /// <summary>Lowers this process's core dump limit to zero, best effort.</summary>
    /// <returns>Whether the limit is now zero; false on Windows and when the system refused.</returns>
    public static bool TryPrevent()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            if (getrlimit(_rlimitCore, out var limit) != 0)
            {
                return false;
            }

            limit.Current = 0;
            return setrlimit(_rlimitCore, ref limit) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>On Linux, stops a process of the same user attaching to this one or reading its memory through <c>/proc</c>.</summary>
    /// <returns>Whether the process is no longer dumpable; false off Linux and when the system refused.</returns>
    /// <remarks>An <c>execve</c> makes a child dumpable again, so what this process starts is unaffected.</remarks>
    public static bool TryPreventAttach() => OperatingSystem.IsLinux() && prctl(_prSetDumpable, 0, 0, 0, 0) == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct ResourceLimit
    {
        public ulong Current;
        public ulong Maximum;
    }

    [DllImport("libc", EntryPoint = "getrlimit", SetLastError = true)]
    private static extern int getrlimit(int resource, out ResourceLimit limit);

    [DllImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
    private static extern int setrlimit(int resource, ref ResourceLimit limit);

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static extern int prctl(int option, nuint second, nuint third, nuint fourth, nuint fifth);
}
