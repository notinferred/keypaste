using System.Runtime.InteropServices;

namespace Keypaste.Infrastructure;

/// <summary>
/// Keeps the system from writing this process's memory, where secrets may be, to a core dump.
/// </summary>
/// <remarks>
/// <para>
/// On Linux and macOS the soft <c>RLIMIT_CORE</c> becomes zero, which the kernel and systemd-coredump honour. The hard
/// limit stays, so a child that inherits the soft limit can raise it again. <c>PR_SET_DUMPABLE</c> is not used: it
/// would also stop xdg-desktop-portal reading the desktop's <c>/proc</c> entry, and with it the desktop's portal calls.
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
}
