using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using KeePassLib.Serialization;
using Xunit;

namespace Keypaste.Core.Tests;

// F.63's probe, deleted once it has run: it reports by failing, so its counts reach the job log.
public sealed class TxfReservationProbe
{
    private const int _errorTransactionalConflict = 6800;
    private const int _heldTries = 20;
    private const int _commits = 1000;

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Measure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Transactional NTFS is a Windows file system feature.");
            return;
        }

        ProcessTemporaryDirectory.EnsureRedirected();
        var temp = Path.GetTempPath();
        var work = Directory.CreateTempSubdirectory("txf-probe-").FullName;
        var vault = Path.Combine(work, "vault.kdbx");
        File.WriteAllBytes(vault, [1, 2, 3]);

        try
        {
            var (heldRefused, heldOther) = Held(temp, work, vault);
            var (lingeringRefused, lingeringOther) = Lingering(vault);

            Assert.Fail(
                $"probe result: Windows {Environment.OSVersion.Version}, temporary directory {temp}; "
                + $"held {heldRefused}/{_heldTries} refused, other failures [{heldOther}]; "
                + $"lingering {lingeringRefused}/{_commits} refused, other failures [{lingeringOther}]");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [SupportedOSPlatform("windows")]
    private static (int Refused, string Other) Held(string temp, string work, string vault)
    {
        var refused = 0;
        var other = new List<string>();

        for (var i = 0; i < _heldTries; i++)
        {
            var name = Path.Combine(temp, "KeePass_TxF_" + Guid.NewGuid().ToString("N") + ".tmp");
            var destination = Path.Combine(Directory.CreateDirectory(Path.Combine(work, "held-" + i)).FullName, "moved.tmp");
            File.WriteAllBytes(name, [0xF6]);

            var transaction = Native.CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "keypaste F.63 probe");
            if (transaction == Native.InvalidHandle)
            {
                throw new Win32Exception();
            }

            try
            {
                if (!Native.MoveFileTransactedW(name, destination, IntPtr.Zero, IntPtr.Zero,
                        Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting, transaction))
                {
                    throw new Win32Exception();
                }

                Count(TryCommit(vault), ref refused, other);
            }
            finally
            {
                Native.RollbackTransaction(transaction);
                Native.CloseHandle(transaction);
                File.Delete(name);
            }
        }

        return (refused, string.Join("; ", other.Distinct()));
    }

    private static (int Refused, string Other) Lingering(string vault)
    {
        var refused = 0;
        var other = new List<string>();

        for (var i = 0; i < _commits; i++)
        {
            Count(TryCommit(vault), ref refused, other);
        }

        return (refused, string.Join("; ", other.Distinct()));
    }

    private static void Count(Exception? failure, ref int refused, List<string> other)
    {
        if (failure is IOException && (failure.HResult & 0xFFFF) == _errorTransactionalConflict)
        {
            refused++;
        }
        else if (failure is not null)
        {
            other.Add($"{failure.GetType().Name}: {failure.Message}");
        }
    }

    private static Exception? TryCommit(string vault)
    {
        try
        {
            using var transaction = new FileTransactionEx(IOConnectionInfo.FromPath(vault), true);
            using (var written = transaction.OpenWrite())
            {
                written.Write([4, 5, 6]);
            }

            transaction.CommitWrite();
            return null;
        }
        catch (IOException ex)
        {
            return ex;
        }
        catch (Win32Exception ex)
        {
            return ex;
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex;
        }
    }

#pragma warning disable SYSLIB1054 // LibraryImport's generated stub needs unsafe blocks.
    private static class Native
    {
        internal const uint MoveFileReplaceExisting = 0x1;
        internal const uint MoveFileCopyAllowed = 0x2;

        internal static readonly IntPtr InvalidHandle = new(-1);

        [DllImport("KtmW32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateTransaction(IntPtr securityAttributes, IntPtr guid,
            uint options, uint isolationLevel, uint isolationFlags, uint timeout, string? description);

        [DllImport("KtmW32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RollbackTransaction(IntPtr transaction);

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MoveFileTransactedW(string existing, string replacement,
            IntPtr progress, IntPtr data, uint flags, IntPtr transaction);

        [DllImport("Kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
#pragma warning restore SYSLIB1054
}
