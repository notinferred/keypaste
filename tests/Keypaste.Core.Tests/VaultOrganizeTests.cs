using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Organizing a vault: a group made, a group renamed, an entry renamed, an entry moved. What the
/// four operations must preserve, and what changes because of them.
/// </summary>
/// <remarks>
/// <para>
/// One claim carries most of this file. <b>The entry is mutated, never replaced</b>: KeePass holds
/// an entry's UUID, timestamps, attachments and custom string fields on the object, and re-adding
/// one under a new name mints a fresh UUID and drops everything keypaste does not model — a
/// <see cref="VaultEntry"/> has five fields and a KDBX entry has more. A rename that quietly lost
/// an attachment would satisfy every assertion made about the five.
/// </para>
/// <para>
/// The second claim is about the file rather than the vault. Recycling raises a written file to
/// KDBX 4.1 because a recycled entry carries <c>PreviousParentGroup</c> (DECISIONS.md D-0247), and
/// tidying a folder must not cost the same reader floor — so an ordinary move records nothing about
/// where the entry came from, and the file stays 4.0 until something is actually deleted.
/// </para>
/// </remarks>
public sealed class VaultOrganizeTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private static readonly EntryName _token = new("env/billing", "TOKEN");

    private readonly string _directory;

    public VaultOrganizeTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-organize-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    // ---------------------------------------------------------------- renaming an entry

    [Fact]
    public void RenameEntry_KeepsTheEntryItRenamed_WithItsIdentityAndItsHistory()
    {
        var path = NewVaultPath();
        string before;

        using (var vault = Seeded(path))
        {
            before = vault.EntryUuid(_token)!;

            Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(_token, "API_TOKEN", out var renamed));
            Assert.Equal(new EntryName("env/billing", "API_TOKEN"), renamed);
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var renamedName = new EntryName("env/billing", "API_TOKEN");

        Assert.Equal(before, reopened.EntryUuid(renamedName));
        Assert.Null(reopened.Find(_token));

        var entry = reopened.Find(renamedName);
        Assert.NotNull(entry);
        Assert.Equal("v3", entry!.Password, StringComparer.Ordinal);
        Assert.Equal("billing@example.test", entry.Username, StringComparer.Ordinal);
        Assert.Equal("https://example.test/a?b=c#d", entry.Url, StringComparer.Ordinal);

        Assert.Equal(
            new[] { "v2", "v1", string.Empty },
            reopened.ReadHistory(renamedName)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// A rename changes identity, not a value, so it takes no history slot. KeePass evicts the
    /// oldest revision when the list fills, and V.3a exists because keypaste once lost history:
    /// two renames must not be able to push out the oldest password an entry ever held.
    /// </summary>
    [Fact]
    public void RenameEntry_TakesNoHistoryRevision_BecauseItOverwritesNoValue()
    {
        using var vault = Seeded(NewVaultPath());

        var before = vault.ReadHistory(_token)!.Count;

        Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(_token, "API_TOKEN", out _));

        Assert.Equal(before, vault.ReadHistory(new EntryName("env/billing", "API_TOKEN"))!.Count);
    }

    // ---------------------------------------------------------------- moving an entry

    [Fact]
    public void MoveEntry_ReadsAtTheNewPathAndNotTheOldOne_WithItsIdentityAndItsHistory()
    {
        var path = NewVaultPath();
        string before;

        using (var vault = Seeded(path))
        {
            before = vault.EntryUuid(_token)!;

            Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(_token, "keys", out var moved));
            Assert.Equal(new EntryName("keys", "TOKEN"), moved);
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var movedName = new EntryName("keys", "TOKEN");

        Assert.Equal(before, reopened.EntryUuid(movedName));
        Assert.Null(reopened.Find(_token));
        Assert.Null(reopened.Find("env/billing/TOKEN"));
        Assert.Equal("v3", reopened.Find("keys/TOKEN")!.Password, StringComparer.Ordinal);

        Assert.Equal(
            new[] { "v2", "v1", string.Empty },
            reopened.ReadHistory(movedName)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// A group is a thing a person made. Emptying it is not a reason to delete it, and V.5a has no
    /// group deletion to do it with.
    /// </summary>
    [Fact]
    public void MoveEntry_LeavesTheGroupItCameFrom_Empty()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(new EntryName("env/billing", "KEPT"), "keys", out _));
        Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(_token, "keys", out _));

        Assert.Contains("env/billing", vault.ReadGroupPaths());
        Assert.DoesNotContain(vault.ReadEntries(), entry => entry.GroupPath == "env/billing");
    }

    // ---------------------------------------------------------------- groups

    /// <summary>
    /// A new group holds nothing, so only <see cref="Vault.ReadGroupPaths"/> can see it. Anything
    /// built on <see cref="Vault.ReadEntries"/> alone will not show it.
    /// </summary>
    [Fact]
    public void CreateGroup_MakesAnEmptyGroup_ThatSurvivesASaveAndAReopen()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            Assert.Equal(GroupOutcome.Created, vault.CreateGroup("keys", "personal", out var created));
            Assert.Equal("keys/personal", created, StringComparer.Ordinal);
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Contains("keys/personal", reopened.ReadGroupPaths());
        Assert.DoesNotContain(reopened.ReadEntries(), entry => entry.GroupPath == "keys/personal");
    }

    [Fact]
    public void CreateGroup_MakesAGroupAtTheRoot_WhenTheParentPathIsEmpty()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Equal(GroupOutcome.Created, vault.CreateGroup(string.Empty, "archive", out var created));

        Assert.Equal("archive", created, StringComparer.Ordinal);
        Assert.Contains("archive", vault.ReadGroupPaths());
    }

    [Fact]
    public void RenameGroup_CarriesEveryEntryUnderIt_WithItsHistory()
    {
        var path = NewVaultPath();
        string before;

        using (var vault = Seeded(path))
        {
            before = vault.EntryUuid(_token)!;

            Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "invoicing", out var renamed));
            Assert.Equal("env/invoicing", renamed, StringComparer.Ordinal);
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var movedName = new EntryName("env/invoicing", "TOKEN");

        Assert.Equal(before, reopened.EntryUuid(movedName));
        Assert.DoesNotContain("env/billing", reopened.ReadGroupPaths());
        Assert.Equal(
            new[] { "v2", "v1", string.Empty },
            reopened.ReadHistory(movedName)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// Where a recycled entry came from is a UUID and not a path, so renaming its original group
    /// cannot strand it. This looks as though it ought to break, and it is the cheapest evidence
    /// that the rename really did mutate the group in place.
    /// </summary>
    [Fact]
    public void RenameGroup_LeavesARecycledEntryAbleToComeHome()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token, out var recycled));
        Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "invoicing", out _));

        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(recycled));
        Assert.NotNull(vault.Find(new EntryName("env/invoicing", "TOKEN")));
    }

    // ---------------------------------------------------------------- the file the writer leaves

    /// <summary>
    /// The load-bearing format claim. Recycling costs a reader below KeePassXC 2.7 (D-0247);
    /// tidying a folder must not, so no organize operation records where anything came from.
    /// </summary>
    [Fact]
    public void Organizing_LeavesTheFileKdbx40_WhileRecyclingRaisesTheSameFileTo41()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            Assert.Equal(GroupOutcome.Created, vault.CreateGroup(string.Empty, "archive", out _));
            Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "invoicing", out _));
            Assert.Equal(
                OrganizeOutcome.Renamed,
                vault.RenameEntry(new EntryName("env/invoicing", "TOKEN"), "API_TOKEN", out _));
            Assert.Equal(
                OrganizeOutcome.Moved,
                vault.MoveEntry(new EntryName("env/invoicing", "API_TOKEN"), "archive", out _));
            vault.Save();
        }

        Assert.Equal(0, MinorVersion(path));

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("archive", "API_TOKEN")));
            vault.Save();
        }

        Assert.Equal(1, MinorVersion(path));
    }

    // ---------------------------------------------------------------- what resolves afterwards

    [Fact]
    public void RenamingAnEnvProject_IsWhatTheEnvStoreThenReads()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "invoicing", out _));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var store = new EnvStore(reopened);

        Assert.Contains("invoicing", store.Projects());
        Assert.False(store.ProjectExists("billing"));
        Assert.Equal(
            "v3",
            store.Read("invoicing").Single(variable => variable.Key == "TOKEN").Value,
            StringComparer.Ordinal);
    }

    [Fact]
    public void MovingAnEntryIntoAnEnvProject_MakesItAVariableThatProjectServes()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(new EntryName("keys", "SPARE"), "env/billing", out _));

        var store = new EnvStore(vault);
        Assert.Equal(
            "spare",
            store.Read("billing").Single(variable => variable.Key == "SPARE").Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// docs/STEPS.md V.5a: fresh resolution uses the resulting path. The exposure matcher is the
    /// seam that actually decides what a bridge may name, so the claim is asserted there rather
    /// than inferred from the entry list.
    /// </summary>
    [Fact]
    public void AnExposure_MatchesAMovedEntryAtItsNewPathAndNotItsOld()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.True(EntryExposure.Default.Allows(_token));

        Assert.Equal(OrganizeOutcome.Moved, vault.MoveEntry(_token, "keys", out var moved));

        Assert.False(EntryExposure.Default.Allows(moved!));
        Assert.Equal(new EntryName("keys", "TOKEN"), EntryName.Of(vault.Find("keys/TOKEN")!));
    }

    /// <summary>
    /// The cost of renaming a project, asserted rather than described. A rule is a standing human
    /// authorization over a <em>path</em>, so carrying a project into a granted namespace carries
    /// its credentials under that rule with nobody prompted (THREATS.md T-13). keypaste does not
    /// prevent this; it must not be able to happen without this test going red.
    /// </summary>
    [Fact]
    public void APolicyRule_FollowsTheProjectPath_SoARenameMovesEntriesUnderTheRuleForTheNewName()
    {
        using var vault = Seeded(NewVaultPath());

        var granted = Rule("env/dev/**");
        var abandoned = Rule("env/billing/**");

        Assert.False(granted.Matches("claude-code", _token, "password"));
        Assert.True(abandoned.Matches("claude-code", _token, "password"));

        Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "dev", out _));

        var names = vault.ReadEntries()
            .Where(entry => entry.GroupPath == "env/dev")
            .Select(EntryName.Of)
            .ToList();

        Assert.Equal(2, names.Count);
        Assert.All(names, name => Assert.True(granted.Matches("claude-code", name, "password")));
        Assert.All(names, name => Assert.False(abandoned.Matches("claude-code", name, "password")));
    }

    // ---------------------------------------------------------------- helpers

    private static PolicyRule Rule(string glob)
    {
        var text = "[[allow]]\n"
            + "client          = \"claude-code\"\n"
            + "entries         = [\"" + glob + "\"]\n"
            + "fields          = [\"password\"]\n"
            + "max_ttl_seconds = 300\n";

        Assert.True(Toml.TryParse(text, out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var document, out var error), error);
        return Assert.Single(document.Rules);
    }

    /// <summary>The KDBX minor version of the saved file: byte 8, little-endian.</summary>
    private static int MinorVersion(string path)
    {
        var header = new byte[12];

        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        Assert.Equal<byte>([0x03, 0xd9, 0xa2, 0x9a, 0x67, 0xfb, 0x4b, 0xb5], header[..8]);
        Assert.Equal(4, header[10]);
        return header[8];
    }

    /// <summary>
    /// A vault with an env project, a plain group, a duplicate title in another group, and an
    /// entry whose password has been replaced three times so it has history to carry.
    /// </summary>
    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, MasterPassword);

        vault.AddEntry(new VaultEntry { Title = "TOKEN", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "KEPT", Password = "kept", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "other", GroupPath = "env/shipping" });
        vault.AddEntry(new VaultEntry { Title = "SPARE", Password = "spare", GroupPath = "keys" });

        for (var revision = 1; revision <= 3; revision++)
        {
            vault.UpdateEntry(new VaultEntry
            {
                Title = "TOKEN",
                Username = "billing@example.test",
                Password = $"v{revision}",
                Url = "https://example.test/a?b=c#d",
                GroupPath = "env/billing",
            });
        }

        vault.Save();
        return vault;
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }
}
