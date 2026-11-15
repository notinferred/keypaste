using System.Runtime.InteropServices;

namespace Keypaste.Infrastructure;

/// <summary>
/// Keeps a pinned array's pages in RAM, out of swap, and on Windows out of Windows Error Reporting's dumps, best effort.
/// </summary>
/// <remarks>
/// <para>
/// A page lock does not stack: one <c>munlock</c> or <c>VirtualUnlock</c> releases a page however many arrays share it,
/// so each page is counted and unlocked when the last array on it is released. A lock the system refuses, as a small
/// <c>RLIMIT_MEMLOCK</c> or working set does, leaves the array as it was. A hibernation file still holds locked pages.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives: every signature is
/// blittable, and the generator would need unsafe code project-wide.
/// </para>
/// </remarks>
public static class SecretMemory
{
    private static readonly Lock _gate = new();
    private static readonly Dictionary<nint, int> _lockedPages = [];
    private static readonly Dictionary<char[], Hold> _holds = new(ReferenceEqualityComparer.Instance);

    /// <summary>Locks <paramref name="pinned"/>'s pages in RAM and, on Windows, leaves it out of error reports.</summary>
    /// <param name="pinned">An array allocated pinned, so its address holds until it is released.</param>
    /// <returns>Whether every page is locked and, on Windows, the array is left out of error reports.</returns>
    public static bool TryHold(char[] pinned)
    {
        ArgumentNullException.ThrowIfNull(pinned);

        var start = Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0);
        var length = pinned.Length * sizeof(char);
        var pages = PagesOf(start, length);

        lock (_gate)
        {
            var locked = pages.Where(TryLockPage).ToArray();
            var excluded = OperatingSystem.IsWindows() && TryExcludeFromReports(start, length);
            _holds[pinned] = new(locked, excluded);

            return locked.Length == pages.Length && (excluded || !OperatingSystem.IsWindows());
        }
    }

    /// <summary>Undoes <see cref="TryHold"/>; the caller zeroes the array first.</summary>
    public static void Release(char[] pinned)
    {
        ArgumentNullException.ThrowIfNull(pinned);

        lock (_gate)
        {
            if (!_holds.Remove(pinned, out var hold))
            {
                return;
            }

            foreach (var page in hold.Pages)
            {
                UnlockPage(page);
            }

            if (hold.Excluded)
            {
                _ = WerUnregisterExcludedMemoryBlock(Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0));
            }
        }
    }

    private static nint[] PagesOf(nint start, int length)
    {
        if (length == 0)
        {
            return [];
        }

        var size = Environment.SystemPageSize;
        var first = start & ~(nint)(size - 1);
        var last = (start + length - 1) & ~(nint)(size - 1);

        return [.. Enumerable.Range(0, (int)((last - first) / size) + 1).Select(index => first + (index * size))];
    }

    private static bool TryLockPage(nint page)
    {
        if (_lockedPages.TryGetValue(page, out var count))
        {
            _lockedPages[page] = count + 1;
            return true;
        }

        var size = (nuint)Environment.SystemPageSize;

        if (!(OperatingSystem.IsWindows() ? VirtualLock(page, size) : mlock(page, size) == 0))
        {
            return false;
        }

        _lockedPages[page] = 1;
        return true;
    }

    private static void UnlockPage(nint page)
    {
        var count = _lockedPages[page] - 1;

        if (count > 0)
        {
            _lockedPages[page] = count;
            return;
        }

        _lockedPages.Remove(page);
        var size = (nuint)Environment.SystemPageSize;

        if (OperatingSystem.IsWindows())
        {
            _ = VirtualUnlock(page, size);
        }
        else
        {
            _ = munlock(page, size);
        }
    }

    private static bool TryExcludeFromReports(nint start, int length)
    {
        try
        {
            return WerRegisterExcludedMemoryBlock(start, (uint)length) == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private sealed record Hold(nint[] Pages, bool Excluded);

    [DllImport("libc", EntryPoint = "mlock", SetLastError = true)]
    private static extern int mlock(nint address, nuint length);

    [DllImport("libc", EntryPoint = "munlock", SetLastError = true)]
    private static extern int munlock(nint address, nuint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualLock(nint address, nuint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualUnlock(nint address, nuint size);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int WerRegisterExcludedMemoryBlock(nint address, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int WerUnregisterExcludedMemoryBlock(nint address);
}
