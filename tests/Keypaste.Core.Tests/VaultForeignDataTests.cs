using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault holding XML KeePassLib does not keep, as another program or a newer KDBX version writes it: the save that leaves that
/// data out keeps the file it replaces first, whatever the backup floor says, and names that copy once; saving goes on.
/// </summary>
[Collection(nameof(SavesThatSpawnProcesses))]
public sealed class VaultForeignDataTests : IDisposable
{
    private const string _password = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-foreign-data-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheFirstSave_KeepsTheFileWithTheOtherProgramsData_ThoughABackupIsMinutesOld_AndSaysSoOnce()
    {
        SeedWithARecentBackup();
        ForeignXml.AddTo(VaultPath, _password);
        var theirs = File.ReadAllBytes(VaultPath);

        using var vault = ClaimedVault.Open(VaultPath, _password);
        Add(vault, "first");
        vault.Save();

        var kept = vault.TakeForeignDataKept();
        Assert.NotNull(kept);
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);
        Assert.Equal(theirs, File.ReadAllBytes(kept.Path));
        Assert.True(ForeignXml.Holds(kept.Path, _password));
        Assert.False(ForeignXml.Holds(VaultPath, _password));
        Assert.Contains(kept.Path, Vault.ForeignDataText(kept), StringComparison.Ordinal);
        Assert.Null(vault.TakeForeignDataKept());

        Add(vault, "second");
        vault.Save();

        Assert.Null(vault.TakeForeignDataKept());
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);

        using var reopened = ClaimedVault.Open(VaultPath, _password);
        Add(reopened, "third");
        reopened.Save();
        Assert.Null(reopened.TakeForeignDataKept());
        Assert.NotNull(reopened.Find(new EntryName("work", "second")));
    }

    [Fact]
    public void AVaultOfKeypastesOwn_NeverSaysSo()
    {
        SeedWithARecentBackup();

        using var vault = ClaimedVault.Open(VaultPath, _password);
        Add(vault, "first");
        vault.Save();

        Assert.Null(vault.TakeForeignDataKept());
        Assert.Single(VaultBackups.List(VaultPath));
    }

    [Fact]
    public void ABackupThatFails_FailsTheSave_AndTheNextSaveStillKeepsTheFile()
    {
        SeedWithARecentBackup();
        ForeignXml.AddTo(VaultPath, _password);
        var backups = VaultBackups.DirectoryFor(VaultPath);
        var aside = backups + ".aside";
        Directory.Move(backups, aside);
        File.WriteAllText(backups, "in the directory's way");

        using var vault = ClaimedVault.Open(VaultPath, _password);
        Add(vault, "first");

        Assert.Throws<VaultBackupException>(vault.Save);
        Assert.True(ForeignXml.Holds(VaultPath, _password));
        Assert.Null(vault.TakeForeignDataKept());

        File.Delete(backups);
        Directory.Move(aside, backups);
        vault.Save();

        Assert.True(ForeignXml.Holds(vault.TakeForeignDataKept()!.Path, _password));
    }

    [Fact]
    public void AMergeOfAnotherProgramsSaveHoldingSuchData_KeepsThatSaveFirst_AndSaysSoOnce()
    {
        SeedWithARecentBackup();
        using var open = ClaimedVault.Open(VaultPath, _password);
        Add(open, "mine");
        ForeignXml.AddTo(VaultPath, _password);
        var theirs = File.ReadAllBytes(VaultPath);

        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.NotNull(merged.ForeignDataKept);
        Assert.Equal(theirs, File.ReadAllBytes(merged.ForeignDataKept.Path));
        Assert.True(ForeignXml.Holds(merged.ForeignDataKept.Path, _password));
        Assert.Null(copy!.TakeForeignDataKept());
        Assert.Null(open.TakeForeignDataKept());

        Add(copy, "after");
        copy.Save();
        Assert.Null(copy.TakeForeignDataKept());
    }

    [Fact]
    public void MergingAConflictedCopyIntoAFileHoldingSuchData_KeepsTheFileFirst_AndSaysSo()
    {
        SeedWithARecentBackup();
        var copyPath = Path.Combine(_directory, "vault (conflicted copy).kdbx");
        File.Copy(VaultPath, copyPath);

        using (var phone = ClaimedVault.Open(copyPath, _password))
        {
            Add(phone, "phone");
            phone.Save();
        }

        ForeignXml.AddTo(VaultPath, _password);
        using var open = ClaimedVault.Open(VaultPath, _password);

        var merged = open.MergeConflictedCopy(copyPath, save: true);

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.True(ForeignXml.Holds(merged.ForeignDataKept!.Path, _password));
        Assert.False(ForeignXml.Holds(VaultPath, _password));
    }

    /// <summary>A vault saved twice by keypaste, so one backup of it is moments old.</summary>
    private void SeedWithARecentBackup()
    {
        using (var created = ClaimedVault.Create(VaultPath, _password))
        {
            Add(created, "seed");
            created.Save();
        }

        using (var vault = ClaimedVault.Open(VaultPath, _password))
        {
            Add(vault, "seed-2");
            vault.Save();
        }

        Assert.Single(VaultBackups.List(VaultPath));
    }

    private static void Add(Vault vault, string title) =>
        vault.AddEntry(new VaultEntry { Title = title, Password = title + "-password", GroupPath = "work" });
}
