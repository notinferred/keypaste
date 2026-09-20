using System.Diagnostics;
using System.Globalization;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What keypaste keeps of a vault before it replaces one, and what it refuses to do when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// Two claims carry the rest. <b>A backup holds the bytes the save replaced</b>, opening with the
/// password they were written under and reading back the values that were current before — or it is
/// a file of the right size and no use to anybody. And <b>a save that cannot take one does not
/// happen</b>, leaving the vault byte-identical, because a backup that quietly stops being taken is
/// worse than none: it is none, believed in.
/// </para>
/// <para>
/// The third claim has no natural home but matters as much: <b>nothing here may cost the last good
/// backup</b>. Retention runs only after a new copy is complete and named, so a full disk drops a
/// save rather than the copy that would have survived it.
/// </para>
/// <para>
/// Failures are arranged by putting a regular file where the backup directory has to go, rather than
/// by locking one. <c>VaultSaveTests</c> gives the reason at length: <c>FileShare.None</c> is
/// advisory on Linux and macOS, so a test built on it asserts the real behaviour on Windows and
/// nothing at all elsewhere. A file in the directory's place fails <c>Directory.CreateDirectory</c>
/// identically everywhere, through the same path a full disk takes.
/// </para>
/// </remarks>
[Collection(nameof(SavesThatSpawnProcesses))]
public sealed class VaultBackupTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private readonly string _directory;

    public VaultBackupTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-backup-tests-").FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            // A test that made a directory read-only to arrange its failure.
            Relax(_directory);
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ------------------------------------------------------------------ what is kept

    [Fact]
    public void ASaveOverAnExistingVault_KeepsTheValuesItReplaced()
    {
        var path = SeededVault("before");

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "after");
            vault.Save();
        }

        var backup = Assert.Single(VaultBackups.List(path));

        using var kept = Vault.Open(backup.Path, MasterPassword);
        Assert.Equal("before", Read(kept));

        using var live = Vault.Open(path, MasterPassword);
        Assert.Equal("after", Read(live));
    }

    [Fact]
    public void AFirstCreation_KeepsNothing()
    {
        // There is nothing to preserve, and EnterGateIfTransacting already returns false on exactly
        // this condition, so "first creation is separate" is the save gate's own rule reused rather
        // than a second one that could drift from it.
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory, "new")).FullName, "v.kdbx");

        using var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "t", Password = "before", GroupPath = "env/p" });
        vault.Save();

        Assert.Empty(VaultBackups.List(path));
        Assert.False(Directory.Exists(VaultBackups.DirectoryFor(path)));
    }

    [Fact]
    public void ABackupTakesTheVaultsOwnExtension()
    {
        // A byte copy must not restate the file's format: nothing here decrypted it, and List
        // matches a copy to its vault by the extension it kept.
        var path = SeededVault("before", name: "personal.kdb");

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "after");
            vault.Save();
        }

        var backup = Assert.Single(VaultBackups.List(path));

        Assert.EndsWith(".kdb", backup.Path, StringComparison.Ordinal);
        Assert.False(backup.Path.EndsWith(".kdbx", StringComparison.Ordinal));
        Assert.EndsWith("personal.kdb.backups", VaultBackups.DirectoryFor(path), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoVaultsDifferingOnlyByExtension_DoNotShareADirectory()
    {
        // Keying on the stem would give these one directory and identical names inside it, so
        // retention would prune across both vaults.
        var home = Directory.CreateDirectory(Path.Combine(_directory, "both")).FullName;

        Assert.NotEqual(
            VaultBackups.DirectoryFor(Path.Combine(home, "personal.kdbx")),
            VaultBackups.DirectoryFor(Path.Combine(home, "personal.kdb")));
    }

    [Fact]
    public void ABackupsNameCarriesNoPartOfTheUnlockSecret()
    {
        var path = SeededVault("before");

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "after");
            vault.Save();
        }

        var backup = Assert.Single(VaultBackups.List(path));
        var name = Path.GetFileName(backup.Path) + Path.GetFileName(VaultBackups.DirectoryFor(path));

        foreach (var word in MasterPassword.Split(' '))
        {
            Assert.DoesNotContain(word, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ABackupIsReadableByItsOwnerAlone()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(_noUnixMode);
            return;
        }

        var path = SeededVault("before");

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "after");
            vault.Save();
        }

        var backup = Assert.Single(VaultBackups.List(path));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(backup.Path));
    }

    // ------------------------------------------------------------------ how often

    [Fact]
    public void ASecondSaveInOneUnlock_KeepsNothingMore()
    {
        var path = SeededVault("before");

        using var vault = Vault.Open(path, MasterPassword);

        Replace(vault, "second");
        vault.Save();
        Replace(vault, "third");
        vault.Save();

        var backup = Assert.Single(VaultBackups.List(path));

        using var kept = Vault.Open(backup.Path, MasterPassword);
        Assert.Equal("before", Read(kept));
    }

    [Fact]
    public void AnotherUnlockInsideTheFloor_KeepsNothingMore()
    {
        // Each CLI command is its own unlock, so without the floor a six-command script would evict
        // the whole window in seconds and leave five backups all from the same minute.
        var path = SeededVault("before");

        Edit(path, "second");
        Edit(path, "third");
        Edit(path, "fourth");

        var backup = Assert.Single(VaultBackups.List(path));

        using var kept = Vault.Open(backup.Path, MasterPassword);
        Assert.Equal("before", Read(kept));
    }

    [Fact]
    public void AnotherUnlockOutsideTheFloor_KeepsAnother()
    {
        var path = SeededVault("before");

        Edit(path, "second");
        Age(path, TimeSpan.FromHours(1));
        Edit(path, "third");

        var backups = VaultBackups.List(path);
        Assert.Equal(2, backups.Count);

        using var newest = Vault.Open(backups[0].Path, MasterPassword);
        Assert.Equal("second", Read(newest));
    }

    [Fact]
    public void ABackupStampedInTheFuture_CountsAsDue()
    {
        // now - newest goes negative when a clock moves back or a vault folder arrives from a
        // machine ahead of this one, and that is less than any floor. Read as "recent" it would
        // suppress every backup for as long as the file sat there, which is the failure this
        // feature cannot notice on its own.
        var path = SeededVault("before");

        Edit(path, "second");
        Age(path, -TimeSpan.FromDays(3));

        Edit(path, "third");

        Assert.Equal(2, VaultBackups.List(path).Count);
    }

    // ------------------------------------------------------------------ retention

    [Fact]
    public void RetentionKeepsTheNewest_AndDropsTheOldest()
    {
        var path = SeededVault("before");

        for (var edit = 1; edit <= VaultBackups.Retained + 1; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        var backups = VaultBackups.List(path);
        Assert.Equal(VaultBackups.Retained, backups.Count);

        using var newest = Vault.Open(backups[0].Path, MasterPassword);
        Assert.Equal($"edit-{VaultBackups.Retained}", Read(newest));

        // The first save's copy — the one holding "before" — is the one that went.
        foreach (var backup in backups)
        {
            using var kept = Vault.Open(backup.Path, MasterPassword);
            Assert.NotEqual("before", Read(kept));
        }
    }

    [Fact]
    public void AFailedBackup_PrunesNothing()
    {
        // Retention runs only after a new copy is complete and named, so the copy that would have
        // survived a bad day is never what pays for one.
        var path = SeededVault("before");

        for (var edit = 1; edit <= VaultBackups.Retained; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        Assert.Equal(VaultBackups.Retained, VaultBackups.List(path).Count);

        var kept = Names(path);
        Block(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "refused");
            Assert.Throws<VaultBackupException>(vault.Save);
        }

        Unblock(path);
        Assert.Equal(kept, Names(path));
    }

    // ------------------------------------------------------------------ when it cannot

    [Fact]
    public void AVaultThatCannotBeBackedUp_IsNotSaved()
    {
        var path = SeededVault("before");
        var bytes = File.ReadAllBytes(path);

        Block(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "after");
            Assert.Throws<VaultBackupException>(vault.Save);
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));

        Unblock(path);
        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal("before", Read(reopened));
    }

    [Fact]
    public void AReadOnlyBackupDirectory_RefusesTheSave()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(_noUnixMode);
            return;
        }

        var path = SeededVault("before");
        Edit(path, "second");
        Age(path, TimeSpan.FromHours(1));

        var directory = VaultBackups.DirectoryFor(path);
        var bytes = File.ReadAllBytes(path);

        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            using var vault = Vault.Open(path, MasterPassword);
            Replace(vault, "refused");
            Assert.Throws<VaultBackupException>(vault.Save);
        }
        finally
        {
            Relax(directory);
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ABackupFailure_IsNotRetried()
    {
        // KeePassInterop.IsTransient admits IOException and UnauthorizedAccessException, which is
        // what a broken backup destination raises. Arriving as one of those would spend the whole
        // eight-attempt budget — about 2.2 seconds of sleeping — before saying the disk is full.
        // VaultBackupException is outside that classification, and this is what says so: the budget
        // is never touched.
        var path = SeededVault("before");
        var waits = 0;

        Block(path);

        using var vault = Vault.Open(path, MasterPassword);
        Replace(vault, "after");

        Assert.Throws<VaultBackupException>(
            () => vault.SaveWaiting(_ => waits++, KeePassInterop.SaveAttempts));

        Assert.Equal(0, waits);
    }

    [Fact]
    public void AnExternalWriter_IsRefusedBeforeAnythingIsCopied()
    {
        // The re-read is what makes a backup "the last readable bytes": it has just established that
        // what is on disk is what this vault was opened from. A file changed underneath us fails
        // there, so nothing is copied and no backup of somebody else's save is kept.
        var path = SeededVault("before");

        using var vault = Vault.Open(path, MasterPassword);
        Replace(vault, "mine");

        using (var other = Vault.Open(path, MasterPassword))
        {
            Replace(other, "theirs");
            other.Save();
        }

        // The other save took its own backup; this one must add nothing.
        var already = Names(path);

        Assert.Throws<VaultChangedOnDiskException>(vault.Save);
        Assert.Equal(already, Names(path));
    }

    // ------------------------------------------------------------------ the overwriting save

    [Fact]
    public void SaveOverwriting_KeepsWhatItReplaces_EvenInsideTheFloor()
    {
        // This discards somebody else's write, and the vault it replaces is exactly the copy somebody
        // needs when that turns out to have been the wrong choice. Neither the per-unlock rule nor
        // the floor may suppress it. Restoring a backup is VaultRestoreTests' subject.
        var path = SeededVault("before");

        using var vault = Vault.Open(path, MasterPassword);

        Replace(vault, "second");
        vault.Save();

        Replace(vault, "restored");
        vault.SaveOverwriting();

        var backups = VaultBackups.List(path);
        Assert.Equal(2, backups.Count);

        using var newest = Vault.Open(backups[0].Path, MasterPassword);
        Assert.Equal("second", Read(newest));
    }

    [Fact]
    public void SaveOverwriting_DoesNotSpendTheUnlocksOwnBackup()
    {
        var path = SeededVault("before");

        using var vault = Vault.Open(path, MasterPassword);

        Replace(vault, "restored");
        vault.SaveOverwriting();

        Age(path, TimeSpan.FromHours(1));

        Replace(vault, "edited");
        vault.Save();

        Assert.Equal(2, VaultBackups.List(path).Count);
    }

    // ------------------------------------------------------------------ interrupted

    [Fact]
    public void ASaveKilledAtTheBackupBoundary_LeavesTheVaultAndACompleteBackup()
    {
        // The boundary needs no product knob to find: the backup file appearing beside the vault is
        // the instant between "the bytes are preserved" and "the vault is replaced".
        var path = SeededVault("before");
        var bytes = File.ReadAllBytes(path);

        var info = new ProcessStartInfo
        {
            FileName = Helper("Keypaste.VaultSaver"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        info.ArgumentList.Add(path);
        info.ArgumentList.Add("1");
        info.Environment["KEYPASTE_SAVER_PASSWORD"] = MasterPassword;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("the saver did not start");

        var waited = Stopwatch.StartNew();
        var killed = false;

        while (waited.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (VaultBackups.List(path).Count > 0)
            {
                process.Kill(entireProcessTree: true);
                killed = true;
                break;
            }

            if (process.HasExited)
            {
                break;
            }

            Thread.Sleep(1);
        }

        process.WaitForExit(TimeSpan.FromSeconds(30));

        var backup = Assert.Single(VaultBackups.List(path));

        using (var kept = Vault.Open(backup.Path, MasterPassword))
        {
            Assert.Equal("before", Read(kept));
        }

        if (!killed)
        {
            // The save outran the poll. The backup above is still the claim; the vault has moved on.
            return;
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));

        using var live = Vault.Open(path, MasterPassword);
        Assert.Equal("before", Read(live));
    }

    // ------------------------------------------------------------------ reading them back

    [Fact]
    public void AFileNobodyHereNamed_IsNeitherListedNorPruned()
    {
        // Pruning a file this type did not write is not ours to do, however much it looks like one.
        var path = SeededVault("before");

        for (var edit = 1; edit <= VaultBackups.Retained + 1; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        var theirs = Path.Combine(VaultBackups.DirectoryFor(path), "my own copy.kdbx");
        File.WriteAllText(theirs, "not a vault");

        Edit(path, "again");

        Assert.True(File.Exists(theirs));
        Assert.DoesNotContain(VaultBackups.List(path), backup => backup.Path == theirs);
    }

    /// <remarks>
    /// Planted rather than saved twice. Two saves land in the same second only when the key
    /// derivations either side of them happen to be quick enough, so a test that made them would
    /// assert this rule on some runs and nothing on others — and it is exactly the rule a
    /// whole-name sort gets backwards, because '-' sorts before the '.' that starts the first
    /// one's extension.
    /// </remarks>
    [Fact]
    public void TwoBackupsInOneSecond_AreOrderedByTheCounter_NotByTheName()
    {
        var path = SeededVault("before");
        var directory = Directory.CreateDirectory(VaultBackups.DirectoryFor(path)).FullName;

        var stem = Path.GetFileNameWithoutExtension(path);
        var first = Path.Combine(directory, $"{stem}.20260920T120000Z.kdbx");
        var second = Path.Combine(directory, $"{stem}.20260920T120000Z-2.kdbx");

        File.Copy(path, first);
        File.Copy(path, second);

        var backups = VaultBackups.List(path);

        Assert.Equal(2, backups.Count);
        Assert.Equal(second, backups[0].Path);
        Assert.Equal(first, backups[1].Path);
    }

    [Fact]
    public void ListReportsNewestFirst()
    {
        var path = SeededVault("before");

        for (var edit = 1; edit <= 3; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        var backups = VaultBackups.List(path);

        Assert.Equal(3, backups.Count);
        Assert.True(backups[0].TakenAt > backups[1].TakenAt);
        Assert.True(backups[1].TakenAt > backups[2].TakenAt);
    }

    [Fact]
    public void TheSaveThatCreatesTheDirectory_IsTheOneThatReportsIt()
    {
        var path = SeededVault("before");

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "second");
            vault.Save();

            var report = Assert.IsType<VaultBackupReport>(vault.LastBackup);
            Assert.True(report.CreatedDirectory);
            Assert.Equal(VaultBackupOutcome.Written, report.Outcome);
            Assert.Equal(VaultBackups.Retained, report.Retained);
        }

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Replace(vault, "third");
            vault.Save();

            var report = Assert.IsType<VaultBackupReport>(vault.LastBackup);
            Assert.False(report.CreatedDirectory);
            Assert.Equal(VaultBackupOutcome.SkippedRecent, report.Outcome);
        }
    }

    // ------------------------------------------------------------------ fixtures

    private const string _noUnixMode =
        "Windows has no owner-only file mode, so there is no permission here to set or to refuse.";

    private string SeededVault(string password, string name = "vault.kdbx")
    {
        var home = Directory.CreateDirectory(
            Path.Combine(_directory, Path.GetRandomFileName())).FullName;
        var path = Path.Combine(home, name);

        using var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = password, GroupPath = "env/p" });

        // FileTransactionEx transacts only over an existing file, and the gate — and so the backup —
        // follows that same condition.
        vault.Save();

        return path;
    }

    /// <summary>One edit through its own unlock, the way a CLI command makes one.</summary>
    private static void Edit(string path, string password)
    {
        using var vault = Vault.Open(path, MasterPassword);
        Replace(vault, password);
        vault.Save();
    }

    private static void Replace(Vault vault, string password) =>
        vault.UpdateEntry(new VaultEntry
        {
            Title = "TOKEN",
            Password = password,
            GroupPath = "env/p",
        });

    private static string Read(Vault vault) =>
        vault.Find("env/p/TOKEN")?.Password ?? throw new InvalidOperationException("no entry");

    /// <summary>Moves every backup's stamp, so the floor sees what a later clock would.</summary>
    /// <remarks>
    /// The stamp lives in the name, so this is a rename and not a touch — which is the point of
    /// keeping it there: a modification time does not survive a copy, a restore or a folder sync.
    /// </remarks>
    private static void Age(string vaultPath, TimeSpan by)
    {
        var stem = Path.GetFileNameWithoutExtension(vaultPath) + ".";
        var extension = Path.GetExtension(vaultPath);

        // Every backup moves by the same amount, so each one's new name is the one its neighbour is
        // vacating. Moving them towards the vacancy — oldest first when shifting back, newest first
        // when shifting forward — is what keeps the renames from colliding. List is newest first.
        var order = VaultBackups.List(vaultPath);
        var moving = by > TimeSpan.Zero ? order.Reverse() : order;

        foreach (var backup in moving)
        {
            var stamp = (backup.TakenAt - by)
                .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            File.Move(
                backup.Path,
                Path.Combine(Path.GetDirectoryName(backup.Path)!, stem + stamp + extension),
                overwrite: false);
        }
    }

    private static string[] Names(string vaultPath) =>
        [.. VaultBackups.List(vaultPath).Select(backup => Path.GetFileName(backup.Path)).Order(StringComparer.Ordinal)];

    /// <summary>Puts a regular file where the backup directory has to go.</summary>
    private static void Block(string vaultPath)
    {
        var directory = VaultBackups.DirectoryFor(vaultPath);

        if (Directory.Exists(directory))
        {
            Directory.Move(directory, directory + ".moved");
        }

        File.WriteAllText(directory, "not a directory");
    }

    private static void Unblock(string vaultPath)
    {
        var directory = VaultBackups.DirectoryFor(vaultPath);
        File.Delete(directory);

        if (Directory.Exists(directory + ".moved"))
        {
            Directory.Move(directory + ".moved", directory);
        }
    }

    private static void Relax(string directory)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return;
        }

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        foreach (var child in Directory.GetDirectories(directory))
        {
            Relax(child);
        }
    }

    private static string Helper(string name)
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    "Could not locate keypaste.slnx above " + AppContext.BaseDirectory);
            }

            directory = parent;
        }

        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase)
            ? "debug"
            : "release";

        return Path.Combine(
            directory, "artifacts", "bin", name, configuration,
            OperatingSystem.IsWindows() ? name + ".exe" : name);
    }
}
