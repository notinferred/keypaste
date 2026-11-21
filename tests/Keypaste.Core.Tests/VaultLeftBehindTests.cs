using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault holding data KeePassLib does not keep, as another program or a newer KDBX version writes it, or saved in a format older
/// than KDBX 4: the save that leaves that data or format behind keeps the file it replaces first, whatever the backup floor says,
/// and names that copy once; saving goes on.
/// </summary>
[Collection(nameof(SavesThatSpawnProcesses))]
public sealed class VaultLeftBehindTests : IDisposable
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

        var leftBehind = vault.TakeLeftBehind();
        Assert.NotNull(leftBehind);
        Assert.Equal((true, false), (leftBehind.OtherProgramsData, leftBehind.OldFormat));
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);
        Assert.Equal(theirs, File.ReadAllBytes(leftBehind.Kept.Path));
        Assert.True(ForeignXml.Holds(leftBehind.Kept.Path, _password));
        Assert.False(ForeignXml.Holds(VaultPath, _password));
        Assert.Contains($"another program that keypaste can't keep, so the save left it out. keypaste kept a copy of the file from before the save, with that data, at '{leftBehind.Kept.Path}'", leftBehind.Text, StringComparison.Ordinal);
        Assert.Contains("for up to about four weeks", leftBehind.Text, StringComparison.Ordinal);
        Assert.Null(vault.TakeLeftBehind());

        Add(vault, "second");
        vault.Save();

        Assert.Null(vault.TakeLeftBehind());
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);

        using var reopened = ClaimedVault.Open(VaultPath, _password);
        Add(reopened, "third");
        reopened.Save();
        Assert.Null(reopened.TakeLeftBehind());
        Assert.NotNull(reopened.Find(new EntryName("work", "second")));
    }

    /// <summary>An attribute or a header field KeePassLib does not know is left out as an element is, and kept and named the same way.</summary>
    [Theory]
    [InlineData("attribute")]
    [InlineData("header field")]
    [InlineData("inner header field")]
    public void AnUnknownAttributeOrHeaderField_IsKeptAndNamedAsAnElementIs(string what)
    {
        SeedWithARecentBackup();
        Action<string, string> add = what switch
        {
            "attribute" => ForeignXml.AddAttributeTo,
            "header field" => ForeignXml.AddHeaderFieldTo,
            _ => ForeignXml.AddInnerHeaderFieldTo,
        };
        add(VaultPath, _password);
        var theirs = File.ReadAllBytes(VaultPath);

        using var vault = ClaimedVault.Open(VaultPath, _password);
        Add(vault, "first");
        vault.Save();

        var leftBehind = vault.TakeLeftBehind();
        Assert.NotNull(leftBehind);
        Assert.True(leftBehind.OtherProgramsData);
        Assert.Equal(theirs, File.ReadAllBytes(leftBehind.Kept.Path));
        Assert.True(ForeignXml.Holds(leftBehind.Kept.Path, _password));
        Assert.False(ForeignXml.Holds(VaultPath, _password));

        Add(vault, "second");
        vault.Save();
        Assert.Null(vault.TakeLeftBehind());
    }

    /// <summary>A KDBX 3.1 vault's first save writes KDBX 4, keeps the 3.1 file though a backup is minutes old, and says so once; later saves say nothing.</summary>
    [Fact]
    public void AKdbx31VaultsFirstSave_KeepsTheOldFormatFile_AndSaysSoOnce()
    {
        SeedWithARecentBackup();
        Kdbx31.SaveAs(VaultPath, _password);
        var old = File.ReadAllBytes(VaultPath);
        Assert.Equal(3, KdbxHeader.Read(VaultPath).FormatMajorVersion);

        using var vault = ClaimedVault.Open(VaultPath, _password);
        Add(vault, "first");
        vault.Save();

        var leftBehind = vault.TakeLeftBehind();
        Assert.NotNull(leftBehind);
        Assert.Equal((false, true), (leftBehind.OtherProgramsData, leftBehind.OldFormat));
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);
        Assert.Equal(old, File.ReadAllBytes(leftBehind.Kept.Path));
        Assert.Equal(4, KdbxHeader.Read(VaultPath).FormatMajorVersion);
        Assert.Equal(
            $"keypaste saved this vault in the current KeePass format, which KeePass before 2.35 can't open. It kept a copy of the file in the old format at '{leftBehind.Kept.Path}', which stays among the vault's backups for up to about four weeks.",
            leftBehind.Text);
        Assert.Null(vault.TakeLeftBehind());

        Add(vault, "second");
        vault.Save();
        Assert.Null(vault.TakeLeftBehind());

        using var reopened = ClaimedVault.Open(VaultPath, _password);
        Add(reopened, "third");
        reopened.Save();
        Assert.Null(reopened.TakeLeftBehind());
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);
    }

    /// <summary>A save leaving both behind says both in one notice, with the copy and how long it stays.</summary>
    [Fact]
    public void BothReasons_AreSaidTogether()
    {
        var kept = new VaultBackup(Path.Combine(_directory, "vault.kdbx.backups", "kept.kdbx"), DateTimeOffset.UnixEpoch);

        Assert.Equal(
            "keypaste saved this vault in the current KeePass format, which KeePass before 2.35 can't open, and left out data from another program that it can't keep. " +
            $"It kept a copy of the file from before the save, in the old format and with that data, at '{kept.Path}', which stays among the vault's backups for up to about four weeks.",
            new VaultLeftBehind(kept, OtherProgramsData: true, OldFormat: true).Text);
    }

    [Fact]
    public void AVaultOfKeypastesOwn_NeverSaysSo()
    {
        SeedWithARecentBackup();

        using var vault = ClaimedVault.Open(VaultPath, _password);
        Add(vault, "first");
        vault.Save();

        Assert.Null(vault.TakeLeftBehind());
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
        Assert.Null(vault.TakeLeftBehind());

        File.Delete(backups);
        Directory.Move(aside, backups);
        vault.Save();

        Assert.True(ForeignXml.Holds(vault.TakeLeftBehind()!.Kept.Path, _password));
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
        Assert.NotNull(merged.LeftBehind);
        Assert.Equal(theirs, File.ReadAllBytes(merged.LeftBehind.Kept.Path));
        Assert.True(ForeignXml.Holds(merged.LeftBehind.Kept.Path, _password));
        Assert.Null(copy!.TakeLeftBehind());
        Assert.Null(open.TakeLeftBehind());

        Add(copy, "after");
        copy.Save();
        Assert.Null(copy.TakeLeftBehind());
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
        Assert.True(ForeignXml.Holds(merged.LeftBehind!.Kept.Path, _password));
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
