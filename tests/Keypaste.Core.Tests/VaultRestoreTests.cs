using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Putting a backup in the vault's place, and writing an encrypted copy of a vault somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// One claim carries the restore: <b>whatever goes wrong, the live file holds what it held</b>.
/// Every refusal here is asserted against the file's bytes, never against an exception alone, because
/// a restore that reports failure after replacing the vault is the defect this whole feature exists
/// to recover from.
/// </para>
/// <para>
/// The second is that <b>a restore only ever adds to what is recoverable</b>: the file it replaces is
/// kept, nothing is pruned, and a file already held byte for byte is not kept twice, so restoring
/// repeatedly cannot push a distinct copy out.
/// </para>
/// <para>
/// Every backup restored here was made by a real save, for <c>VaultBackupTests</c>' reason. The
/// exceptions are planted deliberately and say so: a backup under another password, which the core
/// has no call to produce yet, and the damaged ones.
/// </para>
/// </remarks>
[Collection(nameof(SavesThatSpawnProcesses))]
public sealed class VaultRestoreTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _earlier = "the password before this one";

    private const string _notWindows = "Only Windows refuses a rename onto a name another handle holds.";
    private const string _onlyUnix = "Windows has no directory mode that refuses a new file inside it.";
    private const string _noTransactions = "This volume has no transaction support to hold the name with.";
    private const string _noRefusal = "This machine does not refuse a move onto a transacted name.";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-restore-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            Relax(_directory);
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ------------------------------------------------------------------ validating a backup

    [Fact]
    public void InspectingABackup_CountsWhatIsLive_AndLeavesOutTheRecycleBin()
    {
        var path = SeededVault("before");

        using (var vault = Vault.Open(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "kept", Password = "k", GroupPath = "servers" });
            vault.AddEntry(new VaultEntry { Title = "binned", Password = "b", GroupPath = "servers" });
            vault.Save();
        }

        Age(path, TimeSpan.FromHours(1));

        using (var vault = Vault.Open(path, _master))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry("servers/binned"));
            vault.Save();
        }

        Age(path, TimeSpan.FromHours(1));
        Edit(path, "after");

        // Newest first: the newest backup is the vault as the last edit found it, bin and all.
        var summary = VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master);

        Assert.Equal(2, summary.Entries);
        Assert.Equal(1, summary.EnvProjects);
        Assert.Equal(3, summary.Groups);
        Assert.Equal(new FileInfo(path).Length, summary.Replaces?.Length);
    }

    [Fact]
    public void InspectingChangesNothing()
    {
        var path = SeededVault("before");
        Edit(path, "after");

        var vaultBefore = File.ReadAllBytes(path);
        var namesBefore = Names(path);
        var backup = Assert.Single(VaultBackups.List(path));
        var backupBefore = File.ReadAllBytes(backup.Path);

        VaultBackups.Inspect(path, backup, _master);

        Assert.Equal(vaultBefore, File.ReadAllBytes(path));
        Assert.Equal(backupBefore, File.ReadAllBytes(backup.Path));
        Assert.Equal(namesBefore, Names(path));
        Assert.Empty(Staged(path));
    }

    [Fact]
    public void AWrongPassword_AndADamagedBody_GiveTheSameAnswer()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var backup = Assert.Single(VaultBackups.List(path));

        var wrong = Assert.Throws<InvalidMasterPasswordException>(
            () => VaultBackups.Inspect(path, backup, "not the password"));

        DamageBody(backup.Path);

        var damaged = Assert.Throws<InvalidMasterPasswordException>(
            () => VaultBackups.Inspect(path, backup, _master));

        Assert.Equal(wrong.Message, damaged.Message);
    }

    [Fact]
    public void ABackupWithoutAKdbxHeader_IsRefusedBeforeThePasswordIsUsed()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var backup = Assert.Single(VaultBackups.List(path));

        DamageHeader(backup.Path);

        // The right password, so an InvalidMasterPasswordException here would mean it was tried.
        Assert.Throws<VaultRestoreException>(() => VaultBackups.Inspect(path, backup, _master));
    }

    [Fact]
    public void AFileTheListDoesNotName_IsNotInspected()
    {
        var path = SeededVault("before");
        Edit(path, "after");

        var theirs = Path.Combine(VaultBackups.DirectoryFor(path), "my own copy.kdbx");
        File.Copy(path, theirs);

        Assert.Throws<VaultRestoreException>(
            () => VaultBackups.Inspect(path, new VaultBackup(theirs, DateTimeOffset.UtcNow), _master));

        Assert.Throws<VaultRestoreException>(
            () => VaultBackups.Inspect(path, new VaultBackup(path, DateTimeOffset.UtcNow), _master));
    }

    [Fact]
    public void WhatWillBeReplaced_IsNullWhenNothingIsThere()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        File.Delete(path);

        Assert.Null(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master).Replaces);
    }

    // ------------------------------------------------------------------ what a restore does

    [Fact]
    public void ARestore_PutsTheBackupsBytesAtTheVault()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var backup = Assert.Single(VaultBackups.List(path));
        var bytes = File.ReadAllBytes(backup.Path);

        var report = VaultBackups.Restore(VaultBackups.Inspect(path, backup, _master));

        Assert.Equal(backup, report.Restored);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(bytes, File.ReadAllBytes(backup.Path));
        Assert.Equal("before", ReadToken(path, _master));
        Assert.Empty(Staged(path));
    }

    /// <remarks>
    /// Planted, because the core has no call that changes a master password until V.1a. The file is a
    /// real vault made under another password and given a name <c>List</c> recognises, which is what a
    /// backup taken before a password change in KeePassXC is.
    /// </remarks>
    [Fact]
    public void ARestoredVault_OpensWithTheBackupsPassword_NotTheOneItReplaced()
    {
        var path = SeededVault("current");
        var planted = Plant(path, SeededVault("earlier", master: _earlier), "20200101T000000Z");

        Assert.Throws<InvalidMasterPasswordException>(() => VaultBackups.Inspect(path, planted, _master));

        VaultBackups.Restore(VaultBackups.Inspect(path, planted, _earlier));

        Assert.Equal("earlier", ReadToken(path, _earlier));
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _master).Dispose());

        // What it replaced still opens with the password it was written under.
        var preserved = VaultBackups.List(path)[0];
        Assert.Equal("current", ReadToken(preserved.Path, _master));
    }

    [Fact]
    public void ARestore_OverABodyDamagedVault()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        DamageBody(path);
        var damaged = File.ReadAllBytes(path);

        var report = VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));

        Assert.Equal("before", ReadToken(path, _master));
        Assert.Equal(damaged, File.ReadAllBytes(Assert.IsType<VaultBackup>(report.Preserved).Path));
    }

    [Fact]
    public void ARestore_OverAFileThatIsNotKdbx()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        File.WriteAllText(path, "a sync tool's conflict note, where a vault was");

        VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));

        Assert.Equal("before", ReadToken(path, _master));
    }

    [Fact]
    public void ARestore_WhereThereIsNoVault()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        File.Delete(path);

        var report = VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));

        Assert.Null(report.Preserved);
        Assert.Null(report.AlreadyKeptAs);
        Assert.Equal("before", ReadToken(path, _master));
    }

    [Fact]
    public void ARestoredVaultIsReadableByItsOwnerAlone()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no owner-only file mode, so there is no permission here to set.");
            return;
        }

        var path = SeededVault("before");
        Edit(path, "after");

        VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    // ------------------------------------------------------------------ what it keeps

    [Fact]
    public void TheVaultARestoreReplaces_BecomesAListedBackup_EvenInsideTheFloor()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var replaced = File.ReadAllBytes(path);

        // No Age: the only backup is seconds old, which is inside the floor a save would respect.
        var report = VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));

        var preserved = Assert.IsType<VaultBackup>(report.Preserved);
        Assert.Null(report.AlreadyKeptAs);
        Assert.Equal(preserved, VaultBackups.List(path)[0]);
        Assert.Equal(replaced, File.ReadAllBytes(preserved.Path));
        Assert.Equal("after", ReadToken(preserved.Path, _master));
    }

    [Fact]
    public void AVaultAlreadyKeptByteForByte_IsNotKeptTwice()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var original = Assert.Single(VaultBackups.List(path));

        VaultBackups.Restore(VaultBackups.Inspect(path, original, _master));
        var names = Names(path);

        // The vault is now that backup's bytes, so restoring anything over it has nothing new to keep.
        var again = VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));

        Assert.Null(again.Preserved);
        Assert.Equal(original, again.AlreadyKeptAs);
        Assert.Equal(names, Names(path));
    }

    [Fact]
    public void ARestoreNeverPrunes_EvenWhenItRestoresTheOldestOfFive()
    {
        var path = SeededVault("edit-0");

        for (var edit = 1; edit <= VaultBackups.Retained; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        var before = VaultBackups.List(path);
        Assert.Equal(VaultBackups.Retained, before.Count);
        var oldest = before[^1];

        VaultBackups.Restore(VaultBackups.Inspect(path, oldest, _master));

        var after = VaultBackups.List(path);
        Assert.Equal(VaultBackups.Retained + 1, after.Count);
        Assert.All(before, backup => Assert.Contains(backup, after));
        Assert.Equal("edit-0", ReadToken(path, _master));
    }

    [Fact]
    public void TheNextSavePrunes()
    {
        var path = SeededVault("edit-0");

        for (var edit = 1; edit <= VaultBackups.Retained; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master));
        Age(path, TimeSpan.FromHours(1));

        Edit(path, "after the restore");

        Assert.Equal(VaultBackups.Retained, VaultBackups.List(path).Count);
    }

    [Fact]
    public void RestoringRepeatedlyBetweenSaves_AddsOneBackupAtMost()
    {
        var path = SeededVault("edit-0");

        for (var edit = 1; edit <= 3; edit++)
        {
            Edit(path, $"edit-{edit}");
            Age(path, TimeSpan.FromHours(1));
        }

        var count = VaultBackups.List(path).Count;

        foreach (var pick in new[] { 2, 0, 1, 2, 0 })
        {
            VaultBackups.Restore(VaultBackups.Inspect(path, VaultBackups.List(path)[pick], _master));
        }

        Assert.Equal(count + 1, VaultBackups.List(path).Count);
    }

    // ------------------------------------------------------------------ when it cannot

    [Fact]
    public void ABackupChangedSinceItWasValidated_IsRefused_AndNothingIsKept()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var backup = Assert.Single(VaultBackups.List(path));
        var validated = VaultBackups.Inspect(path, backup, _master);

        // Another real vault under the same password and the same name: everything about it would
        // pass a fresh look, except that it is not what was looked at.
        File.Copy(SeededVault("somebody else's"), backup.Path, overwrite: true);

        var live = File.ReadAllBytes(path);
        var names = Names(path);

        Assert.Throws<VaultRestoreException>(() => VaultBackups.Restore(validated));

        Assert.Equal(live, File.ReadAllBytes(path));
        Assert.Equal(names, Names(path));
        Assert.Empty(Staged(path));
    }

    [Fact]
    public void ABackupNoLongerListed_IsRefused()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var backup = Assert.Single(VaultBackups.List(path));
        var validated = VaultBackups.Inspect(path, backup, _master);

        File.Move(backup.Path, Path.Combine(Path.GetDirectoryName(backup.Path)!, "renamed by hand.kdbx"));
        var live = File.ReadAllBytes(path);

        Assert.Throws<VaultRestoreException>(() => VaultBackups.Restore(validated));
        Assert.Equal(live, File.ReadAllBytes(path));
    }

    /// <remarks>
    /// The destination failure, arranged the same way on every system: the staged file is removed at
    /// the one instant between keeping the vault and replacing it, so the rename has nothing to move.
    /// A missing source is not something waiting clears, so this also shows the refusal is immediate.
    /// </remarks>
    [Fact]
    public void AReplaceThatFails_LeavesTheVaultByteIdentical_AndStrandsNothing()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var live = File.ReadAllBytes(path);
        var waits = 0;

        Assert.Throws<VaultRestoreException>(() => VaultBackups.Restore(
            VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master),
            beforeReplacing: File.Delete,
            waitBetweenAttempts: _ => waits++));

        Assert.Equal(0, waits);
        Assert.Equal(live, File.ReadAllBytes(path));
        Assert.Equal("after", ReadToken(path, _master));
        Assert.Empty(Staged(path));

        // The copy of the vault was made before the failure and stays: a slot, never a loss.
        Assert.Equal(2, VaultBackups.List(path).Count);
    }

    [Fact]
    public void AVaultPathThatIsADirectory_IsRefused()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        File.Delete(path);
        Directory.CreateDirectory(path);

        Assert.Throws<VaultRestoreException>(() => VaultBackups.Restore(
            VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master),
            beforeReplacing: null,
            waitBetweenAttempts: _ => { }));

        Assert.True(Directory.Exists(path));
        Assert.Empty(Staged(path));
    }

    [Fact]
    public void AVaultThatCannotBeKept_IsNotReplaced()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(_onlyUnix);
            return;
        }

        var path = SeededVault("before");
        Edit(path, "after");
        var validated = VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master);
        var live = File.ReadAllBytes(path);

        // Readable, so the backup can still be found and read, and closed to a new copy of the vault.
        var directory = VaultBackups.DirectoryFor(path);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            Assert.Throws<VaultBackupException>(() => VaultBackups.Restore(validated));
        }
        finally
        {
            Relax(directory);
        }

        Assert.Equal(live, File.ReadAllBytes(path));
        Assert.Empty(Staged(path));
    }

    [Fact]
    public void AVaultChangedDuringTheRestore_IsNotReplaced()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var theirs = "another program saved here while the restore ran"u8.ToArray();

        Assert.Throws<VaultChangedOnDiskException>(() => VaultBackups.Restore(
            VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master),
            beforeReplacing: _ => File.WriteAllBytes(path, theirs),
            waitBetweenAttempts: null));

        Assert.Equal(theirs, File.ReadAllBytes(path));
        Assert.Empty(Staged(path));
    }

    [Fact]
    public void AVaultThatAppearedDuringTheRestore_IsNotReplaced()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        File.Delete(path);
        var theirs = "somebody put a file back while the restore ran"u8.ToArray();

        Assert.Throws<VaultChangedOnDiskException>(() => VaultBackups.Restore(
            VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master),
            beforeReplacing: _ => File.WriteAllBytes(path, theirs),
            waitBetweenAttempts: null));

        Assert.Equal(theirs, File.ReadAllBytes(path));
    }

    [Fact]
    public void AVaultHeldOpen_IsRefusedAfterTheWholeBudget_AndComesFreeInsideIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
            return;
        }

        var path = SeededVault("before");
        Edit(path, "after");
        var live = File.ReadAllBytes(path);
        var waits = 0;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<VaultRestoreException>(() => VaultBackups.Restore(
                VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master),
                beforeReplacing: null,
                waitBetweenAttempts: _ => waits++));
        }

        Assert.Equal(KeePassInterop.SaveAttempts - 1, waits);
        Assert.Equal(live, File.ReadAllBytes(path));
        Assert.Empty(Staged(path));

        var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            VaultBackups.Restore(
                VaultBackups.Inspect(path, VaultBackups.List(path)[^1], _master),
                beforeReplacing: null,
                waitBetweenAttempts: _ => holder.Dispose());
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal("before", ReadToken(path, _master));
    }

    /// <remarks>
    /// What holds a vault's name in the wild is another process saving it (D-0119). This confirms or
    /// refutes that the refusal a save meets there reaches a restore's plain rename as something it
    /// retries, and that a writer who then commits is left alone rather than restored over.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ANameTakenByAWriterThatCommits_IsNotRestoredOver()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
            return;
        }

        var path = SeededVault("before");
        Edit(path, "after");
        var validated = VaultBackups.Inspect(path, VaultBackups.List(path)[0], _master);

        HeldTransactedName? held = null;

        try
        {
            // Held from the instant the vault has been kept, which is when another save landing matters:
            // before it, whatever is at the path is simply what the restore keeps and replaces.
            Assert.Throws<VaultChangedOnDiskException>(() => VaultBackups.Restore(
                validated,
                beforeReplacing: _ =>
                {
                    held = HeldTransactedName.TryHold(path);
                    if (held is null)
                    {
                        Assert.Skip(_noTransactions);
                    }

                    if (!HeldTransactedName.ReproducesTheRefusal(path))
                    {
                        Assert.Skip(_noRefusal);
                    }
                },
                waitBetweenAttempts: attempt =>
                {
                    if (attempt == 1)
                    {
                        held!.Commit();
                    }
                }));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(HeldTransactedName.DecoyBytes, File.ReadAllBytes(path));
        Assert.Empty(Staged(path));
    }

    // ------------------------------------------------------------------ interrupted

    /// <remarks>
    /// The helper stops at the instant between keeping the vault and replacing it and says so, which
    /// is the only way to land there: nothing but a rename lies between the two. A rename either
    /// happened or did not, so the claim is that the vault is whole and its copy is already beside it,
    /// and that the next restore clears away what the killed one staged.
    /// </remarks>
    [Fact]
    public async Task ARestoreKilledAtTheBoundary_LeavesAWholeVault_AndTheCopyOfTheOneItReplaced()
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var live = File.ReadAllBytes(path);
        var backup = Assert.Single(VaultBackups.List(path));

        var info = new ProcessStartInfo
        {
            FileName = Helper("Keypaste.VaultSaver"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        info.ArgumentList.Add("restore-hold");
        info.ArgumentList.Add(path);
        info.ArgumentList.Add(Path.GetFileName(backup.Path));
        info.Environment["KEYPASTE_SAVER_PASSWORD"] = _master;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("the saver did not start");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));

        Assert.Equal("holding", await process.StandardOutput.ReadLineAsync(deadline.Token));

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(deadline.Token);

        Assert.Equal(live, File.ReadAllBytes(path));
        Assert.Single(Staged(path));

        var kept = VaultBackups.List(path);
        Assert.Equal(2, kept.Count);
        Assert.Equal(live, File.ReadAllBytes(kept[0].Path));

        var report = VaultBackups.Restore(VaultBackups.Inspect(path, backup, _master));

        Assert.Equal(kept[0], report.AlreadyKeptAs);
        Assert.Empty(Staged(path));
        Assert.Equal(2, VaultBackups.List(path).Count);
        Assert.Equal("before", ReadToken(path, _master));
    }

    // ------------------------------------------------------------------ exporting

    [Fact]
    public void AnExportIsTheSavedBytes_AndOpensWithTheSamePassword()
    {
        var path = SeededVault("exported");
        var destination = Path.Combine(_directory, "elsewhere", "copy.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using (var vault = Vault.Open(path, _master))
        {
            vault.ExportTo(destination);
        }

        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(destination));
        Assert.Equal("exported", ReadToken(destination, _master));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(destination));
        }
    }

    [Fact]
    public void AnExportAfterASave_IsWhatWasSaved()
    {
        var path = SeededVault("before");
        var destination = Path.Combine(_directory, "after-a-save.kdbx");

        using (var vault = Vault.Open(path, _master))
        {
            Replace(vault, "after");
            vault.Save();
            vault.ExportTo(destination);
        }

        Assert.Equal("after", ReadToken(destination, _master));
    }

    [Fact]
    public void AnExistingDestination_IsRefused_AndUntouched()
    {
        var path = SeededVault("exported");
        var file = Path.Combine(_directory, "already here.kdbx");
        File.WriteAllText(file, "somebody's file");
        var folder = Directory.CreateDirectory(Path.Combine(_directory, "a folder.kdbx")).FullName;

        using var vault = Vault.Open(path, _master);

        Assert.Throws<VaultException>(() => vault.ExportTo(file));
        Assert.Throws<VaultException>(() => vault.ExportTo(folder));

        Assert.Equal("somebody's file", File.ReadAllText(file));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void TheVaultItself_UnderAnotherSpelling_IsRefused()
    {
        var path = SeededVault("exported");
        var bytes = File.ReadAllBytes(path);
        var respelled = Path.Combine(
            Path.GetDirectoryName(path)!, "..", Path.GetFileName(Path.GetDirectoryName(path)!), Path.GetFileName(path));

        using var vault = Vault.Open(path, _master);

        Assert.Throws<VaultException>(() => vault.ExportTo(respelled));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <remarks>
    /// Under a name the backup parser does not recognise, and under one it does. The refusal is about
    /// where the file would be, so neither name gets in, and the picker's suggested name is never what
    /// keeps an export out of the rotation.
    /// </remarks>
    [Theory]
    [InlineData("vault-copy-20260920.kdbx")]
    [InlineData("vault.20260920T120000Z.kdbx")]
    [InlineData("nested/deeper/copy.kdbx")]
    public void ADestinationInsideTheBackupDirectory_IsRefused_WhateverItIsCalled(string name)
    {
        var path = SeededVault("before");
        Edit(path, "after");
        var names = Directory.GetFileSystemEntries(VaultBackups.DirectoryFor(path));

        using var vault = Vault.Open(path, _master);

        Assert.Throws<VaultException>(
            () => vault.ExportTo(Path.Combine(VaultBackups.DirectoryFor(path), name)));

        Assert.Equal(names, Directory.GetFileSystemEntries(VaultBackups.DirectoryFor(path)));
    }

    [Fact]
    public void TheBackupDirectorysOwnName_IsRefused_BeforeTheDirectoryExists()
    {
        // A file there would stop the directory being created, and then no save could happen.
        var path = SeededVault("never edited");

        using var vault = Vault.Open(path, _master);

        Assert.Throws<VaultException>(() => vault.ExportTo(VaultBackups.DirectoryFor(path)));
        Assert.False(File.Exists(VaultBackups.DirectoryFor(path)));
    }

    [Fact]
    public void AVaultChangedOnDisk_IsNotExported()
    {
        var path = SeededVault("before");
        var destination = Path.Combine(_directory, "stale.kdbx");

        using var vault = Vault.Open(path, _master);
        Edit(path, "somebody else's edit");

        Assert.Throws<VaultChangedOnDiskException>(() => vault.ExportTo(destination));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void ADestinationThatCannotBeWritten_LeavesNothingBehind()
    {
        var path = SeededVault("exported");
        var blocker = Path.Combine(_directory, "a file, not a folder");
        File.WriteAllText(blocker, "in the way");

        using var vault = Vault.Open(path, _master);

        Assert.Throws<VaultException>(() => vault.ExportTo(Path.Combine(blocker, "copy.kdbx")));
        Assert.Throws<VaultException>(
            () => vault.ExportTo(Path.Combine(_directory, "no such folder", "copy.kdbx")));

        Assert.Equal("in the way", File.ReadAllText(blocker));
        Assert.False(Directory.Exists(Path.Combine(_directory, "no such folder")));
    }

    [Fact]
    public void AVaultNeverSaved_IsNotExported()
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, Path.GetRandomFileName())).FullName;
        var destination = Path.Combine(_directory, "nothing.kdbx");

        using var vault = Vault.Create(Path.Combine(home, "vault.kdbx"), _master);

        Assert.Throws<VaultException>(() => vault.ExportTo(destination));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void AnExportTakesNoBackup_AndTheNextSaveStillWorks()
    {
        var path = SeededVault("before");

        using var vault = Vault.Open(path, _master);
        vault.ExportTo(Path.Combine(_directory, "copy.kdbx"));

        Assert.Empty(VaultBackups.List(path));
        Assert.Null(vault.LastBackup);

        Replace(vault, "after");
        vault.Save();

        Assert.Single(VaultBackups.List(path));
    }

    // ------------------------------------------------------------------ fixtures

    private string SeededVault(string token, string master = _master)
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, Path.GetRandomFileName())).FullName;
        var path = Path.Combine(home, "vault.kdbx");

        using var vault = Vault.Create(path, master);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = token, GroupPath = "env/p" });
        vault.Save();

        return path;
    }

    private static void Edit(string path, string token)
    {
        using var vault = Vault.Open(path, _master);
        Replace(vault, token);
        vault.Save();
    }

    private static void Replace(Vault vault, string token) =>
        vault.UpdateEntry(new VaultEntry { Title = "TOKEN", Password = token, GroupPath = "env/p" });

    private static string ReadToken(string path, string master)
    {
        using var vault = Vault.Open(path, master);
        return vault.Find("env/p/TOKEN")?.Password ?? throw new InvalidOperationException("no entry");
    }

    /// <summary>Gives a file a name <see cref="VaultBackups.List"/> recognises for this vault.</summary>
    private static VaultBackup Plant(string vaultPath, string source, string stamp)
    {
        var directory = Directory.CreateDirectory(VaultBackups.DirectoryFor(vaultPath)).FullName;
        var planted = Path.Combine(
            directory, $"{Path.GetFileNameWithoutExtension(vaultPath)}.{stamp}{Path.GetExtension(vaultPath)}");

        File.Copy(source, planted);

        return VaultBackups.List(vaultPath).Single(backup => backup.Path == planted);
    }

    /// <summary>Flips a run of bytes well past the header, where only the block check can notice.</summary>
    private static void DamageBody(string path)
    {
        var bytes = File.ReadAllBytes(path);

        for (var index = bytes.Length * 2 / 3; index < (bytes.Length * 2 / 3) + 64; index++)
        {
            bytes[index] ^= 0xFF;
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void DamageHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Array.Clear(bytes, 0, 8);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Moves every backup's stamp back, as <c>VaultBackupTests.Age</c> does and for its reason.</summary>
    private static void Age(string vaultPath, TimeSpan by)
    {
        var stem = Path.GetFileNameWithoutExtension(vaultPath) + ".";
        var extension = Path.GetExtension(vaultPath);

        foreach (var backup in VaultBackups.List(vaultPath).Reverse())
        {
            var stamp = (backup.TakenAt - by).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            File.Move(
                backup.Path,
                Path.Combine(Path.GetDirectoryName(backup.Path)!, stem + stamp + extension),
                overwrite: false);
        }
    }

    private static string[] Names(string vaultPath) =>
        [.. VaultBackups.List(vaultPath).Select(backup => Path.GetFileName(backup.Path)).Order(StringComparer.Ordinal)];

    private static string[] Staged(string vaultPath) =>
        Directory.GetFiles(Path.GetDirectoryName(vaultPath)!, "*.keypaste-restore-*.tmp");

    private static void Relax(string directory)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

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
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("Could not locate keypaste.slnx above " + AppContext.BaseDirectory);
        }

        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase)
            ? "debug"
            : "release";

        return Path.Combine(
            directory, "artifacts", "bin", name, configuration,
            OperatingSystem.IsWindows() ? name + ".exe" : name);
    }
}
