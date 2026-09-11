using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Core.Tests;

/// <summary>
/// A Transactional NTFS transaction holding a move onto one name, so a save for that name meets the
/// refusal docs/STEPS.md F.7 is about.
/// </summary>
/// <remarks>
/// <para>
/// The arrangement the probe measures, run from a test: a decoy file in the vault's own directory,
/// moved onto the vault's name inside a transaction that is not committed yet. While that is
/// pending the name is reserved, KeePassLib's transacted move is refused, and its fallback's second
/// hop — a plain <c>MoveFileEx</c> onto the same name — is refused too, which is the defect.
/// <c>scripts/txf-probe.cs</c> reports that refusal as <c>Win32Exception 6800</c> on
/// Windows 10 Pro 19045; <see cref="ReproducesTheRefusal"/> asks the running machine rather than
/// assuming it.
/// </para>
/// <para>
/// The decoy is created beside the vault rather than in <c>%TEMP%</c> so the two are on one volume
/// whatever <c>%TEMP%</c> is set to.
/// </para>
/// <para>
/// <c>[DllImport]</c> rather than <c>[LibraryImport]</c>, and <c>SYSLIB1054</c> silenced, for the
/// reason src/Keypaste.Cli/Execution/NativeSignals.cs already records: the generator emits an
/// unsafe stub and this repository does not enable unsafe blocks.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HeldTransactedName : IDisposable
{
    private readonly IntPtr _transaction;
    private bool _resolved;
    private bool _closed;

    private HeldTransactedName(IntPtr transaction)
    {
        _transaction = transaction;
    }

    /// <summary>Reserves <paramref name="path"/>, or null if this volume has no transactions.</summary>
    internal static HeldTransactedName? TryHold(string path)
    {
        var decoy = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path)!,
            "decoy-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(decoy, DecoyBytes);

        var transaction = Native.CreateTransaction(
            IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "keypaste F.7 regression");
        if (transaction == Native.InvalidHandle)
        {
            File.Delete(decoy);
            return null;
        }

        if (!Native.MoveFileTransactedW(
                decoy, path, IntPtr.Zero, IntPtr.Zero,
                Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting, transaction))
        {
            Native.CloseHandle(transaction);
            File.Delete(decoy);
            return null;
        }

        return new HeldTransactedName(transaction);
    }

    /// <summary>What the decoy holds, so a committed one can be told apart from a saved vault.</summary>
    internal static byte[] DecoyBytes { get; } = [0xF7, 0x00, 0xF7, 0x00];

    /// <summary>
    /// Whether this machine actually refuses a plain move onto the held name.
    /// </summary>
    /// <remarks>
    /// docs/STEPS.md F.7 says the refusal stands on the Windows 10 floor this project advertises
    /// and is unobserved on Server 2025. A runner where the move simply succeeds does not reproduce
    /// the defect, and a test there must say so rather than pass having exercised nothing.
    /// </remarks>
    internal static bool ReproducesTheRefusal(string held)
    {
        var scratch = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(held)!,
            "scratch-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(scratch, [0]);

        var moved = Native.MoveFileExW(
            scratch, held, Native.MoveFileCopyAllowed | Native.MoveFileReplaceExisting);

        if (!moved)
        {
            File.Delete(scratch);
            return true;
        }

        return false;
    }

    /// <summary>Ends the reservation, leaving the held name as it was.</summary>
    internal void RollBack()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        if (!Native.RollbackTransaction(_transaction))
        {
            throw new Win32Exception();
        }
    }

    /// <summary>Ends the reservation by putting the decoy at the held name.</summary>
    /// <remarks>
    /// What another process finishing its own save looks like from here: the name comes free, and
    /// the file at it is not the one this vault was opened from.
    /// </remarks>
    internal void Commit()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        if (!Native.CommitTransaction(_transaction))
        {
            throw new Win32Exception();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        // Closing the last handle to an uncommitted transaction rolls it back, so a test that
        // throws before resolving still leaves the name free.
        RollBack();
        Native.CloseHandle(_transaction);
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
    }
#pragma warning restore SYSLIB1054
}
