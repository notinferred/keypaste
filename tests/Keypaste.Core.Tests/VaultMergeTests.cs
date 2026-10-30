using System.Security.Cryptography;
using System.Text;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core.Agents;
using Keypaste.Core.Browser;
using Keypaste.Core.Internal;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// Merging another program's save with the open copy of a vault (1.4b1, D-0537 to D-0541, D-0545), on real files the vendored
/// library writes: the open copy's changes are keypaste's own, made through <see cref="Vault"/>, and the other program's are made
/// through KeePassLib directly, a revision of the entry as it was before each change, as KeePass and KeePassXC save one.
/// </summary>
public sealed class VaultMergeTests : IDisposable
{
    private const string _password = "correct horse battery staple";
    private const string _work = "work";

    private static readonly EntryName _stripe = new(_work, "stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-merge-").FullName;

    /// <summary>When every object the tests start from was last changed, two hours before the open copy's edits.</summary>
    private readonly DateTime _seeded = Whole(DateTime.UtcNow.AddHours(-2));

    /// <summary>When the other program saves a change the tests want later than the open copy's.</summary>
    private readonly DateTime _later = Whole(DateTime.UtcNow.AddHours(1));

    /// <summary>The vault the open copy reads and keeps its base of.</summary>
    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    /// <summary>The same vault as the other program saves it, a copy of the first until it does.</summary>
    private string FilePath => Path.Combine(_directory, "file.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AnEntryRenamedAndMovedInTheFile_IsMatchedByItsUuid()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        var uuid = open.EntryUuid(_stripe);
        Update(open, "stripe", "mine");
        InFile(FilePath, database =>
        {
            Edit(database, "stripe", _later, entry => entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "stripe-live")));
            MoveTo(Entry(database, "stripe-live"), AddGroup(database, "billing"), _later);
        });

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Assert.Single(database.RootGroup.GetEntries(true));
            Assert.Equal(uuid, entry.Uuid.ToHexString());
            Assert.Equal(("billing", "stripe-live", Value("stripe", 0)), (entry.ParentGroup.Name, Title(entry), Password(entry)));
            Assert.Equal([Value("stripe", 0), "mine"], HistoryPasswords(entry));
        });
        Assert.Equal(new MergeConflict(new EntryName("billing", "stripe-live"), OpenCopyCurrent: false), Assert.Single(report.Conflicts));
    }

    /// <summary>The file holds the base's version in the entry's history, so its change is taken though its clock stamped it earlier (D-0538).</summary>
    [Fact]
    public void AnEntryOnlyTheFileChanged_IsTheFilesVersion_EvenWhenStampedEarlier()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        InFile(FilePath, database => Edit(database, "stripe", _seeded.AddHours(-1), entry => SetPassword(entry, "theirs")));

        using var merged = MergeKeeping(open, out var report);

        Assert.Equal("theirs", merged.Find(_stripe)!.Password);
        Assert.False(report.TookFromOpenCopy);
        Assert.Empty(report.Conflicts);
    }

    [Fact]
    public void AnEntryOnlyThisCopyChanged_IsThisCopysVersion()
    {
        Seed("stripe", "github");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database => Edit(database, "github", _later, entry => SetPassword(entry, "theirs")));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            Assert.Equal("mine", Password(Entry(database, "stripe")));
            Assert.Equal([Value("stripe", 0)], HistoryPasswords(Entry(database, "stripe")));
            Assert.Equal("theirs", Password(Entry(database, "github")));
        });
        Assert.True(report.TookFromOpenCopy);
        Assert.Empty(report.Conflicts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEntryBothChanged_KeepsTheNewerCurrent_AndTheOtherInItsHistory(bool fileNewer)
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database => Edit(database, "stripe", fileNewer ? _later : _seeded.AddHours(1), entry => SetPassword(entry, "theirs")));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal(fileNewer ? "theirs" : "mine", Password(entry));
            Assert.Equal([Value("stripe", 0), fileNewer ? "mine" : "theirs"], HistoryPasswords(entry));
        });
        Assert.Equal(new MergeConflict(_stripe, OpenCopyCurrent: !fileNewer), Assert.Single(report.Conflicts));
    }

    /// <summary>Neither copy's change is dropped: the open copy's is current a second later and the file's in history at its own second (D-0539).</summary>
    [Fact]
    public void AnEntryBothChangedInOneSecond_KeepsBoth_EachInASecondOfItsOwn()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        var second = Whole(open.ReadTimes(_stripe)!.Modified.UtcDateTime);
        InFile(FilePath, database => Edit(database, "stripe", second, entry => SetPassword(entry, "theirs")));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal(("mine", second.AddSeconds(1)), (Password(entry), entry.LastModificationTime));
            Assert.Equal(second, Assert.Single(entry.History, version => Password(version) == "theirs").LastModificationTime);
            Assert.Equal((int)entry.History.UCount, entry.History.Select(version => version.LastModificationTime).Distinct().Count());
        });
        Assert.True(Assert.Single(report.Conflicts).OpenCopyCurrent);
    }

    /// <summary>A sync service putting back a copy from before this copy's save loses neither the change nor the item that save wrote (D-0538).</summary>
    [Fact]
    public void AFileFromAnOlderFork_KeepsAChangeThisCopySaved()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        open.AddEntry(new VaultEntry { Title = "github", GroupPath = _work, Password = "added" });
        open.Save();

        var report = Merge(open);

        Read(FilePath, database =>
        {
            Assert.Equal("mine", Password(Entry(database, "stripe")));
            Assert.Equal("added", Password(Entry(database, "github")));
        });
        Assert.True(report.TookFromOpenCopy);
        Assert.Empty(report.Conflicts);
    }

    /// <summary>With nothing unsaved and a file that descends, the result is the file as Reload would give it, though the file trimmed a version this copy still holds (D-0538).</summary>
    [Fact]
    public void NothingUnsaved_AndAFileThatDescends_MergesToExactlyTheFile()
    {
        Seed(database => Revise(database, "stripe", 10), "stripe", "github", "aws");
        using var open = Vault.Open(VaultPath, _password);
        InFile(FilePath, database =>
        {
            Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs"));
            Purge(database, "github", _later);
            AddEntry(Group(database, _work), "gitlab", _later);
        });

        using var merged = MergeKeeping(open, out var report);
        using var file = Vault.Open(FilePath, _password);

        Assert.False(report.TookFromOpenCopy);
        Assert.Empty(report.Conflicts);
        Assert.Empty(report.Kept);
        Assert.Empty(report.RenamedGroups);
        Assert.Empty(report.Agents);
        Assert.Equal(file.ReadEntries(includeReserved: true), merged.ReadEntries(includeReserved: true));
        Assert.Equal(Revisions(file.ReadHistory(_stripe)!), Revisions(merged.ReadHistory(_stripe)!));
        Assert.Equal(file.TombstoneCount, merged.TombstoneCount);
    }

    [Fact]
    public void HistoryVersionsSharingASecond_AreAllKept()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database =>
        {
            Edit(database, "stripe", _seeded.AddMinutes(30), entry => SetPassword(entry, "first"));
            Edit(database, "stripe", _seeded.AddMinutes(30), entry => SetPassword(entry, "second"));
            Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs"));
        });

        Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal("theirs", Password(entry));
            Assert.Equal([Value("stripe", 0), "first", "second", "mine"], HistoryPasswords(entry));
        });
    }

    /// <summary>
    /// With the vault keeping no history, the version the merge displaced survives the save and a reopen, though KeePassLib trims every
    /// entry to the vault's limits each time it writes or reads a file, and the entry's next edit hands it back to that trimming (D-0601).
    /// </summary>
    [Fact]
    public void TheDisplacedVersion_IsKeptPastTheHistoryLimit_ThroughASaveAndAReopen()
    {
        Seed(database => database.HistoryMaxItems = 0, "stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database => Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs")));

        using (var merged = MergeKeeping(open, out _))
        {
            Assert.Equal("mine", Assert.Single(merged.ReadHistory(_stripe)!).Fields.Password);
            merged.Save();
        }

        Read(FilePath, database => Assert.Equal(["mine"], HistoryPasswords(Entry(database, "stripe"))));

        using (var reopened = Vault.Open(FilePath, _password))
        {
            Assert.Equal("mine", Assert.Single(reopened.ReadHistory(_stripe)!).Fields.Password);
            Update(reopened, "stripe", "next");
            reopened.Save();
        }

        Read(FilePath, database => Assert.Empty(Entry(database, "stripe").History));
    }

    /// <summary>An entry the file changed and this copy did not keeps the history the file gives it, its two identical versions included.</summary>
    [Fact]
    public void EntriesTheMergeDidNotTouch_KeepTheirHistory()
    {
        Seed("stripe", "github");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database =>
        {
            var github = Entry(database, "github");
            github.CreateBackup(null);
            github.CreateBackup(null);
            SetPassword(github, "theirs");
            github.LastModificationTime = _later;
        });

        Merge(open);

        Read(FilePath, database => Assert.Equal([Value("github", 0), Value("github", 0)], HistoryPasswords(Entry(database, "github"))));
    }

    [Fact]
    public void AMoveOnOneSide_IsKept_AndMovesOnBoth_TakeTheLaterLocationChange()
    {
        Seed(
            database =>
            {
                AddGroup(database, "home");
                AddGroup(database, "away");
            },
            "mine-moves",
            "theirs-moves",
            "both-theirs-later",
            "both-mine-later");
        using var open = Vault.Open(VaultPath, _password);
        Move(open, "mine-moves", "home");
        Move(open, "both-theirs-later", "home");
        Move(open, "both-mine-later", "home");
        InFile(FilePath, database =>
        {
            var away = Group(database, "away");
            MoveTo(Entry(database, "theirs-moves"), away, _later);
            MoveTo(Entry(database, "both-theirs-later"), away, _later);
            MoveTo(Entry(database, "both-mine-later"), away, _seeded.AddMinutes(30));
        });

        Merge(open);

        Read(FilePath, database =>
        {
            Assert.Equal("home", Entry(database, "mine-moves").ParentGroup.Name);
            Assert.Equal("away", Entry(database, "theirs-moves").ParentGroup.Name);
            Assert.Equal(("away", _later), (Entry(database, "both-theirs-later").ParentGroup.Name, Entry(database, "both-theirs-later").LocationChanged));
            Assert.Equal("home", Entry(database, "both-mine-later").ParentGroup.Name);
            Assert.True(Entry(database, "both-mine-later").LocationChanged > _seeded.AddMinutes(30));
        });
    }

    [Fact]
    public void AGroupMoveThatWouldMakeACycle_KeepsTheFilesPlace()
    {
        Seed(database =>
        {
            AddGroup(database, "alpha");
            AddGroup(database, "beta");
        });
        using var open = Vault.Open(VaultPath, _password);
        Assert.Equal(GroupOutcome.Moved, open.MoveGroup("alpha", "beta", out _));
        InFile(FilePath, database =>
        {
            var beta = Group(database, "beta");
            beta.ParentGroup.Groups.Remove(beta);
            Group(database, "alpha").AddGroup(beta, true);
            beta.LocationChanged = _later;
        });

        Merge(open);

        Read(FilePath, database =>
        {
            Assert.Same(database.RootGroup, Group(database, "alpha").ParentGroup);
            Assert.Same(Group(database, "alpha"), Group(database, "beta").ParentGroup);
        });
    }

    [Fact]
    public void APurgeInTheFile_RemovesAnEntryThisCopyDidNotChange()
    {
        Seed("stripe", "github");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "github", "mine");
        var purged = PwUuid.Zero;
        InFile(FilePath, database => purged = Purge(database, "stripe", _later));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            Assert.DoesNotContain(database.RootGroup.GetEntries(true), entry => Title(entry) == "stripe");
            Assert.Contains(database.DeletedObjects, tombstone => tombstone.Uuid.Equals(purged));
        });
        Assert.Empty(report.Kept);
    }

    /// <summary>A purge that never saw this copy's change keeps it in the bin, stamped after the deletion so another program's merge keeps it too (D-0540).</summary>
    [Fact]
    public void APurgeInTheFile_OfAnEntryChangedHere_KeepsItInTheBin_StampedAfterTheDeletion()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database => Purge(database, "stripe", _later));

        VaultMergeReport report;
        using (var merged = MergeKeeping(open, out report))
        {
            Assert.Empty(merged.ReadEntries());
            Assert.Equal("stripe", Assert.Single(merged.ReadRecycled()).Title);
            merged.Save();
        }

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal(("mine", _later.AddSeconds(1)), (Password(entry), entry.LastModificationTime));
            Assert.Equal(database.RecycleBinUuid, entry.ParentGroup.Uuid);
            Assert.Equal(Group(database, _work).Uuid, entry.PreviousParentGroup);
            Assert.Equal([Value("stripe", 0), "mine"], HistoryPasswords(entry));
            Assert.Empty(database.DeletedObjects);
        });
        Assert.Equal(new MergeKeptItem(_stripe, DeletedInFile: true, Recycled: true), Assert.Single(report.Kept));
    }

    [Fact]
    public void APurgeHere_OfAnEntryTheFileChanged_KeepsTheFilesVersion()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Purge(open, "stripe");
        InFile(FilePath, database => Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs")));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal(("theirs", _later.AddSeconds(1)), (Password(entry), entry.LastModificationTime));
            Assert.Equal(database.RecycleBinUuid, entry.ParentGroup.Uuid);
            Assert.DoesNotContain(database.DeletedObjects, tombstone => tombstone.Uuid.Equals(entry.Uuid));
        });
        Assert.Equal(new MergeKeptItem(_stripe, DeletedInFile: false, Recycled: true), Assert.Single(report.Kept));
    }

    /// <summary>A deletion this copy saved removes again an item an older copy brings back, unless that copy changed it after the deletion, as KeePass decides (D-0540).</summary>
    [Fact]
    public void ADeletionAlreadyWritten_RemovesAgainACopyNoNewerThanIt()
    {
        Seed("stripe", "github");
        InFile(FilePath, database => Edit(database, "github", _later, entry => SetPassword(entry, "theirs")));
        using var open = Vault.Open(VaultPath, _password);
        Purge(open, "stripe");
        Purge(open, "github");
        open.Save();

        Merge(open);

        Read(FilePath, database =>
        {
            Assert.DoesNotContain(database.RootGroup.GetEntries(true), entry => Title(entry) == "stripe");
            var github = Entry(database, "github");
            Assert.Equal("theirs", Password(github));
            Assert.DoesNotContain(database.DeletedObjects, tombstone => tombstone.Uuid.Equals(github.Uuid));
            Assert.Single(database.DeletedObjects);
        });
    }

    /// <summary>
    /// An item this copy did not change is the file's whole when the file's is stamped later, though the file kept no version of it, as
    /// KeePassXC stamps an item it recycles: no conflict, nothing added to its history and nothing taken from this copy (D-0674).
    /// </summary>
    [Fact]
    public void AnItemOnlyTheFileChanged_WithoutKeepingAVersion_IsTheFilesWhole()
    {
        Seed(database => Recycle(database, AddEntry(Group(database, _work), "binned", _seeded)), "stripe");
        using var open = Vault.Open(VaultPath, _password);
        InFile(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            var from = entry.ParentGroup;
            MoveTo(entry, database.RootGroup.FindGroup(database.RecycleBinUuid, true)!, _later);
            entry.PreviousParentGroup = from.Uuid;
            entry.LastModificationTime = _later;
        });

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal(database.RecycleBinUuid, entry.ParentGroup.Uuid);
            Assert.Empty(entry.History);
        });
        Assert.Empty(report.Conflicts);
        Assert.False(report.TookFromOpenCopy);
    }

    /// <summary>
    /// A later save from a copy forked before this copy saved its change keeps that change in history, though this copy holds nothing
    /// unsaved: its content differs from the file's, so the file's version does not stand for it (D-0538, D-0674).
    /// </summary>
    [Fact]
    public void ALaterSaveFromAForkTakenBeforeThisCopysSave_KeepsThisCopysChangeInHistory()
    {
        Seed("stripe");
        var fork = Path.Combine(_directory, "fork.kdbx");
        File.Copy(VaultPath, fork);
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        open.Save();
        InFile(fork, database => Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs")));
        File.Copy(fork, FilePath, overwrite: true);

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal("theirs", Password(entry));
            Assert.Contains("mine", HistoryPasswords(entry));
        });
        Assert.Single(report.Conflicts);
    }

    /// <summary>Recycling is a move, so the item ends in the bin with the other copy's edit (D-0540).</summary>
    [Fact]
    public void ARecycledEntry_KeepsTheOtherCopysEdit()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Assert.Equal(DeletionOutcome.Recycled, open.RemoveEntry(_stripe, out _));
        InFile(FilePath, database => Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs")));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal("theirs", Password(entry));
            Assert.Equal(database.RecycleBinUuid, entry.ParentGroup.Uuid);
        });
        Assert.Empty(report.Conflicts);
        Assert.Empty(report.Kept);
    }

    /// <summary>A restore is a change the purge never saw, so the item stays where the restore put it (D-0540).</summary>
    [Fact]
    public void ARestore_SurvivesAPurgeThatDidNotSeeIt()
    {
        Seed(database => Recycle(database, Entry(database, "stripe")), "stripe");
        using var open = Vault.Open(VaultPath, _password);
        Assert.Equal(RestoreOutcome.Restored, open.RestoreRecycled(Assert.Single(open.ReadRecycled()).Id));
        InFile(FilePath, database => Purge(database, "stripe", _later));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal((Group(database, _work).Uuid, _later.AddSeconds(1)), (entry.ParentGroup.Uuid, entry.LastModificationTime));
            Assert.Empty(database.DeletedObjects);
        });
        Assert.Equal(new MergeKeptItem(_stripe, DeletedInFile: true, Recycled: false), Assert.Single(report.Kept));
    }

    [Fact]
    public void AGroupDeletedInTheFile_StaysWhileTheMergePutsAnEntryInIt()
    {
        Seed(database => AddEntry(AddGroup(database, "old"), "legacy", _seeded));
        using var open = Vault.Open(VaultPath, _password);
        open.AddEntry(new VaultEntry { Title = "fresh", GroupPath = "old", Password = "added" });
        var legacy = PwUuid.Zero;
        InFile(FilePath, database =>
        {
            legacy = Purge(database, "legacy", _later);
            var old = Group(database, "old");
            old.ParentGroup.Groups.Remove(old);
            database.DeletedObjects.Add(new PwDeletedObject(old.Uuid, _later));
        });

        Merge(open);

        Read(FilePath, database =>
        {
            Assert.Equal(["fresh"], Group(database, "old").Entries.Select(Title));
            Assert.Equal(legacy, Assert.Single(database.DeletedObjects).Uuid);
        });
    }

    /// <summary>Each copy made its own bin: the file's is the result's, and what the open copy's held moves into it with where it came from (D-0541).</summary>
    [Fact]
    public void TwoRecycleBins_BecomeTheFilesBin_AndNothingInEitherIsListed()
    {
        Seed("stripe", "github");
        using var open = Vault.Open(VaultPath, _password);
        Assert.Equal(DeletionOutcome.Recycled, open.RemoveEntry(_stripe, out _));
        var theirBin = PwUuid.Zero;
        InFile(FilePath, database =>
        {
            Recycle(database, Entry(database, "github"));
            theirBin = database.RecycleBinUuid;
        });

        using (var merged = MergeKeeping(open, out _))
        {
            Assert.Empty(merged.ReadEntries());
            Assert.Equal(["github", "stripe"], merged.ReadRecycled().Select(recycled => recycled.Title).Order(StringComparer.Ordinal));
            merged.Save();
        }

        Read(FilePath, database =>
        {
            Assert.Equal(theirBin, database.RecycleBinUuid);
            Assert.All(new[] { "stripe", "github" }.Select(title => Entry(database, title)), entry =>
            {
                Assert.Equal(theirBin, entry.ParentGroup.Uuid);
                Assert.Equal(Group(database, _work).Uuid, entry.PreviousParentGroup);
            });
        });
    }

    /// <summary>Attachments travel inside the versions that hold them, never re-encoded (D-0541, V.8a).</summary>
    [Fact]
    public void AnAttachmentNeitherSideEdited_IsKeptByteForByte_CurrentAndInHistory()
    {
        var statement = RandomNumberGenerator.GetBytes(3000);
        var key = RandomNumberGenerator.GetBytes(300);
        Seed("stripe", "github");
        using (var vault = Vault.Open(VaultPath, _password))
        {
            using MemoryStream content = new(statement);
            Assert.True(vault.AddAttachment(_stripe, "statement.pdf", content, out _));
            Update(vault, "stripe", "second");
            vault.Save();
        }

        File.Copy(VaultPath, FilePath, overwrite: true);
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        using (MemoryStream content = new(key))
        {
            Assert.True(open.AddAttachment(new EntryName(_work, "github"), "id.pem", content, out _));
        }

        InFile(FilePath, database => Edit(database, "stripe", _later, entry => entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "theirs"))));

        Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            var holding = entry.History.Append(entry).Where(version => version.Binaries.Get("statement.pdf") is not null).ToList();
            Assert.Equal([Value("stripe", 0), "second", "mine", "second"], holding.Select(Password));
            Assert.All(holding, version => Assert.Equal(statement, version.Binaries.Get("statement.pdf").ReadData()));
            Assert.Equal(key, Entry(database, "github").Binaries.Get("id.pem").ReadData());
        });
    }

    [Fact]
    public void EntryCustomData_TravelsWithItsVersion()
    {
        Seed(database => Entry(database, "stripe").CustomData.Set("KPXC_note", "base"), "stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database => Edit(database, "stripe", _later, entry => entry.CustomData.Set("KPXC_note", "theirs")));

        Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            Assert.Equal("theirs", entry.CustomData.Get("KPXC_note"));
            Assert.Equal("base", Assert.Single(entry.History, version => Password(version) == "mine").CustomData.Get("KPXC_note"));
        });
    }

    /// <summary>The result is built inside the file's parse, so its meta, cipher and key derivation are the file's (D-0541).</summary>
    [Fact]
    public void TheFilesMetaAndHeader_AreKept()
    {
        Seed("stripe", "github");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database =>
        {
            database.Name = "theirs";
            database.Description = "their notes";
            database.DefaultUserName = "them";
            database.HistoryMaxItems = 7;
            database.DataCipherUuid = new ChaCha20Engine().CipherUuid;
            database.KdfParameters.SetUInt64(Argon2Kdf.ParamIterations, 2);
            Edit(database, "github", _later, entry => SetPassword(entry, "theirs"));
        });

        Merge(open);

        Read(FilePath, database =>
        {
            Assert.Equal(("theirs", "their notes", "them", 7), (database.Name, database.Description, database.DefaultUserName, database.HistoryMaxItems));
            Assert.Equal(new ChaCha20Engine().CipherUuid, database.DataCipherUuid);
            Assert.Equal(2UL, database.KdfParameters.GetUInt64(Argon2Kdf.ParamIterations, 0));
            Assert.Equal("mine", Password(Entry(database, "stripe")));
        });
    }

    [Fact]
    public void TheMergedFile_StaysKdbx40_UnlessItHolds41Content()
    {
        Seed("stripe", "github");

        using (var open = Vault.Open(VaultPath, _password))
        {
            Update(open, "stripe", "mine");
            InFile(FilePath, database => Edit(database, "github", _later, entry => SetPassword(entry, "theirs")));
            Merge(open);
        }

        Assert.Equal("KeePass database 4.0", KeePassInterop.Probe(FilePath, out _)!.Version);

        // An item kept in the bin carries where it came from, which only KDBX 4.1 writes (D-0247).
        using (var open = Vault.Open(VaultPath, _password))
        {
            Update(open, "github", "mine");
            InFile(FilePath, database => Purge(database, "github", _later.AddMinutes(1)));
            Merge(open);
        }

        Assert.Equal("KeePass database 4.1", KeePassInterop.Probe(FilePath, out _)!.Version);
    }

    /// <summary>An item's Auto-Type settings and window associations travel with its version, as P.4a reads them.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEntrysAutoTypeSettings_TravelWithItsVersion(bool fileNewer)
    {
        Seed(
            database =>
            {
                var autoType = Entry(database, "stripe").AutoType;
                autoType.DefaultSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}";
                autoType.Add(new AutoTypeAssociation("*Firefox*", "{PASSWORD}{ENTER}"));
            },
            "stripe");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        InFile(FilePath, database => Edit(database, "stripe", fileNewer ? _later : _seeded.AddHours(1), entry =>
        {
            SetPassword(entry, "theirs");
            entry.AutoType.Enabled = false;
            entry.AutoType.ObfuscationOptions = AutoTypeObfuscationOptions.UseClipboard;
            entry.AutoType.DefaultSequence = "{PASSWORD}{ENTER}";
            entry.AutoType.Clear();
            entry.AutoType.Add(new AutoTypeAssociation("//Chrome//", string.Empty));
        }));

        Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, "stripe");
            var theirs = Password(entry) == "theirs" ? entry : Assert.Single(entry.History, version => Password(version) == "theirs");
            var mine = Password(entry) == "mine" ? entry : Assert.Single(entry.History, version => Password(version) == "mine");
            Assert.Equal(fileNewer ? "theirs" : "mine", Password(entry));
            Assert.Equal(
                (false, AutoTypeObfuscationOptions.UseClipboard, "{PASSWORD}{ENTER}", "//Chrome//"),
                (theirs.AutoType.Enabled, theirs.AutoType.ObfuscationOptions, theirs.AutoType.DefaultSequence, Assert.Single(theirs.AutoType.Associations).WindowName));
            Assert.Equal(
                (true, AutoTypeObfuscationOptions.None, "{USERNAME}{TAB}{PASSWORD}{ENTER}", "*Firefox*"),
                (mine.AutoType.Enabled, mine.AutoType.ObfuscationOptions, mine.AutoType.DefaultSequence, Assert.Single(mine.AutoType.Associations).WindowName));
        });
    }

    /// <summary>
    /// A group both copies changed is the newer whole, its Auto-Type setting, default sequence, tags and custom data with it, and KDBX
    /// keeps no group history, so the report names the name the other version gave it (D-0537).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGroupChangedInBoth_IsTheNewerWhole_AndTheNameItLostIsNamed(bool fileNewer)
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        Assert.Equal(GroupOutcome.Renamed, open.RenameGroup(_work, "billing", out _));
        InFile(FilePath, database =>
        {
            var work = Group(database, _work);
            work.EnableAutoType = false;
            work.DefaultAutoTypeSequence = "{USERNAME}{ENTER}";
            work.Tags.Add("finance");
            work.CustomData.Set("KPXC_group", "theirs");
            work.Notes = "their notes";
            work.LastModificationTime = fileNewer ? _later : _seeded.AddHours(1);
        });

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var group = Assert.Single(database.RootGroup.Groups);
            if (fileNewer)
            {
                Assert.Equal((_work, false, "{USERNAME}{ENTER}", "their notes"), (group.Name, group.EnableAutoType, group.DefaultAutoTypeSequence, group.Notes));
                Assert.Equal(["finance"], group.Tags);
                Assert.Equal("theirs", group.CustomData.Get("KPXC_group"));
            }
            else
            {
                Assert.Equal(("billing", (bool?)null, string.Empty, string.Empty), (group.Name, group.EnableAutoType, group.DefaultAutoTypeSequence, group.Notes));
                Assert.Empty(group.Tags);
                Assert.Empty(group.CustomData);
            }
        });
        Assert.Equal(fileNewer ? new MergeRenamedGroup(_work, "billing") : new MergeRenamedGroup("billing", _work), Assert.Single(report.RenamedGroups));
    }

    /// <summary>Agents' records merge by the ordinary rules, the newer whole and the older in its history, and the report names the agent (D-0545).</summary>
    [Fact]
    public void AnAgentsRecordChangedInBoth_IsTheNewerWhole_WithTheOlderInItsHistory()
    {
        const string agent = "claude-code";
        var theirs = AgentAccess.Defaults with { Mode = AgentMode.Ask, Values = false };
        Seed("stripe");
        using (var vault = Vault.Open(VaultPath, _password))
        {
            Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(vault).Write(agent, Automatic));
            vault.Save();
        }

        File.Copy(VaultPath, FilePath, overwrite: true);
        using var open = Vault.Open(VaultPath, _password);
        Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(open).Write(agent, Asks));
        InFile(FilePath, database => Edit(database, agent, _later, entry => entry.Strings.Set(AgentAccessStore.Field, new ProtectedString(false, AcafRecord.Write(theirs)))));

        var report = Merge(open);

        Read(FilePath, database =>
        {
            var entry = Entry(database, agent);
            Assert.Equal(AcafRecord.Write(theirs), entry.Strings.ReadSafe(AgentAccessStore.Field));
            Assert.Contains(AcafRecord.Write(Asks), entry.History.Select(version => version.Strings.ReadSafe(AgentAccessStore.Field)));
        });
        Assert.Equal([agent], report.Agents);
        Assert.Empty(report.Conflicts);

        using var merged = Vault.Open(FilePath, _password);
        Assert.Equal(theirs, Assert.Single(new AgentAccessStore(merged).Read(agent).Own).Access);
    }

    /// <summary>
    /// A tombstone wins over a pairing's record whatever the times or changes, as over a token's: one copy removed the pairing and the
    /// other changed its record afterwards, which would keep an ordinary item in the bin, or in place in a vault that does not recycle (D-0540, D-0633).
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ATombstone_WinsOverAPairingsRecord_ThatTheOtherCopyChanged(bool removedHere, bool recycles)
    {
        const string pairing = "Chrome on desk";
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var record = new EntryName(ReservedGroups.Browsers, pairing);
        Seed("stripe");
        using (var vault = Vault.Open(VaultPath, _password))
        {
            vault.SetRecyclesDeletedEntries(recycles);
            Assert.True(new BrowserPairings(vault).TryAdd(pairing, BrowserIdentity.ChromeWebStore, key, DateTimeOffset.UtcNow, out _));
            vault.Save();
        }

        File.Copy(VaultPath, FilePath, overwrite: true);
        using var open = Vault.Open(VaultPath, _password);

        if (removedHere)
        {
            Assert.True(new BrowserPairings(open).Remove(pairing, out _));
            InFile(FilePath, database => Edit(database, pairing, _later, entry => entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.com"))));
        }
        else
        {
            Assert.True(open.UpdateEntry(open.Find(record)! with { Url = "https://example.com" }));
            InFile(FilePath, database => Purge(database, pairing, _later));
        }

        var report = Merge(open);

        using var merged = Vault.Open(FilePath, _password);
        Assert.Null(merged.Find(record));
        Assert.Empty(merged.ReadRecycled());
        Assert.Empty(report.Kept);
        Assert.Equal(1, merged.TombstoneCount);
        Assert.Equal(PairingCheck.Unknown, new BrowserPairings(merged).Verify(pairing, BrowserIdentity.ChromeWebStore, key));
    }

    [Fact]
    public void AFileOfAnotherVault_IsNotMerged()
    {
        Seed("stripe");
        var other = Path.Combine(_directory, "other.kdbx");
        using (var vault = Vault.Create(other, _password))
        {
            vault.AddEntry(new VaultEntry { Title = "stripe", GroupPath = _work, Password = "other" });
            vault.Save();
        }

        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        using var file = Vault.Open(other, _password);

        Assert.Equal(VaultMergeOutcome.AnotherVault, open.MergeInto(file, out var report));

        Assert.Null(report);
        Assert.Equal("other", file.Find(_stripe)!.Password);
        Assert.Equal(VaultSaveStatus.Saved, file.SaveState().Status);
    }

    [Fact]
    public void AFileHoldingTwoObjectsUnderOneUuid_IsNotMerged()
    {
        Seed("stripe");
        using var open = Vault.Open(VaultPath, _password);
        InFile(FilePath, database =>
        {
            var twin = Entry(database, "stripe").CloneDeep();
            twin.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "twin"));
            Group(database, _work).AddEntry(twin, true);
        });
        using var file = Vault.Open(FilePath, _password);

        Assert.Equal(VaultMergeOutcome.DuplicateUuids, open.MergeInto(file, out var report));

        Assert.Null(report);
        Assert.Equal(VaultSaveStatus.Saved, file.SaveState().Status);
    }

    /// <summary>The result is built inside the file's copy, and the open copy is only read (D-0537).</summary>
    [Fact]
    public void TheOpenCopy_IsLeftAsItWas()
    {
        Seed("stripe", "github");
        using var open = Vault.Open(VaultPath, _password);
        Update(open, "stripe", "mine");
        Purge(open, "github");
        var entries = open.ReadEntries(includeReserved: true);
        var history = Revisions(open.ReadHistory(_stripe)!);
        var tombstones = open.TombstoneCount;
        InFile(FilePath, database =>
        {
            Edit(database, "stripe", _later, entry => SetPassword(entry, "theirs"));
            Edit(database, "github", _later, entry => SetPassword(entry, "theirs"));
        });

        Merge(open);

        Assert.Equal(entries, open.ReadEntries(includeReserved: true));
        Assert.Equal(history, Revisions(open.ReadHistory(_stripe)!));
        Assert.Equal(tombstones, open.TombstoneCount);
        Assert.Equal(VaultSaveStatus.Unsaved, open.SaveState().Status);
    }

    /// <summary>
    /// On the cases both merges share, with no tie, no deletion against a change the deleting copy never saw, no versions sharing a second
    /// and no change only the file made stamped earlier, KeePassLib's synchronize of the file with the open copy gives keypaste's result.
    /// </summary>
    [Fact]
    public void OnTheCasesTheyShare_TheMergeEqualsKeePassLibsSynchronize()
    {
        Seed(
            database =>
            {
                AddGroup(database, "home");
                Recycle(database, Entry(database, "scrap"));
            },
            "mine-only",
            "theirs-only",
            "both-mine-later",
            "both-theirs-later",
            "mine-moves",
            "theirs-moves",
            "mine-recycles",
            "mine-purges",
            "theirs-purges",
            "untouched",
            "scrap");
        var oracle = Path.Combine(_directory, "oracle.kdbx");

        using (var open = Vault.Open(VaultPath, _password))
        {
            Update(open, "mine-only", "mine");
            Update(open, "both-mine-later", "mine");
            Update(open, "both-theirs-later", "mine");
            Move(open, "mine-moves", "home");
            Assert.Equal(DeletionOutcome.Recycled, open.RemoveEntry(new EntryName(_work, "mine-recycles"), out _));
            Purge(open, "mine-purges");
            open.AddEntry(new VaultEntry { Title = "mine-adds", GroupPath = _work, Password = "mine" });
            InFile(FilePath, database =>
            {
                Edit(database, "theirs-only", _later, entry => SetPassword(entry, "theirs"));
                Edit(database, "both-mine-later", _seeded.AddHours(1), entry => SetPassword(entry, "theirs"));
                Edit(database, "both-theirs-later", _later, entry => SetPassword(entry, "theirs"));
                MoveTo(Entry(database, "theirs-moves"), Group(database, "home"), _later);
                Purge(database, "theirs-purges", _later);
                AddEntry(Group(database, "home"), "theirs-adds", _later);
            });
            File.Copy(FilePath, oracle);

            Merge(open);
            open.Save();
        }

        Synchronize(oracle, VaultPath);

        Assert.Equal(Snapshot(oracle), Snapshot(FilePath));
    }

    /// <summary>
    /// The merge stamps only what keeps its choices stable under time-based merges, so KeePassLib's synchronize of the result with either
    /// input changes nothing, ties and deletions against unseen changes included (D-0539).
    /// </summary>
    [Fact]
    public void SynchronizingTheResultWithEitherInput_ChangesNothing()
    {
        Seed(
            database =>
            {
                AddGroup(database, "home");
                AddGroup(database, "away");
                Recycle(database, Entry(database, "restored"));
            },
            "tie",
            "both",
            "kept-here",
            "kept-there",
            "recycled-here",
            "restored",
            "moved-both");
        var theirs = Path.Combine(_directory, "theirs.kdbx");

        using (var open = Vault.Open(VaultPath, _password))
        {
            Update(open, "tie", "mine");
            var tie = Whole(open.ReadTimes(new EntryName(_work, "tie"))!.Modified.UtcDateTime);
            Update(open, "both", "mine");
            Update(open, "kept-here", "mine");
            Purge(open, "kept-there");
            Assert.Equal(DeletionOutcome.Recycled, open.RemoveEntry(new EntryName(_work, "recycled-here"), out _));
            Assert.Equal(RestoreOutcome.Restored, open.RestoreRecycled(open.ReadRecycled().Single(recycled => recycled.Title == "restored").Id));
            Move(open, "moved-both", "home");
            InFile(FilePath, database =>
            {
                Edit(database, "tie", tie, entry => SetPassword(entry, "theirs"));
                Edit(database, "both", _later, entry => SetPassword(entry, "theirs"));
                Purge(database, "kept-here", _later);
                Edit(database, "kept-there", _later, entry => SetPassword(entry, "theirs"));
                Edit(database, "recycled-here", _later, entry => SetPassword(entry, "theirs"));
                Purge(database, "restored", _later);
                MoveTo(Entry(database, "moved-both"), Group(database, "away"), _later);
            });
            File.Copy(FilePath, theirs);

            Merge(open);
            open.Save();
        }

        var result = Snapshot(FilePath);

        foreach (var input in new[] { VaultPath, theirs })
        {
            var synchronized = Path.Combine(_directory, "synchronized-" + Path.GetFileName(input));
            File.Copy(FilePath, synchronized);
            Synchronize(synchronized, input);
            Assert.Equal(result, Snapshot(synchronized));
        }
    }

    /// <summary>Writes the vault every test starts from, an item per title in <c>work</c> and everything stamped <see cref="_seeded"/>, and the other program's copy of it.</summary>
    private void Seed(params string[] titles) => Seed(_ => { }, titles);

    private void Seed(Action<PwDatabase> shape, params string[] titles)
    {
        using (var vault = Vault.Create(VaultPath, _password))
        {
            foreach (var title in titles)
            {
                vault.AddEntry(new VaultEntry { Title = title, GroupPath = _work, Username = "user", Password = Value(title, 0) });
            }

            vault.Save();
        }

        InFile(VaultPath, database =>
        {
            foreach (var group in database.RootGroup.GetGroups(true).Append(database.RootGroup))
            {
                group.CreationTime = group.LastModificationTime = group.LocationChanged = _seeded;
            }

            foreach (var entry in database.RootGroup.GetEntries(true))
            {
                entry.CreationTime = entry.LastModificationTime = entry.LocationChanged = _seeded;
            }

            shape(database);
        });

        File.Copy(VaultPath, FilePath);
    }

    /// <summary>Merges the open copy into a fresh open of the file the other program saved, which the caller disposes; nothing is saved.</summary>
    private Vault MergeKeeping(Vault open, out VaultMergeReport report)
    {
        var file = Vault.Open(FilePath, _password);

        try
        {
            Assert.Equal(VaultMergeOutcome.Merged, open.MergeInto(file, out var merged));
            Assert.NotNull(merged);
            report = merged;
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Merges the open copy into the file the other program saved and saves the result there.</summary>
    private VaultMergeReport Merge(Vault open)
    {
        using var file = MergeKeeping(open, out var report);
        file.Save();
        return report;
    }

    private static void Update(Vault vault, string title, string password) =>
        Assert.True(vault.UpdateEntry(new VaultEntry { Title = title, GroupPath = _work, Username = "user", Password = password }));

    private static void Move(Vault vault, string title, string groupPath) =>
        Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(new EntryName(_work, title), groupPath, out _));

    private static void Purge(Vault vault, string title)
    {
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName(_work, title), out var recycled));
        Assert.True(vault.PurgeRecycled(recycled));
    }

    /// <summary>Works on a closed vault's file as another KeePass program would, and saves it.</summary>
    private static void InFile(string path, Action<PwDatabase> work) => Open(path, database =>
    {
        work(database);
        database.Save(null);
    });

    private static void Read(string path, Action<PwDatabase> check) => Open(path, check);

    private static void Open(string path, Action<PwDatabase> use)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(_password), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            use(database);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>KeePass's synchronize of one closed file with another, saved into the first.</summary>
    private static void Synchronize(string target, string source) =>
        InFile(target, into => Read(source, from => into.MergeIn(from, PwMergeMethod.Synchronize)));

    /// <summary>An edit as KeePass saves one: the entry as it was into its history, then the change, stamped as given.</summary>
    private static void Edit(PwDatabase database, string title, DateTime at, Action<PwEntry> change)
    {
        var entry = Entry(database, title);
        entry.CreateBackup(database);
        change(entry);
        entry.LastModificationTime = at;
    }

    /// <summary>Gives an entry <paramref name="count"/> later versions, a second apart after the seeded one.</summary>
    private void Revise(PwDatabase database, string title, int count)
    {
        var entry = Entry(database, title);

        for (var version = 1; version <= count; version++)
        {
            entry.CreateBackup(null);
            SetPassword(entry, Value(title, version));
            entry.LastModificationTime = _seeded.AddSeconds(version);
        }
    }

    /// <summary>Deletes an entry for good as KeePass does, leaving its tombstone.</summary>
    private static PwUuid Purge(PwDatabase database, string title, DateTime at)
    {
        var entry = Entry(database, title);
        entry.ParentGroup.Entries.Remove(entry);
        database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, at));
        return entry.Uuid;
    }

    /// <summary>Moves an entry into the vault's recycle bin as KeePass recycles one, making the bin when there is none.</summary>
    private void Recycle(PwDatabase database, PwEntry entry)
    {
        var bin = database.RecycleBinUuid.IsZero ? null : database.RootGroup.FindGroup(database.RecycleBinUuid, true);

        if (bin is null)
        {
            bin = AddGroup(database, "Recycle Bin");
            database.RecycleBinUuid = bin.Uuid;
            database.RecycleBinChanged = _seeded;
        }

        var from = entry.ParentGroup;
        MoveTo(entry, bin, _seeded);
        entry.PreviousParentGroup = from.Uuid;
    }

    private PwGroup AddGroup(PwDatabase database, string name)
    {
        PwGroup group = new(true, true, name, PwIcon.Folder) { CreationTime = _seeded, LastModificationTime = _seeded, LocationChanged = _seeded };
        database.RootGroup.AddGroup(group, true);
        return group;
    }

    private static PwEntry AddEntry(PwGroup group, string title, DateTime at)
    {
        PwEntry entry = new(true, true) { CreationTime = at, LastModificationTime = at, LocationChanged = at };
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Value(title, 0)));
        group.AddEntry(entry, true);
        return entry;
    }

    private static void MoveTo(PwEntry entry, PwGroup group, DateTime at)
    {
        entry.ParentGroup.Entries.Remove(entry);
        group.AddEntry(entry, true);
        entry.LocationChanged = at;
    }

    private static PwEntry Entry(PwDatabase database, string title) =>
        database.RootGroup.GetEntries(true).Single(entry => Title(entry) == title);

    private static PwGroup Group(PwDatabase database, string name) =>
        database.RootGroup.GetGroups(true).Single(group => group.Name == name);

    private static void SetPassword(PwEntry entry, string password) => entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));

    private static string Title(PwEntry entry) => entry.Strings.ReadSafe(PwDefs.TitleField);

    private static string Password(PwEntry entry) => entry.Strings.ReadSafe(PwDefs.PasswordField);

    private static List<string> HistoryPasswords(PwEntry entry) => [.. entry.History.Select(Password)];

    private static List<(DateTime, VaultEntry)> Revisions(IReadOnlyList<EntryRevision> revisions) =>
        [.. revisions.Select(revision => (revision.ModifiedUtc, revision.Fields))];

    private static string Value(string title, int version) => $"{title}-{version}";

    private static DateTime Whole(DateTime time) => new(time.Ticks - (time.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    private static long Second(DateTime time) => time.Ticks / TimeSpan.TicksPerSecond;

    /// <summary>What two merges are compared by: each group's and entry's place, each entry's current values and time and its history, the tombstones and the bin.</summary>
    private static List<string> Snapshot(string path)
    {
        List<string> lines = [];

        Read(path, database =>
        {
            foreach (var group in database.RootGroup.GetGroups(true))
            {
                lines.Add($"group {group.Uuid.ToHexString()} in {group.ParentGroup.Uuid.ToHexString()} {group.Name}");
            }

            foreach (var entry in database.RootGroup.GetEntries(true))
            {
                var history = string.Join(",", entry.History.Select(version => $"{Second(version.LastModificationTime)}:{Password(version)}").Order(StringComparer.Ordinal));
                lines.Add(
                    $"entry {entry.Uuid.ToHexString()} in {entry.ParentGroup.Uuid.ToHexString()} at {Second(entry.LocationChanged)} from {entry.PreviousParentGroup.ToHexString()}: "
                    + $"{Title(entry)} {entry.Strings.ReadSafe(PwDefs.UserNameField)} {Password(entry)} {Second(entry.LastModificationTime)} [{history}]");
            }

            foreach (var tombstone in database.DeletedObjects)
            {
                lines.Add($"deleted {tombstone.Uuid.ToHexString()}");
            }

            lines.Add($"bin {database.RecycleBinUuid.ToHexString()}");
        });

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }
}
