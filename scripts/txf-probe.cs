// Measures how Transactional NTFS refuses a name, for docs/STEPS.md F.6.
//
// F.6 was opened believing that a directory enlisted in one transaction refuses operations from
// outside it. That is refuted on Windows 10 Pro 19045, and every other mechanism reachable from
// that machine is excluded too, so the remaining difference between it and the runner both
// failures came from is the operating system. This is the measurement that says which.
//
// It repairs nothing and closes nothing. V-F.6 asks for a reproduction that is red at one save
// attempt and green after a fix; a probe cannot be that, and a run of this on a runner is not a
// green CI run standing in for one.
//
//   dotnet run scripts/txf-probe.cs

// A lock file for a project with no packages is debris in scripts/, not a pinned dependency.
#:property RestorePackagesWithLockFile=false

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Transactional NTFS is a Windows file system feature; there is nothing to measure here.");
    return 0;
}

var root = Path.GetTempPath();
var volume = Path.GetPathRoot(root)!;

Console.WriteLine($"os         {Environment.OSVersion}");
Console.WriteLine($"temp root  {root}");

uint serial = 0, maxComponent = 0, volumeFlags = 0;
if (!Native.GetVolumeInformationW(volume, null, 0, ref serial, ref maxComponent, ref volumeFlags, null, 0))
{
    Console.WriteLine($"GetVolumeInformation({volume}) failed: {new Win32Exception().Message}");
    return 2;
}

const uint FileSupportsTransactions = 0x00200000;
if ((volumeFlags & FileSupportsTransactions) == 0)
{
    Console.WriteLine($"volume     {volume} does not support transactions; nothing below can run.");
    return 2;
}

Console.WriteLine($"volume     {volume} supports transactions");

Reservation();
Window();
Race(savers: 8, rounds: 200, ownDirectoryPerSaver: false);
// The same load with the one thing a repair would change. If the reservation is directory-scoped
// this comes back clean, and a per-save temporary directory is the repair; if it refuses too, the
// collision outlives the directory and that repair is dead.
Race(savers: 8, rounds: 200, ownDirectoryPerSaver: true);

return 0;

// What a transaction reserves while it holds a move out of one directory.
void Reservation()
{
    var (source, temp, vault) = Arrange("reservation");
    var alias = Probe.Alias(source);

    Console.WriteLine();
    Console.WriteLine($"=== reservation: holding a move of {Path.GetFileName(source)} (alias {alias}) out of the temp directory");

    Probe.Holding(source, vault, () =>
    {
        Probe.Report("a fresh file in the enlisted directory", () => Probe.Touch(Probe.Name(temp)));
        Probe.Report("the moved-out name itself", () => Probe.Touch(source));
        Probe.Report($"its 8.3 alias {alias} literally", () => Probe.Touch(Path.Combine(temp, alias)));
        Probe.Report("a new subdirectory of the enlisted directory", () => Directory.CreateDirectory(Path.Combine(temp, "KeePass_TxF_" + Probe.Id())));
        Probe.Report("a fresh file in the destination directory", () => Probe.Touch(Path.Combine(Path.GetDirectoryName(vault)!, "probe-" + Probe.Id() + ".tmp")));
        Probe.Report("a fresh file in the temp root itself", () => Probe.Touch(Path.Combine(root, "probe-" + Probe.Id() + ".tmp")));
        // The two lines above take a probe- name, which shares no 8.3 stem with the held one, so they
        // cannot see the collision at all - run 34619717595 had both SUCCEEDED while the same
        // directory's KeePass_TxF_ name was refused. These two ask the question that decides the
        // repair: is the reservation directory-scoped, so that a per-save directory escapes it?
        Probe.Report("a KeePass_TxF_ name in the destination directory", () => Probe.Touch(Probe.Name(Path.GetDirectoryName(vault)!)));
        Probe.Report("a KeePass_TxF_ name in the temp root itself", () => Probe.Touch(Probe.Name(root)));
        Probe.Report("a non-transacted move onto the destination name", () =>
        {
            var scratch = Probe.Touch(Path.Combine(Path.GetDirectoryName(vault)!, "scratch-" + Probe.Id() + ".tmp"));
            if (!Native.MoveFileExW(scratch, vault, Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting))
            {
                throw new Win32Exception();
            }
        });
    });

    Probe.Clean(temp);
}

