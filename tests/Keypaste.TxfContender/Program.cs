using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Keypaste.TxfContender;

// This helper must omit Keypaste.Core so its temporary-directory redirect cannot remove contention.
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
            Console.Error.WriteLine($"contender failed: {ex.GetType().Name}: {ex.Message}");
            return 6;
        }
    }

    private static int Hold(string directory, string ready, string release)
    {
        // The collision depends on FileTransactionEx.TxfPrepare's filename stem.
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

            // Publish a closed file so the reader cannot observe readiness before the contents are readable.
            var pendingReady = ready + ".pending";
            File.WriteAllText(pendingReady, name);
            File.Move(pendingReady, ready);

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
