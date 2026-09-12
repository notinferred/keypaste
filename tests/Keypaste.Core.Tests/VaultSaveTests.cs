using System.Diagnostics;
using System.Globalization;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What happens when writing the vault file goes wrong.
/// </summary>
/// <remarks>
/// <para>
/// Saving goes through a file transaction — write a temporary file, then replace the original —
/// and on Windows that replace competes with every process that watches the filesystem. Defender
/// and the search indexer open a newly written file in order to scan it, and for a few
/// milliseconds the replace cannot land, reporting <c>Access is denied</c>. It showed up as
/// intermittent "Could not save" failures across unrelated tests on CI, and
/// <see cref="KeePassInterop.SaveAttempts"/> is what absorbs it (DECISIONS.md D-0017).
/// </para>
/// <para>
/// The failure is simulated by removing the directory the vault lives in, rather than by locking
/// the file. A lock is what actually happens in the wild, but <c>FileShare.None</c> is advisory on
/// Linux and macOS — the save simply succeeds there — so a test built on it would assert the real
/// behaviour on one platform and nothing at all on the other two. A missing directory fails
/// everywhere, through the same retry path, for the same reason.
/// </para>
/// </remarks>
public sealed class VaultSaveTests : IDisposable
{
    internal const string MasterPassword = "save-tests-master-pw";

    private readonly string _directory;

