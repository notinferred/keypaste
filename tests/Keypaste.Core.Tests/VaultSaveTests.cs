using System.Diagnostics;
using System.Security.Cryptography;
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
/// <see cref="KeePassInterop.SaveAttempts"/> is what absorbs it.
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
    /// <summary>Creates a symbolic link, or skips where this machine cannot make one, as Windows without Developer Mode cannot.</summary>
    internal static string Link(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create symbolic links: {ex.Message}");
        }

        return path;
    }

    private (Vault Vault, string Home) NewVaultInItsOwnDirectory(string name)
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        var vault = ClaimedVault.Create(Path.Combine(home, "vault.kdbx"), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "T", Password = "p", GroupPath = "env/dev" });
        vault.Save();
        return (vault, home);
    }

    [Fact]
    public void A_vault_whose_claim_was_released_saves_nothing()
    {
        var path = Path.Combine(_directory, "released.kdbx");
        var claim = ClaimedVault.Claim(path);
        using var vault = Vault.Create(claim, path, MasterPassword);
        vault.Save();
        var saved = File.ReadAllBytes(path);

        claim.Dispose();
        vault.AddEntry(new VaultEntry { Title = "after", Password = "p" });

        Assert.Throws<VaultException>(vault.Save);
        Assert.Throws<VaultException>(vault.SaveOverwriting);
        Assert.Equal(saved, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_vault_opens_for_writing_only_under_a_held_claim_on_it()
    {
        var (created, home) = NewVaultInItsOwnDirectory("claims");
        created.Dispose();
        var path = Path.Combine(home, "vault.kdbx");
        using var other = ClaimedVault.Claim(Path.Combine(home, "other.kdbx"));
        var released = ClaimedVault.Claim(path);
        released.Dispose();

        Assert.Throws<ArgumentException>(() => Vault.Open(other, path, MasterPassword));
        Assert.Throws<ArgumentException>(() => Vault.Open(released, path, MasterPassword));
        Assert.Throws<ArgumentException>(() => Vault.Create(other, Path.Combine(home, "new.kdbx"), MasterPassword));
    }

    [Fact]
    public void A_vault_opened_read_only_saves_nothing()
    {
        var (created, home) = NewVaultInItsOwnDirectory("read-only");
        created.Dispose();
        var path = Path.Combine(home, "vault.kdbx");
        var saved = File.ReadAllBytes(path);

        using var vault = Vault.OpenReadOnly(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "after", Password = "p" });

        Assert.Throws<InvalidOperationException>(vault.Save);
        Assert.Throws<InvalidOperationException>(() => vault.MergeConflictedCopy(path, save: true));
        Assert.Equal(saved, File.ReadAllBytes(path));
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

    /// <summary>A retry writes the bytes the save already produced, deriving no second key.</summary>
    [Fact]
    public void A_retry_writes_the_bytes_the_save_already_produced()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("produced-once");
        using var _ = vault;
        vault.AddEntry(new VaultEntry { Title = "New", Password = "p" });
        var produced = 0;
        vault.AfterSerializing = bytes =>
        {
            produced++;
            return bytes;
        };

        vault.SaveWaiting(null, duringAttempt: attempt =>
        {
            if (attempt == 1)
            {
                throw new IOException("another program holds the vault file");
            }
        });

        Assert.Equal(1, produced);
        using var reopened = ClaimedVault.Open(Path.Combine(home, "vault.kdbx"), MasterPassword);
        Assert.Contains(reopened.ReadEntries(), entry => entry.Title == "New");
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

        // A fixed floor, not derived from SaveAttempts and SaveRetryDelayMilliseconds, which would
        // follow them down to zero; 50 ms is under the 75 ms TheRetryHasRoomToAbsorbATransientFailure's
        // minimums imply and far above one attempt's ~0 ms.
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
    /// Retrying must not turn a permanent failure into a hang. The retry sleeps are about 2.2
    /// seconds, but the caller also waits for the checks, the gate and every attempt's work
    /// so the bound is on the whole wait and the failure names each part.
    /// </summary>
    [Fact]
    public void ASaveThatCannotSucceed_GivesUpQuickly()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("give-up");
        using var _ = vault;

        Directory.Delete(home, recursive: true);

        var started = Environment.TickCount64;
        var timing = SaveTimings.Of(() => Assert.Throws<VaultException>(vault.Save));
        var elapsed = Environment.TickCount64 - started;

        Assert.True(
            elapsed < 5_000,
            $"a doomed save took {elapsed}ms; retrying must stay bounded. {SaveTimings.Describe(timing)}");
    }

    /// <summary>
    /// Where a doomed save's time actually goes: waiting between attempts, or inside them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two readings rather than one, on purpose.</b>
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

        var lastResumed = Stopwatch.GetTimestamp();

        var timing = SaveTimings.Of(() => Assert.Throws<VaultException>(() => vault.SaveWaiting(attempt =>
        {
            attempts = attempt;
            work.Add((long)Stopwatch.GetElapsedTime(lastResumed).TotalMilliseconds);

            var sleeping = Stopwatch.GetTimestamp();
            Thread.Sleep(KeePassInterop.SaveRetryDelayMilliseconds * attempt);
            slept.Add((long)Stopwatch.GetElapsedTime(sleeping).TotalMilliseconds);

            lastResumed = Stopwatch.GetTimestamp();
        })));

        // The budget is the sleeps. Their sum is arithmetic on two shipped constants and cannot
        // drift with the machine; the work is what the machine decides, and it is the reading this
        // test exists to produce.
        var budget = Enumerable.Range(1, attempts).Sum(attempt => KeePassInterop.SaveRetryDelayMilliseconds * attempt);

        Assert.Equal(KeePassInterop.SaveAttempts - 1, attempts);

        if (slept.Sum() > budget * 3)
        {
            Assert.Fail(
                $"the sleeps alone took {slept.Sum()}ms against {budget}ms asked for, over {attempts} attempts; " +
                $"the work between them took {work.Sum()}ms. {SaveTimings.Describe(timing)}");
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

    /// <summary>A save that works still works, and the file is readable afterwards.</summary>
    /// <summary>
    /// A write that fails partway through a save is reported, and the vault stays as it was.
    /// </summary>
    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
    public void A_save_keeps_the_permissions_the_vault_file_had(UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps a file's access list through its own replace.");
            return;
        }

        var (vault, home) = NewVaultInItsOwnDirectory("permissions");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        File.SetUnixFileMode(path, mode);
        vault.AddEntry(new VaultEntry { Title = "New", Password = "p" });

        vault.Save();

        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    [Fact]
    public void A_save_whose_bytes_do_not_open_again_is_refused_and_leaves_the_vault_as_it_was()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("unreadable");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        var before = File.ReadAllBytes(path);
        vault.AddEntry(new VaultEntry { Title = "New", Password = "p" });
        vault.AfterSerializing = bytes =>
        {
            bytes[bytes.Length / 2] ^= 1;
            return bytes;
        };

        var failure = Record.Exception(vault.Save);

        Assert.IsType<VaultException>(failure);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_save_through_a_symbolic_link_replaces_the_file_it_points_to_and_keeps_the_link()
    {
        var (created, home) = NewVaultInItsOwnDirectory("linked");
        created.Dispose();
        var target = Path.Combine(home, "vault.kdbx");
        var link = Link(Path.Combine(_directory, "linked.kdbx"), target);

        using (var vault = ClaimedVault.Open(link, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "New", Password = "p" });
            vault.Save();
        }

        Assert.Equal(target, new FileInfo(link).ResolveLinkTarget(returnFinalTarget: true)?.FullName);
        Assert.Empty(Directory.GetFiles(home, "*.tmp"));
        using var reopened = ClaimedVault.Open(target, MasterPassword);
        Assert.Contains(reopened.ReadEntries(), entry => entry.Title == "New");
    }

    [Fact]
    public void A_save_through_a_symbolic_link_that_is_refused_leaves_the_file_it_points_to_as_it_was()
    {
        var (created, home) = NewVaultInItsOwnDirectory("linked-refused");
        created.Dispose();
        var target = Path.Combine(home, "vault.kdbx");
        var link = Link(Path.Combine(_directory, "linked-refused.kdbx"), target);
        var before = File.ReadAllBytes(target);
        using var vault = ClaimedVault.Open(link, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "New", Password = "p" });
        vault.AfterSerializing = bytes =>
        {
            bytes[bytes.Length / 2] ^= 1;
            return bytes;
        };

        Assert.IsType<VaultException>(Record.Exception(vault.Save));

        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void A_save_that_loses_an_item_nobody_deleted_is_refused_and_leaves_the_vault_as_it_was()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("loses");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        var withoutTheSecond = File.ReadAllBytes(path);
        vault.AddEntry(new VaultEntry { Title = "Second", Password = "p" });
        vault.Save();
        var before = File.ReadAllBytes(path);
        vault.AddEntry(new VaultEntry { Title = "Third", Password = "p" });
        vault.AfterSerializing = _ => [.. withoutTheSecond];

        var failure = Record.Exception(vault.Save);

        Assert.IsType<VaultException>(failure);
        Assert.Contains("lose 1 item", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_save_whose_bytes_hold_other_values_than_the_open_vault_is_refused_and_leaves_the_vault_as_it_was()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("unfaithful");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        var earlier = File.ReadAllBytes(path);
        vault.UpdateEntry(new VaultEntry { Title = "T", Password = "changed", GroupPath = "env/dev" });
        vault.Save();
        var before = File.ReadAllBytes(path);
        vault.UpdateEntry(new VaultEntry { Title = "T", Password = "changed again", GroupPath = "env/dev" });
        vault.AfterSerializing = _ => [.. earlier];

        var failure = Record.Exception(vault.Save);

        Assert.IsType<VaultException>(failure);
        Assert.Contains("not the open vault", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Deleting_an_item_for_good_records_it_and_saves()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("purge");
        var path = Path.Combine(home, "vault.kdbx");

        using (vault)
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(EntryName.Parse("env/dev/T"), out var recycled));
            vault.Save();
            Assert.True(vault.PurgeRecycled(recycled));
            vault.Save();
        }

        using var reopened = ClaimedVault.Open(path, MasterPassword);
        Assert.Empty(reopened.ReadEntries());
    }

    /// <remarks>
    /// A pipe at the temporary file's name stands in for a disk that fills during the save: each
    /// attempt's reader takes the first kilobyte and goes away, so the rest of the write fails, and
    /// the save removes what it could not finish, so each attempt makes the pipe again.
    /// KeePassLib writes a vault under a megabyte while closing its streams and discards what that
    /// throws, so a save through it reported success and moved the pipe over the vault.
    /// </remarks>
    [Fact]
    public void A_write_that_fails_partway_is_reported_and_leaves_the_vault_as_it_was()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no named pipe at a file path.");
        }

        var (vault, home) = NewVaultInItsOwnDirectory("partway");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        var before = File.ReadAllBytes(path);

        // Random, so the file outgrows what a pipe holds and stays under KeePassLib's one-megabyte block.
        vault.AddEntry(new VaultEntry { Title = "Large", Password = "p", Notes = Convert.ToBase64String(RandomNumberGenerator.GetBytes(300_000)) });

        var temporary = path + ".tmp";
        List<Thread> readers = [];
        var failure = Record.Exception(() => vault.SaveWaiting(_ => { }, duringAttempt: _ =>
        {
            using (var mkfifo = Process.Start("mkfifo", [temporary]))
            {
                mkfifo.WaitForExit();
                Assert.Equal(0, mkfifo.ExitCode);
            }

            readers.Add(ReadTheFirstKilobyte(temporary));
        }));

        Assert.All(readers, reader => Assert.True(reader.Join(TimeSpan.FromSeconds(40)), "a reader was left waiting for a write"));
        Assert.Equal(before.Length, new FileInfo(path).Length);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.IsType<VaultException>(failure);
        Assert.False(File.Exists(temporary));
    }

    private static Thread ReadTheFirstKilobyte(string pipe)
    {
        var reader = new Thread(() =>
        {
            using var head = Process.Start(new ProcessStartInfo("head") { ArgumentList = { "-c", "1024", pipe }, RedirectStandardOutput = true })
                ?? throw new InvalidOperationException("head did not start");

            if (!head.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                head.Kill();
            }
        })
        { IsBackground = true };

        reader.Start();
        return reader;
    }

    [Fact]
    public void AnOrdinarySaveIsUnaffected()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("ordinary");

        using (vault)
        {
            vault.Save();
        }

        using var reopened = ClaimedVault.Open(Path.Combine(home, "vault.kdbx"), MasterPassword);
        Assert.Equal("p", reopened.Find("env/dev/T")?.Password, StringComparer.Ordinal);
    }

    /// <summary>A temporary a crash left hard-linked to the vault is unlinked, never written through, so the vault is replaced whole.</summary>
    [Fact]
    public void A_temporary_hard_linked_to_the_vault_leaves_the_vault_intact_and_the_save_succeeds()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("The no-overwrite move that could leave a hard link is Unix's.");
            return;
        }

        var (vault, home) = NewVaultInItsOwnDirectory("linked");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        var before = File.ReadAllBytes(path);
        using (var ln = Process.Start("ln", [path, path + KeePassInterop.StrandedTemporarySuffix]))
        {
            ln.WaitForExit();
            Assert.Equal(0, ln.ExitCode);
        }

        vault.AddEntry(new VaultEntry { Title = "After", Password = "p" });
        vault.WhileWriting = _ => Assert.Equal(before, File.ReadAllBytes(path));
        vault.Save();

        using var reopened = ClaimedVault.Open(path, MasterPassword);
        Assert.NotNull(reopened.Find("After"));
        Assert.False(File.Exists(path + KeePassInterop.StrandedTemporarySuffix));
    }

    /// <summary>A full disk is not waited out: the save fails on its first attempt, names the cause, and leaves no half-written file beside the vault.</summary>
    [Fact]
    public void A_full_disk_fails_at_once_naming_it_and_leaves_no_temporary_file()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("full");
        using var _ = vault;
        var path = Path.Combine(home, "vault.kdbx");
        var before = File.ReadAllBytes(path);
        var attempts = 0;
        string? written = null;
        vault.AddEntry(new VaultEntry { Title = "New", Password = "p" });
        vault.WhileWriting = file =>
        {
            attempts++;
            written = file;
            throw new IOException("No space left on device", OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28);
        };

        var error = Assert.Throws<VaultException>(vault.Save);

        Assert.Equal(1, attempts);
        Assert.Equal($"Could not save '{path}': No space left on device", error.Message);
        Assert.False(File.Exists(written));
        Assert.Empty(Directory.GetFiles(home, "*.tmp"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>What the system raises for a write it has no space for is read as a full disk.</summary>
    [Fact]
    public void A_write_refused_for_space_is_a_full_disk()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Only Linux has /dev/full, which refuses every write for space.");
        }

        var error = Assert.ThrowsAny<IOException>(() =>
        {
            using var full = new FileStream("/dev/full", FileMode.Open, FileAccess.Write);
            full.Write(new byte[1]);
            full.Flush();
        });

        Assert.True(KeePassInterop.IsFullDisk(error), $"{error.GetType().Name} 0x{error.HResult:X8}: {error.Message}");
    }

    /// <summary>A save whose folder the system will not flush is still saved, and says so once.</summary>
    [Fact]
    public void A_save_whose_folder_is_not_flushed_says_so_once()
    {
        var (vault, home) = NewVaultInItsOwnDirectory("unflushed");
        using var _ = vault;
        vault.AddEntry(new VaultEntry { Title = "First", Password = "p" });

        using (UnflushableFolder.Stage(home))
        {
            vault.Save();
        }

        Assert.True(vault.TakeUnflushedSave());
        Assert.False(vault.TakeUnflushedSave());
        using (var reopened = ClaimedVault.Open(Path.Combine(home, "vault.kdbx"), MasterPassword))
        {
            Assert.NotNull(reopened.Find("First"));
        }

        vault.AddEntry(new VaultEntry { Title = "Second", Password = "p" });
        vault.Save();
        Assert.False(vault.TakeUnflushedSave());
    }
}
