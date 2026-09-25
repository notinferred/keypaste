using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Keypaste.Core.Launch;

/// <summary>
/// Keeps an agent's command and everything it starts from outliving the run, so no descendant keeps
/// the injected values in its environment afterwards (THREATS.md T-35).
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows:</b> the child is put in a Job Object that kills every member when its handle closes,
/// with no breakaway right, straight after it starts; a grandchild started in that gap escapes. The
/// job is terminated at the end of every run and dies with the bridge.
/// </para>
/// <para>
/// <b>Linux:</b> the child starts through util-linux <c>setsid</c>, so it has no controlling
/// terminal and leads a process group that is killed at the end of every run. A process that leaves
/// the group with <c>setsid</c> of its own is found only when the bridge has called
/// <see cref="AdoptOrphans"/>: orphans are then reparented to it and killed after each run.
/// </para>
/// <para>
/// <b>macOS:</b> best effort. The tree is killed while the child lives; a descendant that outlives it
/// is not found, and the child shares the bridge's terminal.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives: every
/// signature is blittable, and the generator would need unsafe code project-wide.
/// </para>
/// </remarks>
internal sealed class ChildContainment : IDisposable
{
    private const int _sigkill = 9;
    private const int _prSetChildSubreaper = 36;
    private const int _waitNoHang = 1;
    private const int _jobObjectExtendedLimitInformation = 9;
    private const uint _killOnJobClose = 0x2000;

    private static readonly string[] _setsidCandidates = ["/usr/bin/setsid", "/bin/setsid"];
    private static int _adopted;

    private IntPtr _job;
    private int _group;

    private ChildContainment()
    {
    }

    /// <summary>The <c>setsid</c> a Linux child starts through, or null elsewhere and where none is installed.</summary>
    internal static string? SessionLeader =>
        OperatingSystem.IsLinux() ? _setsidCandidates.FirstOrDefault(File.Exists) : null;

    /// <summary>Marks this process as the reaper of its descendants' orphans, so a run's daemon can be found and killed.</summary>
    /// <remarks>Linux only, and only for a process whose every child is a run's: after a run, every child it still has is killed.</remarks>
    internal static void AdoptOrphans()
    {
        if (!OperatingSystem.IsLinux() || Interlocked.Exchange(ref _adopted, 1) == 1)
        {
            return;
        }

        try
        {
            _ = prctl(_prSetChildSubreaper, 1, 0, 0, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Volatile.Write(ref _adopted, 0);
        }
    }

    /// <summary>Contains a child that has just started.</summary>
    /// <param name="process">The child.</param>
    /// <param name="leadsGroup">Whether it started through <see cref="SessionLeader"/>.</param>
    /// <returns>What ends it and everything it started.</returns>
    internal static ChildContainment Contain(Process process, bool leadsGroup)
    {
        var containment = new ChildContainment();

        if (OperatingSystem.IsWindows())
        {
            containment._job = WindowsJob(process);
        }
        else if (leadsGroup)
        {
            containment._group = process.Id;
        }

        return containment;
    }

    /// <summary>Kills the child and everything it started that can still be found.</summary>
    /// <param name="process">The child.</param>
    internal void KillAll(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Exited between the check and the kill, or already being torn down.
        }

        if (_job != IntPtr.Zero)
        {
            _ = TerminateJobObject(_job, 1);
        }

        if (_group > 0)
        {
            try
            {
                _ = kill(-_group, _sigkill);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // No libc to ask: the tree kill above is all there is.
            }
        }

        if (Volatile.Read(ref _adopted) == 1)
        {
            KillOrphans();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var job = Interlocked.Exchange(ref _job, IntPtr.Zero);

        if (job != IntPtr.Zero)
        {
            _ = CloseHandle(job);
        }
    }

    private static IntPtr WindowsJob(Process process)
    {
        var job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);

        if (job == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = _killOnJobClose } };

        if (!SetInformationJobObject(job, _jobObjectExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())
            || !AssignProcessToJobObject(job, process.Handle))
        {
            _ = CloseHandle(job);
            return IntPtr.Zero;
        }

        return job;
    }

    /// <summary>Kills every child this process still has, and theirs as they are reparented here, then reaps them.</summary>
    private static void KillOrphans()
    {
        var self = Environment.ProcessId;
        HashSet<int> killed = [];

        for (var round = 0; round < 8; round++)
        {
            var children = ChildrenOf(self).Where(pid => !killed.Contains(pid)).ToList();

            if (children.Count == 0)
            {
                break;
            }

            foreach (var pid in children)
            {
                _ = kill(pid, _sigkill);
                killed.Add(pid);
            }

            Thread.Sleep(20);
        }

        foreach (var pid in killed)
        {
            _ = waitpid(pid, IntPtr.Zero, _waitNoHang);
        }
    }

    private static IEnumerable<int> ChildrenOf(int parent)
    {
        IEnumerable<string> entries;

        try
        {
            entries = Directory.EnumerateDirectories("/proc").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var directory in entries)
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            string stat;

            try
            {
                stat = File.ReadAllText(Path.Combine(directory, "stat"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // "pid (comm) state ppid ...": the command may hold spaces and parentheses, so read after the last ')'.
            var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length > 2
                && fields[0] != "Z"
                && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ppid)
                && ppid == parent)
            {
                yield return pid;
            }
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static extern int prctl(int option, nuint argument2, nuint argument3, nuint argument4, nuint argument5);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static extern int waitpid(int pid, IntPtr status, int options);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, IntPtr name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimits information, uint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