    public VaultSaveTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-save-tests-").FullName;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Creates a saved vault inside its own subdirectory, and returns both.</summary>
    private (Vault Vault, string Home) NewVaultInItsOwnDirectory(string name)
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        var vault = Vault.Create(Path.Combine(home, "vault.kdbx"), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "T", Password = "p", GroupPath = "env/dev" });
        vault.Save();
        return (vault, home);
    }

    /// <summary>
    /// The point of the retry: a failure that goes away on its own must not reach the user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repair runs on a dedicated thread rather than on the thread pool. The first version of
    /// this test used <see cref="Task.Run(Action)"/> and failed on the Windows runner: with four
    /// cores already saturated by other test collections, the pool did not schedule the repair
    /// until after the retry window had closed. A test whose result depends on how busy the
    /// machine is tells you about the machine.
    /// </para>
    /// <para>
    /// The delay is a small fraction of the retry window, so the margin is wide in the direction
    /// that could flake, while the failing direction stays exact: at one attempt there is no
    /// window at all and this cannot pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATransientFailure_IsAbsorbed()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("transient");
        using var _ = vault;

        Directory.Delete(home, recursive: true);

        var repair = new Thread(() =>
        {
            Thread.Sleep(40);
            Directory.CreateDirectory(home);
        })
        { IsBackground = true };

        repair.Start();
        var failure = Record.Exception(vault.Save);
        repair.Join();

        Assert.True(failure is null, $"a failure that cured itself still reached the caller: {failure?.Message}");
        Assert.True(File.Exists(Path.Combine(home, "vault.kdbx")));
    }

    /// <summary>
    /// The retry is real, and not a loop that gives up on the first pass. Asserted on elapsed
    /// time, which only ever runs long, so this is exact in both directions: the backoff cannot
    /// take less than the sum of its sleeps, and a single attempt cannot take as long as one.
    /// </summary>
    [Fact]
    public void ADoomedSave_ActuallyRetriesBeforeGivingUp()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("retries");
        using var _ = vault;

        Directory.Delete(home, recursive: true);

        var started = Environment.TickCount64;
        Assert.Throws<VaultException>(vault.Save);
        var elapsed = Environment.TickCount64 - started;

        // A fixed floor, deliberately NOT computed from SaveAttempts and SaveRetryDelayMilliseconds.
        // Deriving the expectation from the values under test makes it follow them down to zero:
        // the first version of this line did exactly that and passed at one attempt, asserting
        // nothing. 50ms is comfortably under the 75ms that the minimums pinned by
        // TheRetryHasRoomToAbsorbATransientFailure already imply, and far above the ~0ms a single
        // attempt takes.
        Assert.True(
            elapsed >= 50,
            $"a doomed save returned in {elapsed}ms; it cannot have retried");
    }

    /// <summary>
    /// A save that cannot succeed still reports, and reports <em>why</em>. Without the cause the
    /// user gets "Could not save 'vault.kdbx'." and no way to tell a full disk from a file open in
    /// KeePassXC — and on CI it cost two round trips to learn what was actually failing.
    /// </summary>
    [Fact]
    public void ASaveThatCannotSucceed_ReportsTheReasonItFailed()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("doomed");
        using var _ = vault;

        Directory.Delete(home, recursive: true);

        var error = Assert.Throws<VaultException>(vault.Save);

        Assert.Contains("vault.kdbx", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);

        // Says more than the path: it carries whatever the operating system said.
        Assert.Contains(":", error.Message[error.Message.IndexOf("vault.kdbx", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Retrying must not turn a permanent failure into a hang. Four attempts with a linear backoff
    /// is well under a second, so a save that will never work still fails promptly.
    /// </summary>
    [Fact]
    public void ASaveThatCannotSucceed_GivesUpQuickly()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("give-up");
        using var _ = vault;

        Directory.Delete(home, recursive: true);

        using var watch = PoolSnapshot.Watch("a doomed save's whole retry budget");
        PoolTimeline.Mark("save-enter", "doomed");

        var started = Environment.TickCount64;
        Assert.Throws<VaultException>(vault.Save);
        var elapsed = Environment.TickCount64 - started;

        PoolTimeline.Mark("save-exit", elapsed.ToString(CultureInfo.InvariantCulture));

        // Reported only once the comparison has already failed: Assert.True evaluates its message
        // eagerly, and this one reads the pool.
        if (elapsed >= 5_000)
        {
            Assert.Fail($"a doomed save took {elapsed}ms; retrying must stay bounded.{Environment.NewLine}{watch.Report()}");
        }
    }

    /// <summary>
    /// Where a doomed save's time actually goes: waiting between attempts, or inside them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F.9's measurement, and it is two readings rather than one on purpose.</b>
    /// <see cref="ASaveThatCannotSucceed_GivesUpQuickly"/> overran at 6797 ms against a budget of
    /// about 2.2 seconds, and a single elapsed cannot say whether the retry was scheduled late or
    /// the work inside each attempt got slower. Those have different repairs, and one of them is not
    /// a repair at all: key derivation is CPU-parallel, so an arm that moved the processor count
    /// would slow the work and look like a scheduling defect.
    /// </para>
    /// <para>
    /// The wait callback is the existing seam <see cref="Vault.SaveWaiting"/> offers, invoked with
    /// the production delay inside it, so the attempt count and the budget are exactly what ships.
    /// Timestamps around the callback bracket each attempt, which is what separates the sleeping
    /// from the working without opening a production file to measure it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADoomedSave_SpendsItsBudgetWhereThisSays()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("budget");
        using var _ = vault;

        Directory.Delete(home, recursive: true);

        var work = new List<long>();
        var slept = new List<long>();
        var attempts = 0;

        using var watch = PoolSnapshot.Watch("a doomed save, attempt by attempt");
        PoolTimeline.Mark("save-enter", "budget");

        var lastResumed = Stopwatch.GetTimestamp();

        Assert.Throws<VaultException>(() => vault.SaveWaiting(attempt =>
        {
            attempts = attempt;
            work.Add((long)Stopwatch.GetElapsedTime(lastResumed).TotalMilliseconds);

            var sleeping = Stopwatch.GetTimestamp();
            Thread.Sleep(KeePassInterop.SaveRetryDelayMilliseconds * attempt);
            slept.Add((long)Stopwatch.GetElapsedTime(sleeping).TotalMilliseconds);

            lastResumed = Stopwatch.GetTimestamp();
        }));

        // Attempt by attempt rather than summed, because only the first work interval contains
        // acquiring the process-wide save gate. `work 4100/9/8/...` is a save that queued behind
        // another save in this process and `work 40/38/41/...` is one whose every attempt got
        // slower; those are different findings with different repairs and a sum cannot tell them
        // apart. Slash-separated: the detail is written into a JSON string field.
        PoolTimeline.Mark(
            "save-exit",
            string.Create(
                CultureInfo.InvariantCulture,
                $"attempts {attempts}; work {EachOf(work)}; slept {EachOf(slept)}"));

        // The budget is the sleeps. Their sum is arithmetic on two shipped constants and cannot
        // drift with the machine; the work is what the machine decides, and it is the reading this
        // test exists to produce.
        var budget = Enumerable.Range(1, attempts).Sum(attempt => KeePassInterop.SaveRetryDelayMilliseconds * attempt);

        Assert.Equal(KeePassInterop.SaveAttempts - 1, attempts);

        if (slept.Sum() > budget * 3)
        {
            Assert.Fail(
                $"the sleeps alone took {slept.Sum()}ms against {budget}ms asked for, over {attempts} attempts; " +
                $"the work between them took {work.Sum()}ms.{Environment.NewLine}{watch.Report()}");
        }
    }

    /// <summary>
    /// The retry is not allowed to become a no-op: dropped to one attempt, or to no delay, the
    /// transient window on Windows would stop being absorbed and the intermittent CI failures
    /// would come back with nothing noticing.
    /// </summary>
    [Fact]
    public void TheRetryHasRoomToAbsorbATransientFailure()
    {
        Assert.True(KeePassInterop.SaveAttempts >= 3, "one retry is not enough to ride out a scanner");
        Assert.True(KeePassInterop.SaveRetryDelayMilliseconds >= 25, "an immediate retry hits the same lock");
    }

    /// <summary>
    /// The measurements of one save, in the order they were taken, for the timeline's detail field.
    /// </summary>
    private static string EachOf(List<long> milliseconds) =>
        string.Join('/', milliseconds.Select(m => m.ToString(CultureInfo.InvariantCulture)));

    /// <summary>A save that works still works, and the file is readable afterwards.</summary>
    [Fact]
    public void AnOrdinarySaveIsUnaffected()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("ordinary");

        using (vault)
        {
            vault.Save();
        }

        using var reopened = Vault.Open(Path.Combine(home, "vault.kdbx"), MasterPassword);
        Assert.Equal("p", reopened.Find("env/dev/T")?.Password, StringComparer.Ordinal);
    }
}
