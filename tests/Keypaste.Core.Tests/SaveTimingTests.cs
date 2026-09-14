using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V-F.10a: the save instrument tells a save held at the gate from a save whose own work is slow.
/// </summary>
/// <remarks>
/// Serialised against every other collection, because any other save in this process would take
/// the gate these two cases need to control.
/// </remarks>
[Collection(nameof(SavesTimedAlone))]
public sealed class SaveTimingTests : IDisposable
{
    private static readonly TimeSpan _hold = TimeSpan.FromMilliseconds(1000);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-f10a-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ASaveHeldAtTheGate_IsReportedAsGateWait_AndNamesTheHolder()
    {
        using var holder = DoomedVault("holder");
        using var waiter = DoomedVault("waiter");

        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        SaveTiming held = default;
        SaveTiming waited = default;

        // A failed first attempt reaches the wait callback with the gate still taken, and stays there.
        var holderThread = new Thread(() => held = SaveTimings.Of(() => Assert.Throws<VaultException>(() =>
            holder.SaveWaiting(_ => { holding.Set(); release.Wait(); }, attempts: 2))))
        { IsBackground = true };

        var waiterThread = new Thread(() => waited = SaveTimings.Of(() => Assert.Throws<VaultException>(() =>
            waiter.SaveWaiting(null, attempts: 1))))
        { IsBackground = true };

        holderThread.Start();
        Assert.True(holding.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken), "the holder never reached its wait");

        waiterThread.Start();
        Thread.Sleep(_hold);
        release.Set();

        Assert.True(holderThread.Join(TimeSpan.FromSeconds(30)) && waiterThread.Join(TimeSpan.FromSeconds(30)));

        Assert.Equal(held.Operation, waited.HeldBy);
        Assert.True(
            waited.GateWait >= _hold / 2,
            $"a save held for {_hold.TotalMilliseconds} ms reported {SaveTimings.Describe(waited)}");
        Assert.True(
            waited.GateWait.TotalMilliseconds > 10 * SaveTimings.Milliseconds(waited.Attempts),
            $"the held save's own work was not small beside its gate wait: {SaveTimings.Describe(waited)}");
        AssertComponentsFitTheTotal(waited);
    }

    [Fact]
    public void ASaveDoingRealWorkAlone_IsReportedAsWork_NotGateWait()
    {
        var path = Path.Combine(_directory, "work.kdbx");
        using var vault = Vault.Create(path, VaultSaveTests.MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "T", Password = "p" });

        var timing = SaveTimings.Of(vault.Save);

        Assert.True(timing.Succeeded);
        Assert.Equal(0, timing.HeldBy);
        Assert.True(
            SaveTimings.Milliseconds(timing.Attempts) > 10 * timing.GateWait.TotalMilliseconds,
            $"key derivation and encryption did not dominate an uncontended gate: {SaveTimings.Describe(timing)}");
        AssertComponentsFitTheTotal(timing);
    }

    private static void AssertComponentsFitTheTotal(SaveTiming timing)
    {
        var parts = (timing.Check ?? TimeSpan.Zero) + timing.Redirect + timing.GateWait + (timing.Stamp ?? TimeSpan.Zero)
            + TimeSpan.FromMilliseconds(
                SaveTimings.Milliseconds(timing.Attempts)
                + SaveTimings.Milliseconds(timing.Waits)
                + SaveTimings.Milliseconds(timing.Rereads));

        Assert.True(parts <= timing.Total, $"the components exceed the whole: {SaveTimings.Describe(timing)}");
    }

    private Vault DoomedVault(string name)
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        var vault = Vault.Create(Path.Combine(home, "vault.kdbx"), VaultSaveTests.MasterPassword);
        vault.Save();
        Directory.Delete(home, recursive: true);
        return vault;
    }
}

[CollectionDefinition(nameof(SavesTimedAlone), DisableParallelization = true)]
public sealed class SavesTimedAlone;
