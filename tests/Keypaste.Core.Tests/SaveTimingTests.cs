using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V-F.10a: the save instrument tells a save held at the gate from a save whose own work is slow.
/// V-F.10b: a save that cannot transact never waits at that gate.
/// V-F.12: a save sleeping between attempts does not hold that gate, and a save that waited for it re-reads.
/// </summary>
/// <remarks>
/// Serialised against every other collection, because any other save in this process would take
/// the gate these cases need to control. The gate is held by a save blocked inside its attempt, which
/// any save over an existing file does on every platform. Only the retry-wait case needs a refused
/// attempt, which <see cref="HeldTransactedName"/> arranges on Windows alone.
/// </remarks>
[Collection(nameof(SavesTimedAlone))]
public sealed class SaveTimingTests : IDisposable
{
    private const string _notWindows =
        "Transactional NTFS is a Windows file system feature; no save can be refused into its retry wait here.";

    private const string _noTransactions =
        "this volume does not support transactions, so no save can be refused into its retry wait.";

    private const string _noRefusal =
        "this Windows version allows a plain move onto a held name, so the holder's attempt is not refused.";

    private static readonly TimeSpan _hold = TimeSpan.FromMilliseconds(1000);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-f10-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ASaveHeldAtTheGate_IsReportedAsGateWait_AndNamesTheHolder()
    {
        using var holder = SavedVault("holder").Vault;
        using var waiter = SavedVault("waiter").Vault;

        var (holding, waited) = WhileInsideAnAttempt(holder, () => waiter.SaveWaiting(null, attempts: 1));

        var gated = Assert.Single(waited.Attempts);
        Assert.Equal(holding.Operation, gated.HeldBy);
        Assert.True(
            gated.Gate >= _hold / 2,
            $"a save held for {_hold.TotalMilliseconds} ms reported {SaveTimings.Describe(waited)}");
        Assert.True(
            gated.Gate > gated.Work,
            $"the held save's own work was not smaller than its gate wait: {SaveTimings.Describe(waited)}");
        AssertComponentsFitTheTotal(waited);
    }

