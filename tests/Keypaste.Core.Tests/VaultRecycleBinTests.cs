using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Deleting an entry used to be the one irreversible thing keypaste did, and the one act that
/// could erase a value it had written. V.3a splits those apart: an ordinary delete is recoverable,
/// and erasing is a second deliberate step through the bin.
/// </summary>
/// <remarks>
/// <para>
/// Two claims carry the rest. <b>A recycled entry is still in the file</b>, with its identity, its
/// fields and its history, or nothing can put it back. <b>Nothing that reads the vault can see
/// it</b> — not a listing, not a lookup, not an env injection and not a credential release — or a
/// deleted credential is still a live one.
/// </para>
/// <para>
/// The second claim is asserted at the seams that actually serve agents, not inferred from the
/// entry's path having moved out from under the default <c>env/**</c> exposure. An exposure of
/// <c>**</c> matches the bin's path too, so the path is not what protects anything.
/// </para>
/// </remarks>
public sealed class VaultRecycleBinTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private static readonly EntryName _token = new("env/billing", "TOKEN");

    private readonly string _directory;

    public VaultRecycleBinTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-recyclebin-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public void RemoveEntry_MovesTheEntryToTheBin_RatherThanRemovingIt()
    {
        using var vault = Seeded(out _);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));

        var recycled = Assert.Single(vault.ReadRecycled());
        Assert.Equal("TOKEN", recycled.Title, StringComparer.Ordinal);
        Assert.Equal("env/billing", recycled.OriginalGroupPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// A tombstone says an object was deleted, and a merge that believed one would delete the
    /// entry in the other copy of the vault. Recycling is not that; purging is.
    /// </summary>
    [Fact]
    public void Recycling_WritesNoTombstone_AndPurgingWritesOne()
    {
        using var vault = Seeded(out _);

        Assert.Equal(0, vault.TombstoneCount);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        Assert.Equal(0, vault.TombstoneCount);

        Assert.True(vault.PurgeRecycled(Assert.Single(vault.ReadRecycled()).Id));
        Assert.Equal(1, vault.TombstoneCount);
    }

    /// <summary>
    /// The identity comes back from the delete that produced it, so a caller offering to undo one
    /// does not have to work out which row it just made (docs/PRODUCT.md §4.2).
    /// </summary>
    [Fact]
    public void RemoveEntry_HandsBackTheIdentityTheBinNowListsForThatEntry()
    {
        using var vault = Seeded(out _);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token, out var recycled));

        Assert.Equal(Assert.Single(vault.ReadRecycled()).Id, recycled);
        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(recycled));
    }

    /// <summary>
    /// Nothing to restore, so nothing is named. A caller checking the outcome first cannot act on
    /// an identity that stands for no row.
    /// </summary>
    [Fact]
    public void RemoveEntry_NamesNoIdentity_WhenItRecycledNothing()
    {
        using var vault = Seeded(out _);

        Assert.Equal(
            DeletionOutcome.NothingMatched,
            vault.RemoveEntry(new EntryName("env/billing", "ABSENT"), out var missing));

        Assert.Equal(default, missing);

        vault.SetRecyclesDeletedEntries(false);

        Assert.Equal(DeletionOutcome.DeletedPermanently, vault.RemoveEntry(_token, out var erased));
        Assert.Equal(default, erased);
    }

    [Fact]
    public void RemoveEntry_MatchesNothing_WhenNoEntryHasThatName()
    {
        using var vault = Seeded(out _);

        Assert.Equal(
            DeletionOutcome.NothingMatched,
            vault.RemoveEntry(new EntryName("env/billing", "ABSENT")));

        Assert.Empty(vault.ReadRecycled());
    }

    [Fact]
    public void AFreshVault_HasNothingToRecover()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);

        Assert.Empty(vault.ReadRecycled());
        Assert.Equal(0, vault.EmptyRecycleBin());
    }

    /// <summary>One bin, however many deletions: a second one would split the trash in two.</summary>
    [Fact]
    public void TwoDeletions_ShareOneBin()
    {
        using var vault = Seeded(out _);
        vault.AddEntry(new VaultEntry { Title = "OTHER", Password = "other", GroupPath = "env/billing" });

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("env/billing", "OTHER")));

        Assert.Equal(2, vault.ReadRecycled().Count);
    }

    // ---------------------------------------------------------------- surviving a reopen

    [Fact]
    public void ARecycledEntry_SurvivesAReopen_WithItsIdentityFieldsAndHistory()
    {
        var path = NewVaultPath();
        string? uuid;

        using (var vault = Seeded(out _, path))
        {
            uuid = vault.EntryUuid(_token);
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var recycled = Assert.Single(reopened.ReadRecycled());

        Assert.Equal("env/billing", recycled.OriginalGroupPath, StringComparer.Ordinal);
        Assert.Equal(RestoreOutcome.Restored, reopened.RestoreRecycled(recycled.Id));

        Assert.Equal("v3", reopened.Find(_token)?.Password, StringComparer.Ordinal);
        Assert.Equal(uuid, reopened.EntryUuid(_token), StringComparer.Ordinal);
        Assert.Equal(
            ["v2", "v1", "v0"],
            reopened.ReadHistory(_token)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// The identity has to be the entry's, not its place in a list. A reopened vault is free to
    /// return the trash in another order, and D-0229 already settled that an index addresses one
    /// reading rather than a thing.
    /// </summary>
    [Fact]
    public void AnIdCapturedBeforeAReopen_StillNamesTheSameEntryAfterOne()
    {
        var path = NewVaultPath();
        RecycledEntryId id;

        using (var vault = Seeded(out _, path))
        {
            vault.AddEntry(new VaultEntry { Title = "FIRST", Password = "first", GroupPath = "env/billing" });
            vault.AddEntry(new VaultEntry { Title = "LAST", Password = "last", GroupPath = "env/billing" });

            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("env/billing", "FIRST")));
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("env/billing", "LAST")));

            id = vault.ReadRecycled().Single(row => string.Equals(row.Title, "TOKEN", StringComparison.Ordinal)).Id;
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Equal(3, reopened.ReadRecycled().Count);
        Assert.Equal(RestoreOutcome.Restored, reopened.RestoreRecycled(id));

        Assert.Equal("v3", reopened.Find(_token)?.Password, StringComparer.Ordinal);
        Assert.DoesNotContain(reopened.ReadRecycled(), row => string.Equals(row.Title, "TOKEN", StringComparison.Ordinal));
    }

    /// <summary>The text form is what a separate process has to carry the identity in.</summary>
    [Fact]
    public void AnIdSurvivesBeingWrittenOutAndReadBack()
    {
        using var vault = Seeded(out _);
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));

        var id = Assert.Single(vault.ReadRecycled()).Id;

        Assert.True(RecycledEntryId.TryParse(id.ToString(), out var parsed));
        Assert.Equal(id, parsed);
        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(parsed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("0123456789ABCDEF0123456789ABCDE")]
    [InlineData("0123456789ABCDEF0123456789ABCDEFF")]
    [InlineData("0123456789ABCDEF0123456789ABCDEZ")]
    public void TextThatIsNotAnId_IsRefused(string? text)
    {
        Assert.False(RecycledEntryId.TryParse(text, out var id));
        Assert.Equal(default, id);
    }

    // ---------------------------------------------------------------- staying out of sight

    [Fact]
    public void ARecycledEntry_IsAbsentFromEveryListingAndLookup()
    {
        using var vault = Seeded(out _);
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));

        Assert.Empty(vault.ReadEntries());
        Assert.Null(vault.Find(_token));
        Assert.Null(vault.Find("env/billing/TOKEN"));
        Assert.Null(vault.ReadHistory(_token));
        Assert.DoesNotContain(vault.ReadGroupPaths(), path => path.Contains("Recycle", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecycledVariable_IsAbsentFromItsProject()
    {
        using var vault = Seeded(out _);
        var store = new EnvStore(vault);

        Assert.Single(store.Read("billing"));

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));

        Assert.Empty(store.Read("billing"));
        Assert.DoesNotContain(store.Projects(), project => project.Contains("Recycle", StringComparison.Ordinal));
    }

    /// <summary>
    /// The seam an MCP client's <c>list_entry_names</c> reaches. Exposed as <c>**</c> on purpose:
    /// the default <c>env/**</c> would stop matching the moved entry by accident, and an accident
    /// is not a control.
    /// </summary>
    [Fact]
    public void ARecycledEntry_IsNotListedToAnAgent_EvenUnderTheWidestExposure()
    {
        using var vault = Seeded(out _);
        vault.Save();
        var lister = new VaultEntryNameLister(() => vault);

        Assert.True(EntryExposure.TryCreate(["**"], out var exposure, out _));
        Assert.True(lister.TryList(exposure!, out var before, out _));
        Assert.Contains(before!, name => name == _token);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        vault.Save();

        Assert.True(lister.TryList(exposure!, out var after, out _));
        Assert.Empty(after!);
    }

    /// <summary>The seam a <c>request_credential</c> reaches, on both of its calls.</summary>
    [Fact]
    public void ARecycledEntry_CannotBeResolvedOrReadForARelease()
    {
        using var vault = Seeded(out _);
        vault.Save();
        var source = new VaultCredentialSource(() => vault);

        Assert.True(source.TryResolve("env/billing/TOKEN", out _, out _));

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        vault.Save();

        Assert.False(source.TryResolve("env/billing/TOKEN", out _, out var resolveFailure));
        Assert.Equal(CredentialFailure.NotFound, resolveFailure);

        Assert.False(source.TryRead(_token, "password", out _, out var readFailure));
        Assert.Equal(CredentialFailure.NotFound, readFailure);
    }

    /// <summary>
    /// The handle is derived from the name, so recycling changes it. Addressing a deleted entry by
    /// the handle it had is refused like any other way of naming it.
    /// </summary>
    [Fact]
    public void ARecycledEntry_CannotBeResolvedByTheHandleItHad()
    {
        using var vault = Seeded(out _);
        vault.Save();
        var handle = EntryHandle.For(_token);
        var source = new VaultCredentialSource(() => vault);

        Assert.True(source.TryResolve(handle, out _, out _));

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        vault.Save();

        Assert.False(source.TryResolve(handle, out _, out var failure));
        Assert.Equal(CredentialFailure.NotFound, failure);
    }

    /// <summary>
    /// An entry written into the bin would be reported created and then be invisible to every read,
    /// and emptying the bin would destroy it.
    /// </summary>
    [Fact]
    public void AddEntry_IntoTheBin_IsRefusedAndWritesNothing()
    {
        using var vault = Seeded(out _);
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        var groups = vault.ReadGroupPaths();

        Assert.Throws<VaultException>(() => vault.AddEntry(new VaultEntry { GroupPath = "Recycle Bin", Title = "Chase", Password = "x" }));
        Assert.Throws<VaultException>(() => vault.AddEntry(new VaultEntry { GroupPath = "Recycle Bin/Sub", Title = "Chase", Password = "x" }));

        Assert.Single(vault.ReadRecycled());
        Assert.Equal(groups, vault.ReadGroupPaths());
        Assert.Null(vault.Find(new EntryName("Recycle Bin", "Chase")));
    }

    // ---------------------------------------------------------------- restoring

    [Fact]
    public void RestoreRecycled_PutsTheEntryBackInTheGroupItCameFrom()
    {
        using var vault = Seeded(out _);
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));

        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(Assert.Single(vault.ReadRecycled()).Id));

        Assert.Equal("v3", vault.Find(_token)?.Password, StringComparer.Ordinal);
        Assert.Empty(vault.ReadRecycled());
        Assert.Single(new EnvStore(vault).Read("billing"));
    }

    /// <summary>
    /// Two entries of one title in two groups is the ordinary case for an env variable, and the
    /// case a trash addressed by name could not answer at all.
    /// </summary>
    [Fact]
    public void TwoEntriesSharingATitle_GoBackToTheirOwnGroups()
    {
        using var vault = Seeded(out _);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "dev-value", GroupPath = "env/dev" });

        var dev = new EntryName("env/dev", "TOKEN");

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(dev));

        foreach (var row in vault.ReadRecycled())
        {
            Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(row.Id));
        }

        Assert.Equal("v3", vault.Find(_token)?.Password, StringComparer.Ordinal);
        Assert.Equal("dev-value", vault.Find(dev)?.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// Restoring onto a name something else now answers to would make both entries unusable:
    /// <see cref="Vault.Find(EntryName)"/> refuses, a release is denied as ambiguous, and
    /// <see cref="EnvStore.Read(string)"/> throws for the whole project. A recovery must not do that.
    /// </summary>
    [Fact]
    public void RestoreRecycled_IsRefusedAndWritesNothing_WhenTheNameIsTakenAgain()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(out _, path))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "new-value", GroupPath = "env/billing" });
            vault.Save();
        }

        AssertWritesNothing(path, vault =>
        {
            var id = Assert.Single(vault.ReadRecycled()).Id;
            Assert.Equal(RestoreOutcome.DestinationOccupied, vault.RestoreRecycled(id));
        });
    }

    /// <summary>
    /// <see cref="RestoreOutcome.RestoredToRoot"/> — the group an entry came from no longer exists
    /// — is proved in <c>scripts/verify-keepassxc-recyclebin.sh</c> rather than here, because
    /// arranging it honestly means another program removing the group. keypaste has no
    /// delete-group operation (V.5a owns group work), and a test-only one would be a vault
    /// mutation this step does not own. The gate uses <c>keepassxc-cli rmdir</c>, which is the
    /// real way a vault arrives in that state.
    /// </summary>
    [Fact]
    public void ARestoreToTheRoot_IsProvedByTheCompatibilityGate()
    {
        var gate = RepoFile("scripts/verify-keepassxc-recyclebin.sh");

        Assert.Contains("rmdir", gate, StringComparison.Ordinal);
        Assert.Contains("restored root", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreRecycled_OnAnIdNothingAnswersTo_IsRefusedAndWritesNothing()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(out _, path))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            vault.Save();
        }

        AssertWritesNothing(path, vault =>
        {
            Assert.True(RecycledEntryId.TryParse("0123456789ABCDEF0123456789ABCDEF", out var absent));
            Assert.Equal(RestoreOutcome.NothingMatched, vault.RestoreRecycled(absent));
            Assert.False(vault.PurgeRecycled(absent));
        });
    }

    // ---------------------------------------------------------------- purging

    [Fact]
    public void PurgeRecycled_RemovesTheEntryAndItsHistoryForGood()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(out _, path))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            Assert.True(vault.PurgeRecycled(Assert.Single(vault.ReadRecycled()).Id));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Empty(reopened.ReadRecycled());
        Assert.Null(reopened.Find(_token));
        Assert.Null(reopened.ReadHistory(_token));
        Assert.Equal(1, reopened.TombstoneCount);
    }

    [Fact]
    public void EmptyRecycleBin_TakesEverythingAndKeepsTheTombstonesAlreadyThere()
    {
        using var vault = Seeded(out _);
        vault.AddEntry(new VaultEntry { Title = "OTHER", Password = "other", GroupPath = "env/billing" });

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        Assert.True(vault.PurgeRecycled(Assert.Single(vault.ReadRecycled()).Id));
        Assert.Equal(1, vault.TombstoneCount);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("env/billing", "OTHER")));

        Assert.Equal(1, vault.EmptyRecycleBin());
        Assert.Empty(vault.ReadRecycled());
        Assert.Equal(2, vault.TombstoneCount);
    }

    [Fact]
    public void EmptyRecycleBin_OnAnEmptyBin_RemovesNothing()
    {
        using var vault = Seeded(out _);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(Assert.Single(vault.ReadRecycled()).Id));

        Assert.Equal(0, vault.EmptyRecycleBin());
        Assert.Equal(0, vault.TombstoneCount);
    }

    // ---------------------------------------------------------------- a vault with no bin

    /// <summary>
    /// KeePassXC writes the setting and a person can turn it off there. keypaste honours it rather
    /// than overriding it, and says so instead of pretending the delete was recoverable.
    /// </summary>
    [Fact]
    public void WhenTheVaultsBinIsOff_DeletingRemovesPermanently()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(out _, path))
        {
            vault.SetRecyclesDeletedEntries(false);
            Assert.False(vault.RecyclesDeletedEntries);

            Assert.Equal(DeletionOutcome.DeletedPermanently, vault.RemoveEntry(_token));
            Assert.Empty(vault.ReadRecycled());
            Assert.Equal(1, vault.TombstoneCount);
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.False(reopened.RecyclesDeletedEntries);
        Assert.Null(reopened.Find(_token));
        Assert.Empty(reopened.ReadRecycled());
    }

    /// <summary>
    /// Turning the bin off later does not make what is already in it live again. The flag decides
    /// what a delete does; it is not a filter on the vault's contents.
    /// </summary>
    [Fact]
    public void TurningTheBinOff_DoesNotReviveWhatIsAlreadyInIt()
    {
        using var vault = Seeded(out _);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
        vault.SetRecyclesDeletedEntries(false);

        Assert.Empty(vault.ReadEntries());
        Assert.Null(vault.Find(_token));
        Assert.Single(vault.ReadRecycled());
    }

    [Fact]
    public void AFreshVault_Recycles()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);

        Assert.True(vault.RecyclesDeletedEntries);
    }

    // ---------------------------------------------------------------- the save boundary

    /// <summary>
    /// A recycle is an ordinary edit to the vault, so it goes through the same refusal as any
    /// other: a file changed underneath is not overwritten, and the deletion is not lost quietly
    /// either — the open vault still holds it, unsaved.
    /// </summary>
    [Fact]
    public void ASaveRefusedAsChangedOnDisk_WritesNeitherTheRecycleNorAnythingElse()
    {
        var path = NewVaultPath();
        using (var vault = Seeded(out _, path))
        {
            vault.Save();
        }

        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            using (var other = Vault.Open(path, MasterPassword))
            {
                other.AddEntry(new VaultEntry { Title = "ELSEWHERE", Password = "x", GroupPath = "env/dev" });
                other.Save();
            }

            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            Assert.Throws<VaultChangedOnDiskException>(vault.Save);
        }

        Assert.NotEqual(bytes, File.ReadAllBytes(path));

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Equal("v3", reopened.Find(_token)?.Password, StringComparer.Ordinal);
        Assert.Empty(reopened.ReadRecycled());
        Assert.NotNull(reopened.Find(new EntryName("env/dev", "ELSEWHERE")));
    }

    [Fact]
    public void NothingIsOnDisk_UntilTheSave()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(out _, path))
        {
            vault.Save();
        }

        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.NotNull(reopened.Find(_token));
        Assert.Empty(reopened.ReadRecycled());
    }

    [Fact]
    public void ADisposedVault_AnswersNothing()
    {
        var vault = Seeded(out _);
        vault.Dispose();

        Assert.Throws<ObjectDisposedException>(() => vault.ReadRecycled());
        Assert.Throws<ObjectDisposedException>(() => vault.EmptyRecycleBin());
        Assert.Throws<ObjectDisposedException>(() => _ = vault.RecyclesDeletedEntries);
        Assert.Throws<ObjectDisposedException>(() => vault.RestoreRecycled(default));
        Assert.Throws<ObjectDisposedException>(() => vault.PurgeRecycled(default));
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>One variable with three earlier values behind it, which is what a recovery has to
    /// bring back whole.</summary>
    private Vault Seeded(out string path, string? at = null)
    {
        path = at ?? NewVaultPath();

        var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "v0", GroupPath = "env/billing" });

        for (var revision = 1; revision <= 3; revision++)
        {
            vault.UpdateEntry(new VaultEntry
            {
                Title = "TOKEN",
                Password = $"v{revision}",
                GroupPath = "env/billing",
            });
        }

        return vault;
    }

    /// <summary>Reads a file from the repository, walking up to the solution that names it.</summary>
    private static string RepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "keypaste.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(System.IO.Path.Combine(directory!.FullName, relativePath));
    }

    /// <summary>
    /// Runs a refused operation and checks it left nothing behind: not the file's bytes, not the
    /// open vault's own answers, and not what a save of that vault would then put on disk.
    /// </summary>
    private static void AssertWritesNothing(string path, Action<Vault> refused)
    {
        var bytes = File.ReadAllBytes(path);
        var before = Recycled(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            refused(vault);

            Assert.Equal(bytes, File.ReadAllBytes(path));
            vault.Save();
        }

        Assert.Equal(before, Recycled(path));
    }

    private static List<string> Recycled(string path)
    {
        using var vault = Vault.Open(path, MasterPassword);

        return [.. vault.ReadRecycled().Select(row => $"{row.OriginalGroupPath}/{row.Title}").Order(StringComparer.Ordinal)];
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }
}
