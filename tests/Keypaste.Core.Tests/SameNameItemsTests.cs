using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A name two items answer to stays refused to whoever names it, and a UUID reaches one item whatever shares its name.</summary>
public sealed class SameNameItemsTests : IDisposable
{
    private static readonly EntryName _mail = new("services", "Mail");

    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-same-name-core-").FullName;

    public SameNameItemsTests()
    {
        using var vault = Vault.Create(VaultPath, _master);
        vault.AddEntry(new VaultEntry { Title = _mail.Title, Username = "alice", Password = "alice-password", GroupPath = _mail.GroupPath });
        vault.AddEntry(new VaultEntry { Title = _mail.Title, Username = "bob", Password = "bob-password", GroupPath = _mail.GroupPath });
        vault.Save();
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_bare_name_is_refused_for_every_reader_and_writer()
    {
        using var vault = Open();

        Assert.Throws<VaultException>(() => vault.Find(_mail));
        Assert.Throws<VaultException>(() => vault.Find("services/Mail"));
        Assert.Throws<VaultException>(() => vault.ReadHistory(_mail));
        Assert.Throws<VaultException>(() => vault.RemoveEntry(_mail));
        Assert.Throws<VaultException>(() => vault.UpdateEntry(new VaultEntry { Title = _mail.Title, GroupPath = _mail.GroupPath, Password = "guess" }));
        Assert.Equal(OrganizeOutcome.DestinationAmbiguous, vault.RelocationRefusal(_mail, new EntryName("services", "Other")));
    }

    [Fact]
    public void What_an_agent_or_a_terminal_reads_a_name_from_carries_no_uuid()
    {
        using var vault = Open();
        var entry = vault.ReadEntries()[0];

        Assert.Null(EntryName.Of(entry).Uuid);
        Assert.Equal(SavedRead.Current, vault.ReadSavedListing(out var listing));
        Assert.All(listing!, listed => Assert.Null(listed.Name.Uuid));
        Assert.All(vault.Search("mail"), match => Assert.Null(match.Name.Uuid));
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public void A_uuid_reaches_its_item_alone(string username)
    {
        using var vault = Open();
        var item = Item(vault, username);

        Assert.Equal(username + "-password", vault.Find(item)!.Password);
        Assert.True(vault.UpdateEntry(item, vault.Find(item)! with { Notes = "edited" }));
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(item, out _));

        Assert.Null(vault.Find(item));
        Assert.DoesNotContain(vault.ReadEntries(), entry => entry.Username == username);
        Assert.Empty(vault.Find(_mail)!.Notes);
    }

    [Fact]
    public void A_uuid_finds_its_item_after_a_rename_and_a_move_and_says_where_it_is()
    {
        using var vault = Open();
        vault.AddEntry(new VaultEntry { Title = "seed", GroupPath = "other" });
        var item = Item(vault, "alice");

        Assert.Equal(OrganizeOutcome.RenamedAndMoved, vault.Relocate(item, new EntryName("other", "Webmail"), out var moved));
        Assert.Equal(item.Uuid, moved!.Uuid);

        var found = vault.Find(item)!;

        Assert.Equal("Webmail", found.Title);
        Assert.Equal("other", found.GroupPath);
        Assert.Equal(new EntryName("other", "Webmail"), vault.EntryNameOf(item.Uuid!));
    }

    [Fact]
    public void A_uuid_does_not_reach_an_item_in_the_bin_or_one_that_is_not_there()
    {
        using var vault = Open();
        var item = Item(vault, "alice");

        vault.RemoveEntry(item, out _);

        Assert.Null(vault.Find(item));
        Assert.Null(vault.Find(_mail with { Uuid = "00000000000000000000000000000000" }));
        Assert.Equal(OrganizeOutcome.NothingMatched, vault.RelocationRefusal(item, new EntryName("services", "Other")));
    }

    [Fact]
    public void A_uuid_two_items_hold_reaches_only_the_one_its_name_says()
    {
        using (var seed = Open())
        {
            seed.AddEntry(new VaultEntry { Title = "Forum", Username = "carol", Password = "forum-password", GroupPath = "services" });
            seed.AddEntry(new VaultEntry { Title = "seed", GroupPath = "other" });
            seed.Save();
        }

        ForeignEdit.CloneKeepingItsUuid(VaultPath, _master, "Forum", "Twin", "twin-password");

        using var vault = Open();
        var uuid = vault.ReadEntries().First(entry => entry.Title == "Forum").Uuid!;
        var forum = new EntryName("services", "Forum") { Uuid = uuid };
        var twin = new EntryName("services", "Twin") { Uuid = uuid };

        Assert.Equal("forum-password", vault.Find(forum)!.Password);
        Assert.Equal("twin-password", vault.Find(twin)!.Password);
        Assert.Null(vault.Find(new EntryName("services", "Gone") { Uuid = uuid }));
        Assert.Null(vault.EntryNameOf(uuid));

        Assert.Equal(OrganizeOutcome.Moved, vault.Relocate(twin, new EntryName("other", "Twin"), out _));

        Assert.Equal("services", vault.Find(forum)!.GroupPath);
        Assert.Equal("other", vault.ReadEntries().Single(entry => entry.Title == "Twin").GroupPath);
    }

    [Fact]
    public void A_uuid_two_items_of_one_name_hold_is_refused()
    {
        using (var seed = Open())
        {
            seed.AddEntry(new VaultEntry { Title = "Forum", Username = "carol", Password = "forum-password", GroupPath = "services" });
            seed.Save();
        }

        ForeignEdit.CloneKeepingItsUuid(VaultPath, _master, "Forum", "Forum", "twin-password");

        using var vault = Open();
        var item = new EntryName("services", "Forum") { Uuid = vault.ReadEntries().First(entry => entry.Title == "Forum").Uuid };

        Assert.Throws<VaultException>(() => vault.Find(item));
        Assert.Throws<VaultException>(() => vault.RemoveEntry(item));
    }

    [Fact]
    public void Two_names_read_alike_whatever_their_uuids()
    {
        Assert.Equal(_mail with { Uuid = "A" }, _mail with { Uuid = "B" });
        Assert.Equal((_mail with { Uuid = "A" }).GetHashCode(), _mail.GetHashCode());
    }

    [Fact]
    public void A_rename_onto_a_shared_name_stays_refused()
    {
        using var vault = Open();
        vault.AddEntry(new VaultEntry { Title = "Forum", GroupPath = "services" });

        Assert.Equal(OrganizeOutcome.DestinationOccupied, vault.Relocate(new EntryName("services", "Forum"), _mail, out _));
    }

    [Fact]
    public void A_clone_by_uuid_copies_that_item_and_addresses_the_copy()
    {
        using var vault = Open();

        Assert.Equal(OrganizeOutcome.Copied, vault.CloneEntry(Item(vault, "bob"), out var copy, out _));
        Assert.Equal("bob-password", vault.Find(copy!)!.Password);
        Assert.NotNull(copy!.Uuid);
    }

    [Fact]
    public void A_shared_name_says_so()
    {
        using var vault = Open();

        Assert.True(vault.NameIsShared(_mail));
        Assert.False(vault.NameIsShared(new EntryName("services", "Forum")));
    }

    private Vault Open() => Vault.Open(VaultPath, _master);

    private static EntryName Item(Vault vault, string username)
    {
        var entry = vault.ReadEntries().Single(candidate => candidate.Username == username);
        return new EntryName(entry.GroupPath, entry.Title) { Uuid = entry.Uuid };
    }
}
