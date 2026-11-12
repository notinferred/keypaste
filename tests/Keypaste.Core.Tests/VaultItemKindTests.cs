using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An item's kind is kept as the standard KeePass icon it sets, so KeePassXC shows the same icon: set at creation,
/// changed as one edit with one history revision, its fields untouched, and an entry with the default or a custom
/// icon read as keeping no kind.
/// </summary>
public sealed class VaultItemKindTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _github = new("Work", "GitHub");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-item-kind-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_entry_with_the_default_icon_keeps_no_kind()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Equal(ItemKind.None, Read(vault).Kind);
    }

    [Theory]
    [InlineData(ItemKind.Login)]
    [InlineData(ItemKind.ApiKey)]
    [InlineData(ItemKind.Database)]
    [InlineData(ItemKind.Server)]
    [InlineData(ItemKind.SecureNote)]
    public void A_kind_given_at_creation_is_read_back_after_a_save(ItemKind kind)
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, _master))
        {
            vault.CreateEntry(new VaultEntry { Title = "Thing", Password = "pw", Kind = kind }, [], []);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal(kind, reopened.ReadEntries().Single(entry => entry.Title == "Thing").Kind);
    }

    [Fact]
    public void Changing_the_kind_is_one_edit_that_keeps_every_field_and_the_earlier_kind_in_history()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            var before = Read(vault);
            var history = vault.ReadHistory(_github)!.Count;

            Assert.True(vault.SetKind(_github, ItemKind.ApiKey));
            Assert.False(vault.SetKind(_github, ItemKind.ApiKey));

            var after = Read(vault);
            Assert.Equal(ItemKind.ApiKey, after.Kind);
            Assert.Equal(before with { Kind = ItemKind.ApiKey }, after);
            Assert.Equal(history + 1, vault.ReadHistory(_github)!.Count);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal(ItemKind.ApiKey, Read(reopened).Kind);
    }

    [Fact]
    public void No_kind_cannot_be_set_and_a_missing_entry_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Throws<ArgumentOutOfRangeException>(() => vault.SetKind(_github, ItemKind.None));
        Assert.False(vault.SetKind(new EntryName("Work", "Nobody"), ItemKind.Login));
    }

    private static VaultEntry Read(Vault vault) => vault.ReadEntries().Single(entry => entry.Title == _github.Title);

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = _github.Title, GroupPath = _github.GroupPath, Username = "alice", Password = "login-password" });
        vault.Save();
        return vault;
    }

    private string NewVaultPath() => Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
}
