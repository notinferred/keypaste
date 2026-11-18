using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Core.Ipc;

/// <summary>A running process as the operating system describes it.</summary>
/// <param name="Pid">Its id.</param>
/// <param name="Path">Its executable, or null when the system did not say.</param>
/// <param name="Parent">The id of the process that started it, or null.</param>
/// <param name="Started">When it started, in the system's own units, which only compare with another process's; null when unknown.</param>
/// <param name="Arguments">Its command line, the program first, or null when the system did not say.</param>
internal sealed record ProcessFacts(int Pid, string? Path, int? Parent, long? Started, IReadOnlyList<string>? Arguments = null);

/// <summary>Reads processes by id, so a pipe's caller can be traced to the app that started it.</summary>
internal interface IProcessTree
{
    /// <summary>The process with <paramref name="pid"/>, or null when it is gone or cannot be read.</summary>
    ProcessFacts? Of(int pid);
}

/// <summary>
/// The operating system's processes: <c>/proc</c> on Linux, <c>proc_pidinfo</c> and <c>KERN_PROCARGS2</c> on macOS, and on
/// Windows <c>NtQueryInformationProcess</c> with the process's creation time and command line.
/// </summary>
/// <remarks>
/// A parent's id can be reused once it exits, so a start time is read with each process and a caller compares them.
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives.
/// </remarks>
internal sealed class SystemProcessTree : IProcessTree
{
    internal static SystemProcessTree Instance { get; } = new();

    private const int _procPidTbsdInfo = 3;
    private const int _macPathBytes = 4096;
    private const uint _queryLimitedInformation = 0x1000;
    private const int _ctlKern = 1;
    private const int _kernProcArgs2 = 49;
    private const int _macArgumentBytes = 262144;
    private const int _processCommandLineInformation = 60;
    private const int _windowsCommandLineBytes = 65536;

    /// <inheritdoc/>
    public ProcessFacts? Of(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        try
        {
            return OperatingSystem.IsLinux() ? LinuxOf(pid)
                : OperatingSystem.IsMacOS() ? MacOf(pid)
                : OperatingSystem.IsWindows() ? WindowsOf(pid)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    private static ProcessFacts? LinuxOf(int pid)
    {
        var stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/stat"));

        // The command name is in parentheses and may hold spaces or parentheses itself, so fields are counted after the last ')'.
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        var parent = int.Parse(fields[1], CultureInfo.InvariantCulture);
        var started = long.Parse(fields[19], CultureInfo.InvariantCulture);
        var path = new FileInfo(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/exe")).LinkTarget;
        var arguments = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/cmdline")).Split('\0', StringSplitOptions.RemoveEmptyEntries);

        return new ProcessFacts(pid, path, parent, started, arguments);
    }

    [SupportedOSPlatform("macos")]
    private static ProcessFacts? MacOf(int pid)
    {
        var size = Marshal.SizeOf<MacBsdInfo>();

        if (proc_pidinfo(pid, _procPidTbsdInfo, 0, out var info, size) != size)
        {
            return null;
        }

        var buffer = new byte[_macPathBytes];
        var length = proc_pidpath(pid, buffer, (uint)buffer.Length);
        var path = length > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, length) : null;
        var started = (checked((long)info.StartSeconds) * 1_000_000) + (long)info.StartMicroseconds;

        return new ProcessFacts(pid, path, (int)info.Parent, started, MacArgumentsOf(pid));
    }

    /// <summary>A process's arguments from <c>KERN_PROCARGS2</c>: their count, the executable's path, padding, then each argument.</summary>
    [SupportedOSPlatform("macos")]
    private static string[]? MacArgumentsOf(int pid)
    {
        var buffer = new byte[_macArgumentBytes];
        var size = (nuint)buffer.Length;

        if (sysctl([_ctlKern, _kernProcArgs2, pid], 3, buffer, ref size, 0, 0) != 0 || size < sizeof(int))
        {
            return null;
        }

        var count = BitConverter.ToInt32(buffer, 0);
        var at = sizeof(int);
        var end = (int)size;

        // Past the executable's path and the NULs that pad it.
        while (at < end && buffer[at] != 0)
        {
            at++;
        }

        while (at < end && buffer[at] == 0)
        {
            at++;
        }

        List<string> arguments = [];

        while (arguments.Count < count && at < end)
        {
            var stop = Array.IndexOf(buffer, (byte)0, at, end - at);
            stop = stop < 0 ? end : stop;
            arguments.Add(System.Text.Encoding.UTF8.GetString(buffer, at, stop - at));
            at = stop + 1;
        }

        return [.. arguments];
    }

    [SupportedOSPlatform("windows")]
    private static ProcessFacts? WindowsOf(int pid)
    {
        var process = OpenProcess(_queryLimitedInformation, inheritHandle: false, (uint)pid);

        if (process == 0)
        {
            return null;
        }

        try
        {
            int? parent = NtQueryInformationProcess(process, 0, out var basic, Marshal.SizeOf<WindowsBasicInformation>(), out _) == 0
                ? (int)basic.InheritedFromUniqueProcessId
                : null;
            long? started = GetProcessTimes(process, out var created, out _, out _, out _) ? created : null;
            var buffer = new char[32768];
            var length = buffer.Length;
            var path = QueryFullProcessImageNameW(process, 0, buffer, ref length) ? new string(buffer, 0, length) : null;

            return new ProcessFacts(pid, path, parent, started, WindowsArgumentsOf(process));
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    /// <summary>A process's command line, split as the C runtime splits it, from <c>ProcessCommandLineInformation</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static string[]? WindowsArgumentsOf(nint process)
    {
        var buffer = Marshal.AllocHGlobal(_windowsCommandLineBytes);

        try
        {
            if (NtQueryInformationProcess(process, _processCommandLineInformation, buffer, _windowsCommandLineBytes, out _) != 0)
            {
                return null;
            }

            // A UNICODE_STRING: its length in bytes, then a pointer to the characters, which follow it in the same buffer.
            var line = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, nint.Size), Marshal.ReadInt16(buffer) / 2);
            var split = CommandLineToArgvW(line, out var count);

            if (split == 0)
            {
                return null;
            }

            try
            {
                return [.. Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(split, i * nint.Size)) ?? string.Empty)];
            }
            finally
            {
                _ = LocalFree(split);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>macOS's 136-byte <c>struct proc_bsdinfo</c>, of which the parent and the start time are read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 136)]
    private struct MacBsdInfo
    {
        [FieldOffset(16)]
        public uint Parent;

        [FieldOffset(120)]
        public ulong StartSeconds;

        [FieldOffset(128)]
        public ulong StartMicroseconds;
    }

    /// <summary>Windows's <c>PROCESS_BASIC_INFORMATION</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [DllImport("libc", EntryPoint = "proc_pidinfo", SetLastError = true)]
    private static extern int proc_pidinfo(int pid, int flavor, ulong argument, out MacBsdInfo info, int size);

    [DllImport("libc", EntryPoint = "proc_pidpath", SetLastError = true)]
    private static extern int proc_pidpath(int pid, [Out] byte[] buffer, uint size);

    [DllImport("libc", EntryPoint = "sysctl", SetLastError = true)]
    private static extern int sysctl(int[] name, uint length, [Out] byte[] value, ref nuint size, nint newValue, nuint newSize);

    [DllImport("ntdll.dll", ExactSpelling = true, EntryPoint = "NtQueryInformationProcess")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryInformationProcess(nint process, int informationClass, nint information, int length, out int returned);

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LocalFree(nint memory);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryInformationProcess(nint process, int informationClass, out WindowsBasicInformation information, int length, out int returned);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
