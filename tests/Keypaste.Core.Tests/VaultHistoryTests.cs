using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Reading and restoring entry history (docs/STEPS.md V.2a).
/// </summary>
/// <remarks>
/// Retaining a replaced value has been the promise since D-0014; until now nothing could read one
/// back, so the promise rested on a counter. These tests are what make the value itself, its age
/// and its order assertable, and what stop a restore from being an edit that loses the thing it
/// replaced.
/// </remarks>
public sealed class VaultHistoryTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private static readonly EntryName _production = new("servers", "production");

    private readonly string _directory;

    public VaultHistoryTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-history-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// The whole of what a read promises: every revision, newest first, each carrying the values it
    /// held and when it held them, and the same answer before and after the file has been through
    /// KDBX.
    /// </summary>
    [Fact]
    public void ReadHistory_AfterThreeUpdates_ReturnsEveryRevisionNewestFirst()
    {
        var path = Seed(out var beforeSaving);

        using var vault = Vault.Open(path, MasterPassword);
        var revisions = vault.ReadHistory(_production);

        Assert.NotNull(revisions);
        Assert.Equal([0, 1, 2], revisions.Select(revision => revision.Index));
        Assert.Equal(["v2", "v1", "v0"], revisions.Select(revision => revision.Fields.Password));
        Assert.Equal(["u2", "u1", "u0"], revisions.Select(revision => revision.Fields.Username));

        Assert.All(revisions, revision =>
        {
            Assert.Equal(DateTimeKind.Utc, revision.ModifiedUtc.Kind);
            Assert.True(revision.ModifiedUtc <= DateTime.UtcNow);
            Assert.Equal("servers", revision.Fields.GroupPath, StringComparer.Ordinal);
            Assert.Equal("production", revision.Fields.Title, StringComparer.Ordinal);
        });

        Assert.Equal(revisions.Select(revision => revision.ModifiedUtc).Order().Reverse(),
            revisions.Select(revision => revision.ModifiedUtc));

        // The file is what collapses the times onto whole seconds, so the values and their order
        // are what can be compared across it, and a re-sort on the read path would show up here.
        Assert.Equal(beforeSaving.Select(revision => revision.Fields), revisions.Select(revision => revision.Fields));
    }

    /// <summary>
    /// KDBX4 writes a timestamp to the second, so three updates a microsecond apart come back from
    /// a reopened file with one time between them. Order is then the file's, newest last, and a
    /// stable sort left to itself returns exactly the wrong end first.
    /// </summary>
    [Fact]
    public void ReadHistory_AfterAReopenCollapsesTheTimesOntoOneSecond_StillOrdersNewestFirst()
    {
        IReadOnlyList<EntryRevision>? revisions = null;

        for (var attempt = 0; attempt < 3 && !SharesATime(revisions); attempt++)
        {
            // Seeded just after a second boundary, so three updates and a save land before the next
            // one: the tie is the condition under test, not an accident of when this ran.
            while (DateTime.UtcNow.Millisecond > 50)
            {
                Thread.Yield();
            }

            using var vault = Vault.Open(Seed(out _), MasterPassword);
            revisions = vault.ReadHistory(_production);
        }

        Assert.True(SharesATime(revisions), "the three revisions did not land inside one second");
        Assert.NotNull(revisions);
        Assert.Equal([0, 1, 2], revisions.Select(revision => revision.Index));
        Assert.Equal(["v2", "v1", "v0"], revisions.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// The restore itself: the oldest value becomes current, what it displaced becomes the newest
    /// history item rather than being lost (D-0014), and the entry is the same entry throughout.
    /// </summary>
    [Fact]
    public void RestoreRevision_PutsTheOldestRevisionBack_AndKeepsTheUuid()
    {
        var path = Seed(out _);
        string? uuid;

        using (var vault = Vault.Open(path, MasterPassword))
        {
            uuid = vault.EntryUuid(_production);
            Assert.NotNull(uuid);

            Assert.True(vault.RestoreRevision(_production, 2));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var current = reopened.Find(_production);

        Assert.Equal("v0", current?.Password, StringComparer.Ordinal);
        Assert.Equal("u0", current?.Username, StringComparer.Ordinal);
        Assert.Equal(uuid, reopened.EntryUuid(_production), StringComparer.Ordinal);

        var revisions = reopened.ReadHistory(_production);
        Assert.NotNull(revisions);
        Assert.Equal(["v3", "v2", "v1", "v0"], revisions.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// A restore records when it happened (D-0227). <c>RestoreFromBackup</c> assigns the old
    /// revision's timestamps back, which would leave the entry older than the value it replaced —
    /// so a merge would revert it and eviction, which drops the oldest, would take it first. The
    /// next edit is where that time becomes visible: it backs the restored value up carrying it,
    /// and an untouched restore puts that item at the far end of the history instead of the near
    /// one.
    /// </summary>
    [Fact]
    public void RestoreRevision_StampsTheEntryWithTheMomentOfTheRestore()
    {
        using var vault = Vault.Open(Seed(out _), MasterPassword);

        Assert.True(vault.RestoreRevision(_production, 2));
        vault.UpdateEntry(new VaultEntry { Title = "production", Password = "v4", GroupPath = "servers" });

        var revisions = vault.ReadHistory(_production);

        Assert.NotNull(revisions);
        Assert.Equal("v0", revisions[0].Fields.Password, StringComparer.Ordinal);
        Assert.True(revisions[0].ModifiedUtc > revisions[1].ModifiedUtc);
    }

    /// <summary>
    /// A restore is a change to the open vault and nothing more until it is saved, exactly as an
    /// edit is. Another reader of the same path still sees what is on disk.
    /// </summary>
    [Fact]
    public void RestoreRevision_LeavesTheFileAloneUntilSave()
    {
        var path = Seed(out _);
        var bytes = File.ReadAllBytes(path);

        using var vault = Vault.Open(path, MasterPassword);
        Assert.True(vault.RestoreRevision(_production, 2));
        Assert.Equal("v0", vault.Find(_production)?.Password, StringComparer.Ordinal);

        Assert.Equal(bytes, File.ReadAllBytes(path));

        using var other = Vault.Open(path, MasterPassword);
        Assert.Equal("v3", other.Find(_production)?.Password, StringComparer.Ordinal);
        Assert.Equal(3, other.ReadHistory(_production)?.Count);
    }

    /// <summary>
    /// A restore costs a history item like any other edit, so at the cap it evicts one — and the one
    /// it evicts is the revision being restored, because that is the oldest. Surprising in a list,
    /// which is why V.2b should be written against it rather than against an assumption.
    /// </summary>
    [Fact]
    public void RestoreRevision_KeepsTheHistoryWithinHistoryMaxItems()
    {
        var path = NewVaultPath();

        // One session throughout: in-memory times differ by ticks, so "the oldest" is unambiguous to
        // the eviction rule, which a reopened file's one-second resolution would not be.
        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "production", Password = "v0", GroupPath = "servers" });

            for (var revision = 1; revision <= 12; revision++)
            {
                vault.UpdateEntry(new VaultEntry
                {
                    Title = "production",
                    Password = $"v{revision}",
                    GroupPath = "servers",
                });
            }

            Assert.Equal(10, vault.ReadHistory(_production)?.Count);

            Assert.True(vault.RestoreRevision(_production, 9));
            var revisions = vault.ReadHistory(_production);

            Assert.NotNull(revisions);
            Assert.Equal(10, revisions.Count);
            Assert.Equal("v2", vault.Find(_production)?.Password, StringComparer.Ordinal);
            Assert.Equal("v12", revisions[0].Fields.Password, StringComparer.Ordinal);
            Assert.DoesNotContain("v2", revisions.Select(revision => revision.Fields.Password));

            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal(10, reopened.ReadHistory(_production)?.Count);
    }

    /// <summary>
    /// An index the read never handed out is a caller's mistake, and a mistake must not cost the
    /// entry a history item on the way to being reported.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void RestoreRevision_WithAnIndexOutsideTheHistory_IsRefusedAndWritesNothing(int index)
    {
        AssertWritesNothing(Seed(out _), vault =>
            Assert.Throws<ArgumentOutOfRangeException>(() => vault.RestoreRevision(_production, index)));
    }

    /// <summary>An entry nobody has changed yet has no revision 0 either.</summary>
    [Fact]
    public void RestoreRevision_OnAnEntryWithNoHistory_IsRefused()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "production", Password = "v0", GroupPath = "servers" });

        Assert.Throws<ArgumentOutOfRangeException>(() => vault.RestoreRevision(_production, 0));
        Assert.Equal("v0", vault.Find(_production)?.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// A name can be stale — the entry was removed in KeePassXC while this vault was open — so
    /// naming nothing is an answer rather than an error, as it is for removing and updating.
    /// </summary>
    [Fact]
    public void RestoreRevision_OnAnEntryTheVaultDoesNotHave_IsRefusedAndWritesNothing()
    {
        var absent = new EntryName("servers", "absent");

        AssertWritesNothing(Seed(out _), vault =>
        {
            Assert.False(vault.RestoreRevision(absent, 0));
            Assert.Null(vault.ReadHistory(absent));
        });
    }

    /// <summary>
    /// Two entries in one group can share a title, and KeePassXC will make them. Neither reading
    /// history nor restoring it may guess which was meant (docs/PRODUCT.md law 3.7, D-0091).
    /// </summary>
    [Fact]
    public void ReadHistoryAndRestoreRevision_RefuseANameTwoEntriesAnswerTo()
    {
        var path = NewVaultPath();
        var token = new EntryName("env/dev", "TOKEN");

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "first", GroupPath = "env/dev" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "second", GroupPath = "env/dev" });
            vault.Save();
        }

        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Throws<VaultException>(() => vault.ReadHistory(token));
            Assert.Throws<VaultException>(() => vault.RestoreRevision(token, 0));
            Assert.Equal(bytes, File.ReadAllBytes(path));

            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal(["first", "second"], reopened.ReadEntries().Select(entry => entry.Password));
    }

    /// <summary>
    /// The one claim that ties the two operations together: the revision a read showed at an index
    /// is the revision a restore of that index puts back. It fails on any divergence between the
    /// order the read reports and the order the restore addresses, whatever caused it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RestoreRevision_PutsBackTheRevisionTheReadShowedAtThatIndex(int index)
    {
        using var vault = Vault.Open(Seed(out _), MasterPassword);
        var shown = vault.ReadHistory(_production)?[index].Fields;

        Assert.NotNull(shown);
        Assert.True(vault.RestoreRevision(_production, index));
        Assert.Equal(shown, vault.Find(_production));
    }

    /// <summary>
    /// "Nothing has changed yet" and "there is no such entry" are different answers, and a screen
    /// that collapses them tells somebody their history is empty when their entry is gone.
    /// </summary>
    [Fact]
    public void ReadHistory_OnAnEntryWithNoHistory_IsEmptyRatherThanMissing()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "production", Password = "v0", GroupPath = "servers" });

        Assert.Empty(vault.ReadHistory(_production)!);
        Assert.Null(vault.ReadHistory(new EntryName("servers", "absent")));
    }

    /// <summary>
    /// A saved vault whose entry has been through three updates, and the revisions as the writing
    /// session saw them, so a caller can compare those with what survives the file.
    /// </summary>
    /// <summary>
    /// A revision is values, never identity. History items carry the title the entry had when the
    /// revision was taken, and <c>RestoreFromBackup</c> assigns every string back — so without a
    /// guard, restoring an old revision would silently undo a rename nobody asked about, and could
    /// move the entry onto a name something else already answers to (D-0091).
    /// </summary>
    [Fact]
    public void RestoringARevisionTakenBeforeARename_LeavesTheEntryAtItsCurrentName()
    {
        var path = Seed(out _);
        var renamed = new EntryName("servers", "prod");

        using var vault = Vault.Open(path, MasterPassword);

        Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(_production, "prod", out _));
        Assert.True(vault.RestoreRevision(renamed, 0));

        Assert.Null(vault.Find(_production));

        var entry = vault.Find(renamed);
        Assert.NotNull(entry);
        Assert.Equal("v2", entry!.Password, StringComparer.Ordinal);
        Assert.Equal("u2", entry.Username, StringComparer.Ordinal);
    }

    /// <summary>
    /// The value the restore replaced is still kept, so a restore after a rename can itself be
    /// undone — and the entry has still not moved.
    /// </summary>
    [Fact]
    public void ARestoreAfterARename_KeepsWhatItReplaced_AndStillDoesNotMoveTheEntry()
    {
        var path = Seed(out _);
        var renamed = new EntryName("servers", "prod");

        using var vault = Vault.Open(path, MasterPassword);

        Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(_production, "prod", out _));
        Assert.True(vault.RestoreRevision(renamed, 0));

        Assert.Equal("v3", vault.ReadHistory(renamed)![0].Fields.Password, StringComparer.Ordinal);

        Assert.True(vault.RestoreRevision(renamed, 0));
        Assert.Equal("v3", vault.Find(renamed)!.Password, StringComparer.Ordinal);
        Assert.Equal("prod", vault.Find(renamed)!.Title, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reading is unchanged by all of this. <see cref="EntryRevision"/> says a revision carries the
    /// title it held, which is why its path can address nothing; the guard is on the restore, which
    /// writes, and not on the read, which does not.
    /// </summary>
    [Fact]
    public void AfterARename_ARevisionStillReportsTheTitleItHeld()
    {
        var path = Seed(out _);
        var renamed = new EntryName("servers", "prod");

        using var vault = Vault.Open(path, MasterPassword);

        Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(_production, "prod", out _));

        var revisions = vault.ReadHistory(renamed)!;
        Assert.All(revisions, revision => Assert.Equal("production", revision.Fields.Title, StringComparer.Ordinal));
        Assert.All(revisions, revision => Assert.Equal("servers", revision.Fields.GroupPath, StringComparer.Ordinal));
    }

    private string Seed(out IReadOnlyList<EntryRevision> beforeSaving)
    {
        var path = NewVaultPath();
        using var vault = Vault.Create(path, MasterPassword);

        vault.AddEntry(new VaultEntry
        {
            Title = "production",
            Username = "u0",
            Password = "v0",
            GroupPath = "servers",
        });

        for (var revision = 1; revision <= 3; revision++)
        {
            vault.UpdateEntry(new VaultEntry
            {
                Title = "production",
                Username = $"u{revision}",
                Password = $"v{revision}",
                GroupPath = "servers",
            });
        }

        vault.Save();
        beforeSaving = vault.ReadHistory(_production)!;
        return path;
    }

    /// <summary>
    /// Runs a refused operation and checks it left nothing behind: not the file's bytes, not the
    /// open vault's own answers, and not what a save of that vault would then put on disk. The last
    /// is the one that catches a mutation the read path smooths over.
    /// </summary>
    private static void AssertWritesNothing(string path, Action<Vault> refused)
    {
        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            refused(vault);

            Assert.Equal(bytes, File.ReadAllBytes(path));
            AssertUntouched(vault);

            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        AssertUntouched(reopened);
    }

    private static void AssertUntouched(Vault vault)
    {
        Assert.Equal("v3", vault.Find(_production)?.Password, StringComparer.Ordinal);
        Assert.Equal(
            ["v2", "v1", "v0"],
            vault.ReadHistory(_production)!.Select(revision => revision.Fields.Password));
    }

    private static bool SharesATime(IReadOnlyList<EntryRevision>? revisions)
    {
        return revisions is { Count: 3 } && revisions[0].ModifiedUtc == revisions[2].ModifiedUtc;
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }
}