// When the reservation starts and ends, and what an outside reader sees meanwhile. A saver can only
// collide with a name that is reserved and unoccupied at the same moment.
void Window()
{
    var (source, temp, vault) = Arrange("window");
    var alias = Probe.Alias(source);

    Console.WriteLine();
    Console.WriteLine("=== window: the same reservation, before and after the commit");

    var transaction = Native.CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "keypaste TxF probe");
    if (transaction == Native.InvalidHandle)
    {
        Console.WriteLine($"CreateTransaction failed: {new Win32Exception().NativeErrorCode}");
        return;
    }

    try
    {
        if (!Native.MoveFileTransactedW(source, vault, IntPtr.Zero, IntPtr.Zero,
                Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting, transaction))
        {
            Console.WriteLine($"MoveFileTransacted failed: {new Win32Exception().NativeErrorCode}");
            return;
        }

        Console.WriteLine($"  moved, not committed    source visible outside: {File.Exists(source)}");
        Inspect(temp, source, alias);

        if (!Native.CommitTransaction(transaction))
        {
            Console.WriteLine($"CommitTransaction failed: {new Win32Exception().NativeErrorCode}");
            return;
        }

        Console.WriteLine($"  committed, handle open  source visible outside: {File.Exists(source)}");
        Inspect(temp, source, alias);
    }
    finally
    {
        Native.CloseHandle(transaction);
        Probe.Clean(temp);
    }
}

void Inspect(string temp, string source, string alias)
{
    Probe.Report("  the moved-out name", () => Probe.Touch(source));
    Probe.Report($"  its alias {alias}", () => Probe.Touch(Path.Combine(temp, alias)));

    for (var i = 0; i < 3; i++)
    {
        string? made = null;
        Probe.Report("  a fresh temporary file", () => made = Probe.Touch(Probe.Name(temp)));
        if (made is not null)
        {
            Console.WriteLine($"        took the alias {Probe.Alias(made)}");
        }
    }
}

// The arrangement CI failed in: concurrent savers through one shared temporary directory, running
// the create, move and commit KeePassLib performs.
void Race(int savers, int rounds, bool ownDirectoryPerSaver)
{
    var arrangement = ownDirectoryPerSaver ? "one temporary directory each" : "one shared temporary directory";

    Console.WriteLine();
    Console.WriteLine($"=== race: {savers} savers x {rounds} rounds through {arrangement}");

    var shared = Directory.CreateDirectory(Path.Combine(root, "keypaste-txf-race-" + Probe.Id())).FullName;
    var reasons = new ConcurrentBag<string>();
    var refused = 0;
    var committed = 0;

    using var ready = new Barrier(savers);
    var started = Environment.TickCount64;

    Parallel.For(0, savers, new ParallelOptions { MaxDegreeOfParallelism = savers }, saver =>
    {
        var vault = Path.Combine(Directory.CreateDirectory(Path.Combine(shared, $"vault-{saver}")).FullName, "vault.kdbx");
        File.WriteAllText(vault, "seed");
        var payload = new byte[64 * 1024];

        var temporaries = ownDirectoryPerSaver
            ? Directory.CreateDirectory(Path.Combine(shared, $"temp-{saver}")).FullName
            : shared;

        ready.SignalAndWait();

        for (var round = 0; round < rounds; round++)
        {
            var temp = Probe.Name(temporaries);

            try
            {
                using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(payload);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref refused);
                reasons.Add($"{ex.GetType().Name} {ex.HResult & 0xFFFF}: {ex.Message}");
                continue;
            }

            var transaction = Native.CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "keypaste TxF probe");
            try
            {
                if (transaction == Native.InvalidHandle)
                {
                    reasons.Add($"CreateTransaction: {new Win32Exception().NativeErrorCode}");
                    continue;
                }

                if (!Native.MoveFileTransactedW(temp, vault, IntPtr.Zero, IntPtr.Zero,
                        Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting, transaction))
                {
                    reasons.Add($"MoveFileTransacted: {new Win32Exception().NativeErrorCode}");
                    continue;
                }

                if (!Native.CommitTransaction(transaction))
                {
                    reasons.Add($"CommitTransaction: {new Win32Exception().NativeErrorCode}");
                    continue;
                }

                Interlocked.Increment(ref committed);
            }
            finally
            {
                if (transaction != Native.InvalidHandle)
                {
                    Native.CloseHandle(transaction);
                }
            }
        }
    });

    Console.WriteLine($"  commits {committed}   refused at the create {refused}   other failures {reasons.Count - refused}   {Environment.TickCount64 - started}ms");
    foreach (var reason in reasons.Distinct(StringComparer.Ordinal).Take(6))
    {
        Console.WriteLine($"    {reason}");
    }

    Probe.Clean(shared);
}

