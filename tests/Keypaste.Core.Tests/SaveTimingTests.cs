using System.Runtime.Versioning;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V-F.10a: the save instrument tells a save held at the gate from a save whose own work is slow.
/// V-F.10b: a save that cannot transact never waits at that gate.
/// </summary>
/// <remarks>
/// Serialised against every other collection, because any other save in this process would take
/// the gate these cases need to control. The gate is held by a transacted save whose first attempt
/// is refused by <see cref="HeldTransactedName"/>, blocking in its retry wait; only a transacted save
/// takes the gate, so these cases are Windows-only and skip where the refusal cannot be arranged.
/// </remarks>
[Collection(nameof(SavesTimedAlone))]
public sealed class SaveTimingTests : IDisposable
{
    private const string _notWindows =
        "Transactional NTFS is a Windows file system feature; no transacted save can be held at the gate here.";

    private const string _noTransactions =
        "this volume does not support transactions, so no save can be held at the gate.";

    private const string _noRefusal =
        "this Windows version allows a plain move onto a held name, so the holder's attempt is not refused.";

    private static readonly TimeSpan _hold = TimeSpan.FromMilliseconds(1000);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-f10-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ASaveHeldAtTheGate_IsReportedAsGateWait_AndNamesTheHolder()
    {
        var (holder, held) = HoldAVault("holder");
        using (holder)
        using (held)
        {
            using var waiter = SavedVault("waiter").Vault;

            var (holding, waited) = WhileTheGateIsHeld(holder, held, () => waiter.SaveWaiting(null, attempts: 1));

            Assert.Equal(holding.Operation, waited.HeldBy);
            Assert.True(
                waited.GateWait >= _hold / 2,
                $"a save held for {_hold.TotalMilliseconds} ms reported {SaveTimings.Describe(waited)}");
            Assert.True(
                waited.GateWait?.TotalMilliseconds > SaveTimings.Milliseconds(waited.Attempts),
                $"the held save's own work was not smaller than its gate wait: {SaveTimings.Describe(waited)}");
            AssertComponentsFitTheTotal(waited);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ADoomedSave_DoesNotQueueBehindATransactedSave()
    {
        var (holder, held) = HoldAVault("holder");
        using (holder)
        using (held)
        {
            using var doomed = DoomedVault("doomed");

            var (_, waited) = WhileTheGateIsHeld(
                holder, held, () => Assert.Throws<VaultException>(() => doomed.SaveWaiting(null, attempts: 1)));

            Assert.True(
                waited.GateWait is null && waited.HeldBy == 0,
                $"a save that cannot transact waited at the gate: {SaveTimings.Describe(waited)}");
            Assert.True(
                waited.Total < _hold / 2,
                $"a save that cannot transact still waited out the holder: {SaveTimings.Describe(waited)}");
        }
    }

    [Fact]
    public void ASaveDoingRealWorkAlone_IsReportedAsWork_NotGateWait()
    {
        using var vault = SavedVault("work").Vault;
        vault.AddEntry(new VaultEntry { Title = "T", Password = "p" });

        var timing = SaveTimings.Of(vault.Save);

        Assert.True(timing.Succeeded);
        Assert.Equal(0, timing.HeldBy);
        Assert.NotNull(timing.GateWait);
        Assert.True(
            SaveTimings.Milliseconds(timing.Attempts) > 10 * timing.GateWait.Value.TotalMilliseconds,
            $"key derivation and encryption did not dominate an uncontended gate: {SaveTimings.Describe(timing)}");
        AssertComponentsFitTheTotal(timing);
    }

    /// <summary>Runs <paramref name="waiter"/> while <paramref name="holder"/> blocks in its retry wait.</summary>
    [SupportedOSPlatform("windows")]
    private static (SaveTiming Holding, SaveTiming Waited) WhileTheGateIsHeld(
        Vault holder, HeldTransactedName held, Action waiter)
    {
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        SaveTiming holderTiming = default;
        SaveTiming waiterTiming = default;

        var holderThread = new Thread(() => holderTiming = SaveTimings.Of(() => holder.SaveWaiting(_ =>
        {
            holding.Set();
            release.Wait();
            held.RollBack();
        }, attempts: 2)))
        { IsBackground = true };

        var waiterThread = new Thread(() => waiterTiming = SaveTimings.Of(waiter)) { IsBackground = true };

        holderThread.Start();
        Assert.True(
            holding.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken),
            "the holder's first attempt was never refused, so it never held the gate in its wait");

        waiterThread.Start();
        Thread.Sleep(_hold);
        release.Set();

        Assert.True(holderThread.Join(TimeSpan.FromSeconds(30)) && waiterThread.Join(TimeSpan.FromSeconds(30)));
        return (holderTiming, waiterTiming);
    }

    /// <summary>A saved vault whose name is held by a transaction; skips where that cannot be arranged.</summary>
    [SupportedOSPlatform("windows")]
    private (Vault Vault, HeldTransactedName Held) HoldAVault(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
        }

        var (vault, path) = SavedVault(name);

        var held = HeldTransactedName.TryHold(path);
        if (held is null)
        {
            vault.Dispose();
            Assert.Skip(_noTransactions);
        }

        if (!HeldTransactedName.ReproducesTheRefusal(path))
        {
            held.Dispose();
            vault.Dispose();
            Assert.Skip(_noRefusal);
        }

        return (vault, held);
    }

    private static void AssertComponentsFitTheTotal(SaveTiming timing)
    {
        var parts = (timing.Check ?? TimeSpan.Zero) + timing.Redirect + (timing.GateWait ?? TimeSpan.Zero)
            + (timing.Stamp ?? TimeSpan.Zero)
            + TimeSpan.FromMilliseconds(
                SaveTimings.Milliseconds(timing.Attempts)
                + SaveTimings.Milliseconds(timing.Waits)
                + SaveTimings.Milliseconds(timing.Rereads));

        Assert.True(parts <= timing.Total, $"the components exceed the whole: {SaveTimings.Describe(timing)}");
    }

    /// <summary>A vault saved once, so its file exists and a save of it transacts.</summary>
    private (Vault Vault, string Path) SavedVault(string name)
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        var path = Path.Combine(home, "vault.kdbx");
        var vault = Vault.Create(path, VaultSaveTests.MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "seeded", Password = "seed" });
        vault.Save();
        return (vault, path);
    }

    /// <summary>A vault whose directory is gone, so every attempt fails before it could transact.</summary>
    private Vault DoomedVault(string name)
    {
        var (vault, path) = SavedVault(name);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        return vault;
    }
}

[CollectionDefinition(nameof(SavesTimedAlone), DisableParallelization = true)]
public sealed class SavesTimedAlone;
