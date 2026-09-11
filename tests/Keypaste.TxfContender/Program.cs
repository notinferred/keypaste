using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Keypaste.TxfContender;

/// <summary>
/// Holds one transacted <c>KeePass_TxF_*.tmp</c> name open until it is told to let go.
/// </summary>
/// <remarks>
/// <para>
/// V-F.6's contender. It stands in for KeePass 2 — a program that is not keypaste, saving its own
/// database into the same <c>%TEMP%</c>. D-0122 measured that as the cause: any holder of such a
/// name reserves <c>KEEPAS~1.TMP</c> for the directory, and on build 10.0.26100 every other
/// <c>KeePass_TxF_*</c> create there is refused rather than given the next alias.
/// </para>
/// <para>
/// <b>It references no keypaste assembly</b>, so nothing redirects its temporary directory. A
/// contender that got the fix too would contend with nobody.
/// </para>
/// <para>
/// It holds until signalled rather than for a duration, so the test's save meets the refusal
/// every run instead of usually.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: <directory> <ready-file> <release-file>");
            return 2;
        }

        var (directory, ready, release) = (args[0], args[1], args[2]);

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Transactional NTFS is a Windows feature; there is nothing to hold here.");
            return 3;
        }

        try
        {
            return Hold(directory, ready, release);
        }
        catch (Exception ex)
        {
            // Said out loud rather than thrown, because the only reader is a test that sees an exit
            // code. A crash here once read as the save failing (ci run 34624443263) when it was the
            // contenders refusing each other.
            Console.Error.WriteLine($"contender failed: {ex.GetType().Name}: {ex.Message}");
            return 6;
        }
    }

    private static int Hold(string directory, string ready, string release)
    {
        // Exactly what FileTransactionEx.TxfPrepare builds, because the collision is on this stem.
        var name = Path.Combine(directory, "KeePass_TxF_" + Guid.NewGuid().ToString("N") + ".tmp");
        var destination = Path.Combine(
            Directory.CreateDirectory(Path.Combine(directory, "held-" + Guid.NewGuid().ToString("N"))).FullName,
            "moved.tmp");

        File.WriteAllBytes(name, [0xF6]);

        var transaction = Native.CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "keypaste V-F.6 contender");

        if (transaction == Native.InvalidHandle)
        {
            Console.Error.WriteLine($"CreateTransaction failed: {new Win32Exception().NativeErrorCode}");
            return 4;
        }

        try
        {
            if (!Native.MoveFileTransactedW(name, destination, IntPtr.Zero, IntPtr.Zero,
                    Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting, transaction))
            {
                Console.Error.WriteLine($"MoveFileTransacted failed: {new Win32Exception().NativeErrorCode}");
                return 5;
            }

            // Uncommitted, so the name and its 8.3 alias stay reserved for as long as this lives.
            File.WriteAllText(ready, name);

            while (!File.Exists(release))
            {
                Thread.Sleep(25);
            }

            return 0;
        }
        finally
        {
            Native.RollbackTransaction(transaction);
            Native.CloseHandle(transaction);
            try { Directory.Delete(Path.GetDirectoryName(destination)!, recursive: true); } catch (IOException) { }
            try { File.Delete(name); } catch (IOException) { }
        }
    }
}

internal static class Native
{
    internal const uint MoveFileReplaceExisting = 0x1;
    internal const uint MoveFileCopyAllowed = 0x2;
    internal static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("ktmw32.dll", SetLastError = true)]
    internal static extern IntPtr CreateTransaction(IntPtr security, IntPtr guid, uint options,
        uint isolationLevel, uint isolationFlags, uint timeout, string? description);

    [DllImport("ktmw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RollbackTransaction(IntPtr transaction);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileTransactedW(string existing, string replacement,
        IntPtr progress, IntPtr data, uint flags, IntPtr transaction);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