(string Source, string Temp, string Vault) Arrange(string name)
{
    var temp = Directory.CreateDirectory(Path.Combine(root, $"keypaste-txf-{name}-" + Probe.Id())).FullName;
    var vault = Path.Combine(Directory.CreateDirectory(Path.Combine(temp, "vault")).FullName, "vault.kdbx");
    File.WriteAllText(vault, "seed");

    return (Probe.Touch(Probe.Name(temp)), temp, vault);
}

internal static class Probe
{
    // What TxfPrepare builds: PwDefs.ShortProductName + "_TxF_", an alphanumeric-filtered base64 of
    // sixteen random bytes, and ".tmp". Every one of them shares the stem the 8.3 alias comes from.
    internal static string Name(string directory) =>
        Path.Combine(directory, "KeePass_TxF_" + Id() + ".tmp");

    internal static string Id() =>
        Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("+", "").Replace("/", "").Replace("=", "");

    internal static string Touch(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(1);
        return path;
    }

    internal static string Alias(string path)
    {
        var buffer = new char[512];
        var written = Native.GetShortPathNameW(path, buffer, (uint)buffer.Length);

        return written == 0 ? "<none>" : Path.GetFileName(new string(buffer, 0, (int)written));
    }

    internal static void Holding(string source, string destination, Action act)
    {
        var transaction = Native.CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "keypaste TxF probe");
        if (transaction == Native.InvalidHandle)
        {
            Console.WriteLine($"CreateTransaction failed: {new Win32Exception().NativeErrorCode}");
            return;
        }

        try
        {
            if (!Native.MoveFileTransactedW(source, destination, IntPtr.Zero, IntPtr.Zero,
                    Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting, transaction))
            {
                Console.WriteLine($"MoveFileTransacted failed: {new Win32Exception().NativeErrorCode}");
                return;
            }

            act();
            Native.RollbackTransaction(transaction);
        }
        finally
        {
            Native.CloseHandle(transaction);
        }
    }

    internal static void Report(string label, Action act)
    {
        try
        {
            act();
            Console.WriteLine($"  {label,-52} SUCCEEDED");
        }
        catch (Exception ex)
        {
            var win32 = ex as Win32Exception ?? ex.InnerException as Win32Exception;
            Console.WriteLine($"  {label,-52} REFUSED  {ex.GetType().Name} {win32?.NativeErrorCode ?? (ex.HResult & 0xFFFF)}");
        }
    }

    internal static void Clean(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
        }
    }
}

internal static class Native
{
    internal const uint MoveFileReplaceExisting = 0x1;
    internal const uint MoveFileCopyAllowed = 0x2;

    internal static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("KtmW32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateTransaction(IntPtr securityAttributes, IntPtr guid, uint options,
        uint isolationLevel, uint isolationFlags, uint timeout, string? description);

    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CommitTransaction(IntPtr transaction);

    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RollbackTransaction(IntPtr transaction);

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileTransactedW(string existing, string replacement,
        IntPtr progress, IntPtr data, uint flags, IntPtr transaction);

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileExW(string existing, string? replacement, uint flags);

    [DllImport("Kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetShortPathNameW(string longPath, char[] shortPath, uint length);

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeInformationW(string root, char[]? name, uint nameLength,
        ref uint serial, ref uint maxComponent, ref uint flags, char[]? fileSystem, uint fileSystemLength);
}
