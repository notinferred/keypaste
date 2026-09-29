using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>An entry's own tags: read, added and removed as edits, and read into projects.</summary>
public sealed class VaultTagTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _stripe = new("services", "Stripe");
    private static readonly EntryName _database = new("services", "Database");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-tag-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Adding_a_tag_is_one_revision_and_one_edit_naming_the_entry_and_survives_a_save()
    {
        var path = NewVaultPath();
        var edits = new List<VaultEdit>();

        using (var vault = Seeded(path))
        {
            vault.Edited += (_, edit) => edits.Add(edit);

            Assert.True(vault.AddTag(_stripe, "env:billing:prod"));
            Assert.Single(vault.ReadHistory(_stripe)!);
            Assert.Equal([_stripe], Assert.Single(edits).Entries);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        Assert.Equal(["env:billing:prod"], reopened.Tags(_stripe));
    }

    [Fact]
    public void Adding_a_tag_the_entry_has_or_to_an_entry_that_is_not_there_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        vault.AddTag(_stripe, "finance");
        vault.Save();

        Assert.False(vault.AddTag(_stripe, "finance"));
        Assert.False(vault.AddTag(new EntryName("services", "Nobody"), "finance"));

        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Single(vault.ReadHistory(_stripe)!);
    }

    [Fact]
    public void Tags_differing_only_in_case_are_two_tags()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.True(vault.AddTag(_stripe, "env:billing:prod"));
        Assert.True(vault.AddTag(_stripe, "env:billing:Prod"));

        Assert.Equal(2, vault.Tags(_stripe)!.Count);
    }

    [Fact]
    public void Removing_tags_is_one_revision_and_removing_absent_ones_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        vault.AddTag(_stripe, "env:billing");
        vault.AddTag(_stripe, "env:billing:dev");
        vault.AddTag(_stripe, "finance");
        vault.Save();
        var revisions = vault.ReadHistory(_stripe)!.Count;

        Assert.True(vault.RemoveTags(_stripe, ["env:billing", "env:billing:dev", "absent"]));
        Assert.Equal(["finance"], vault.Tags(_stripe));
        Assert.Equal(revisions + 1, vault.ReadHistory(_stripe)!.Count);
        vault.Save();

        Assert.False(vault.RemoveTags(_stripe, ["absent", "env:billing"]));
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a,b")]
    [InlineData("a;b")]
    [InlineData("a\tb")]
    [InlineData(" env:billing")]
    public void A_refused_tag_changes_nothing(string tag)
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.Throws<VaultException>(() => vault.AddTag(_stripe, tag));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.Tags(_stripe)!);
    }

    [Fact]
    public void An_entry_tag_leaves_the_file_KDBX_4_0_and_group_tags_are_never_read()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            vault.AddTag(_stripe, "env:billing:prod");
            vault.Save();
        }

        Assert.Equal(KdbxVersion(path), (4, 0));

        using var reopened = Vault.Open(path, _master);
        Assert.Empty(reopened.Tags(_database)!);
    }

    [Fact]
    public void ReadTags_lists_live_tagged_entries_and_not_the_recycle_bin()
    {
        using var vault = Seeded(NewVaultPath());
        vault.AddTag(_stripe, "env:billing");
        vault.AddTag(_database, "env:billing:prod");
        vault.RemoveEntry(_database, out _);

        Assert.Equal([new EntryTags(_stripe, ["env:billing"])], vault.ReadTags(), new EntryTagsComparer());
    }

    [Fact]
    public void A_tag_edit_leaves_fields_attachments_and_other_tags_as_they_were()
    {
        var path = NewVaultPath();
        KdbxImportTests.WriteForeign(path, _master);
        var checking = new EntryName("Banking", "Checking");

        using (var vault = Vault.Open(path, _master))
        {
            Assert.True(vault.AddTag(checking, "env:billing"));
            vault.Save();
        }

        using var reopened = KeePassInterop.Open(path, System.Text.Encoding.UTF8.GetBytes(_master));
        var facts = reopened.FactsUnchecked(checking)!;
        Assert.Equal(["env:billing", "finance"], facts.Tags.Order(StringComparer.Ordinal));
        Assert.Equal("4321", facts.Strings["PIN"]);
        Assert.Contains("statement.txt", facts.Attachments.Keys);
    }

    private static (int Major, int Minor) KdbxVersion(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        file.ReadExactly(header);
        return (header[10] | (header[11] << 8), header[8] | (header[9] << 8));
    }

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = _stripe.Title, GroupPath = _stripe.GroupPath, Password = "s" });
        vault.AddEntry(new VaultEntry { Title = _database.Title, GroupPath = _database.GroupPath, Password = "d" });
        vault.Save();
        return vault;
    }

    private string NewVaultPath() => System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

    private sealed class EntryTagsComparer : IEqualityComparer<EntryTags>
    {
        public bool Equals(EntryTags? x, EntryTags? y) =>
            x is not null && y is not null && x.Name == y.Name && x.Tags.SequenceEqual(y.Tags, StringComparer.Ordinal);

        public int GetHashCode(EntryTags obj) => obj.Name.GetHashCode();
    }
}