    [Fact]
    public void ADoomedSave_DoesNotQueueBehindATransactedSave()
    {
        using var holder = SavedVault("holder").Vault;
        using var doomed = DoomedVault("doomed");

        var (_, waited) = WhileInsideAnAttempt(
            holder, () => Assert.Throws<VaultException>(() => doomed.SaveWaiting(null, attempts: 1)));

        Assert.True(
            waited.Attempts.All(attempt => attempt.Gate is null),
            $"a save that cannot transact waited at the gate: {SaveTimings.Describe(waited)}");
        Assert.True(
            waited.Total < _hold / 2,
            $"a save that cannot transact still waited out the holder: {SaveTimings.Describe(waited)}");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ATransactedSave_DoesNotQueueBehindAnotherSavesRetryWait()
    {
        var (holder, held) = HoldAVault("holder");
        using (holder)
        using (held)
        {
            using var waiter = SavedVault("waiter").Vault;

            var (holding, waited) = WhileTheHolderSleeps(holder, held, () => waiter.SaveWaiting(null, attempts: 1));

            var gated = Assert.Single(waited.Attempts);
            Assert.True(
                gated.Gate < TimeSpan.FromMilliseconds(50),
                $"a save queued behind another save's retry wait: {SaveTimings.Describe(waited)}");

            var refused = holding.Attempts[0];
            Assert.True(
                refused.Held < refused.Wait,
                $"the holder kept the gate through its retry wait: {SaveTimings.Describe(holding)}");
        }
    }

    [Fact]
    public void AnInProcessCommitDuringTheGateWait_IsRefusedRatherThanReverted()
    {
        var (holder, path) = SavedVault("shared");
        using (holder)
        {
            using var waiter = Vault.Open(path, VaultSaveTests.MasterPassword);
            holder.AddEntry(new VaultEntry { Title = "committed-while-queued", Password = "kept" });
            waiter.AddEntry(new VaultEntry { Title = "must-not-land", Password = "no" });

            WhileInsideAnAttempt(
                holder, () => Assert.Throws<VaultChangedOnDiskException>(() => waiter.SaveWaiting(null, attempts: 1)));

            using var reopened = Vault.Open(path, VaultSaveTests.MasterPassword);
            Assert.NotNull(reopened.Find("committed-while-queued"));
            Assert.Null(reopened.Find("must-not-land"));
        }
    }

    [Fact]
    public void ASaveDoingRealWorkAlone_IsReportedAsWork_NotGateWait()
    {
        using var vault = SavedVault("work").Vault;
        vault.AddEntry(new VaultEntry { Title = "T", Password = "p" });

        var timing = SaveTimings.Of(vault.Save);

        Assert.True(timing.Succeeded);
        var gated = Assert.Single(timing.Attempts);
        Assert.Equal(0, gated.HeldBy);
        Assert.NotNull(gated.Gate);
        Assert.True(
            gated.Work > 10 * gated.Gate,
            $"key derivation and encryption did not dominate an uncontended gate: {SaveTimings.Describe(timing)}");
        Assert.True(gated.Held >= gated.Work, $"the hold did not cover the attempt: {SaveTimings.Describe(timing)}");
        AssertComponentsFitTheTotal(timing);
    }

    /// <summary>Runs <paramref name="waiter"/> while <paramref name="holder"/> is blocked inside its only attempt.</summary>
    private static (SaveTiming Holding, SaveTiming Waited) WhileInsideAnAttempt(Vault holder, Action waiter)
    {
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        return Concurrently(
            () => holder.SaveWaiting(null, attempts: 1, duringAttempt: _ =>
            {
                inside.Set();
                release.Wait();
            }),
            inside,
            release,
            waiter,
            "the holder never entered its attempt");
    }

    /// <summary>Runs <paramref name="waiter"/> while <paramref name="holder"/> blocks in its retry wait.</summary>
    [SupportedOSPlatform("windows")]
    private static (SaveTiming Holding, SaveTiming Waited) WhileTheHolderSleeps(
        Vault holder, HeldTransactedName held, Action waiter)
    {
        using var sleeping = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        return Concurrently(
            () => holder.SaveWaiting(_ =>
            {
                sleeping.Set();
                release.Wait();
                held.RollBack();
            }, attempts: 2),
            sleeping,
            release,
            waiter,
            "the holder's first attempt was never refused, so it never reached its retry wait");
    }

    private static (SaveTiming Holding, SaveTiming Waited) Concurrently(
        Action holder, ManualResetEventSlim holding, ManualResetEventSlim release, Action waiter, string neverHeld)
    {
        SaveTiming holderTiming = default;
        SaveTiming waiterTiming = default;
        ExceptionDispatchInfo? failure = null;

        Thread Start(Action save, Action<SaveTiming> record)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    record(SaveTimings.Of(save));
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                    holding.Set();
                }
            })
            { IsBackground = true };
            thread.Start();
            return thread;
        }

        var holderThread = Start(holder, timing => holderTiming = timing);
        Assert.True(holding.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken), neverHeld);
        failure?.Throw();

        var waiterThread = Start(waiter, timing => waiterTiming = timing);
        Thread.Sleep(_hold);
        release.Set();

        Assert.True(holderThread.Join(TimeSpan.FromSeconds(30)) && waiterThread.Join(TimeSpan.FromSeconds(30)));
        failure?.Throw();
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
        var parts = (timing.Check ?? TimeSpan.Zero) + timing.Redirect + (timing.Stamp ?? TimeSpan.Zero)
            + SaveTimings.Sum(timing, a => a.Gate)
            + SaveTimings.Sum(timing, a => a.Reread)
            + SaveTimings.Sum(timing, a => a.Work)
            + SaveTimings.Sum(timing, a => a.Wait);

        Assert.True(parts <= timing.Total, $"the components exceed the whole: {SaveTimings.Describe(timing)}");
    }

    /// <summary>A vault saved once, so its file exists and a save of it gates.</summary>
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
