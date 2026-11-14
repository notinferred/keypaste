using System.Globalization;
using System.Runtime.InteropServices;
using Keypaste.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A secret's pinned array is locked in RAM while it is held, and a page stays locked until the last array on it is released.</summary>
[Collection(nameof(LockedMemoryAlone))]
public sealed class SecretMemoryTests
{
    [Fact]
    public void A_pinned_array_is_held_and_released()
    {
        Assert.SkipWhen(OperatingSystem.IsLinux() && LinuxLockLimitBytes() < 1024 * 1024, "This system lets a process lock less than 1 MiB.");

        var array = GC.AllocateArray<char>(SecretBuffer.InitialCapacity, pinned: true);

        Assert.True(SecretMemory.TryHold(array));
        SecretMemory.Release(array);
        Assert.True(SecretMemory.TryHold(array));
        SecretMemory.Release(array);
    }

    [Fact]
    public void A_page_two_arrays_share_stays_locked_until_both_are_released()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux reports a process's locked memory.");
        Assert.SkipWhen(LinuxLockLimitBytes() < 1024 * 1024, "This system lets a process lock less than 1 MiB.");

        var first = GC.AllocateArray<char>(8, pinned: true);
        var second = GC.AllocateArray<char>(8, pinned: true);

        while (PageOf(second) != PageOf(first))
        {
            (first, second) = (second, GC.AllocateArray<char>(8, pinned: true));
        }

        var before = LockedKilobytes();
        Assert.True(SecretMemory.TryHold(first));
        Assert.True(SecretMemory.TryHold(second));
        var held = LockedKilobytes();
        Assert.SkipWhen(held == before, "Another hold already locked the page.");

        SecretMemory.Release(first);
        Assert.Equal(held, LockedKilobytes());

        SecretMemory.Release(second);
        Assert.Equal(before, LockedKilobytes());
    }

    private static nint PageOf(char[] pinned) =>
        Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0) & ~(nint)(Environment.SystemPageSize - 1);

    private static long LockedKilobytes() =>
        long.Parse(File.ReadLines("/proc/self/status").Single(line => line.StartsWith("VmLck:", StringComparison.Ordinal))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);

    private static long LinuxLockLimitBytes()
    {
        var soft = File.ReadLines("/proc/self/limits").Single(line => line.StartsWith("Max locked memory", StringComparison.Ordinal))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)[3];

        return soft == "unlimited" ? long.MaxValue : long.Parse(soft, CultureInfo.InvariantCulture);
    }
}

[CollectionDefinition(nameof(LockedMemoryAlone), DisableParallelization = true)]
public sealed class LockedMemoryAlone;
