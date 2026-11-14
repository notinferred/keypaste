using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What the vault's owner reads for a search: names, kind, a URL's origin and, when Settings shows it, a username, and nothing from its own groups or the bin.</summary>
public sealed class VaultIndexTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-index-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("https://gist.github.com:8443/a/b?q=secret#frag", "https://gist.github.com:8443")]
    [InlineData("github.com/login?next=/settings", "https://github.com:443")]
    [InlineData("  HTTP://Example.COM/path  ", "http://example.com:80")]
    [InlineData("http://localhost:3000/", "http://localhost:3000")]
    [InlineData("https://user:pw@github.com/", null)]
    [InlineData("mailto:dan@example.com", null)]
    [InlineData("ftp://files.example.com/", null)]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    public void An_items_origin_is_its_urls_scheme_host_and_port_alone(string url, string? origin)
    {
        using var vault = Saved(new VaultEntry { Title = "Site", GroupPath = "Web", Url = url, Password = "pw" });

        Assert.Equal(origin, Assert.Single(Index(vault, showsUsername: true)).Origin?.ToString());
    }

    [Fact]
    public void A_username_is_read_only_while_settings_shows_usernames()
    {
        using var vault = Saved(
            new VaultEntry { Title = "GitHub", GroupPath = "Web", Username = "dan", Password = "pw" },
            new VaultEntry { Title = "Blank", GroupPath = "Web", Password = "pw" });

        Assert.Equal(["dan", null], Index(vault, showsUsername: true).Select(entry => entry.Username));
        Assert.Equal([null, null], Index(vault, showsUsername: false).Select(entry => entry.Username));
    }

    [Fact]
    public void An_entry_carries_its_kind_its_uuid_its_askable_fields_and_its_project_tags()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "kinds.kdbx"), _master);
        var name = new EntryName("Ops", "Database");
        vault.AddEntry(new VaultEntry { Title = name.Title, GroupPath = name.GroupPath, Username = "admin", Password = "pw" });
        vault.SetKind(name, ItemKind.Database);
        vault.SetFields(name, [new FieldWrite("DATABASE_URL", "postgres://x"), new FieldWrite("Recovery codes", "1 2 3")]);
        vault.AddTag(name, "env:web:prod");
        vault.AddTag(name, "team");
        vault.Save();

        var entry = Assert.Single(Index(vault, showsUsername: true));

        Assert.Equal(ItemKind.Database, entry.Kind);
        Assert.Equal(vault.ReadEntries().Single().Uuid, entry.Listing.Name.Uuid);
        Assert.Equal(["password", "username", "DATABASE_URL", "Recovery codes"], entry.Listing.Fields);
        Assert.Equal(["env:web:prod"], entry.Listing.Tags);
        Assert.Equal(["web"], entry.Projects);
    }

    [Fact]
    public void Keypastes_own_groups_and_the_bin_are_left_out()
    {
        using var vault = Saved(
            new VaultEntry { Title = "GitHub", GroupPath = "Web", Password = "pw" },
            new VaultEntry { Title = "Gone", GroupPath = "Web", Password = "pw" },
            new VaultEntry { Title = "token record", GroupPath = ReservedGroups.Tokens, Password = "verifier" },
            new VaultEntry { Title = "share record", GroupPath = ".Keypaste/shares", Password = "revoke" });
        vault.RemoveEntry(new EntryName("Web", "Gone"));
        vault.Save();

        Assert.Equal([new EntryName("Web", "GitHub")], Index(vault, showsUsername: true).Select(entry => entry.Listing.Name));
    }

    [Fact]
    public void The_index_is_read_only_while_the_vault_matches_its_file()
    {
        using var vault = Saved(new VaultEntry { Title = "GitHub", GroupPath = "Web", Password = "pw" });
        vault.AddEntry(new VaultEntry { Title = "Unsaved", GroupPath = "Web", Password = "pw" });

        Assert.NotEqual(SavedRead.Current, vault.ReadSavedIndex(showsUsername: true, out var index));
        Assert.Null(index);
    }

    private static IReadOnlyList<IndexedEntry> Index(Vault vault, bool showsUsername)
    {
        Assert.Equal(SavedRead.Current, vault.ReadSavedIndex(showsUsername, out var index));
        return index!;
    }

    private Vault Saved(params VaultEntry[] entries)
    {
        var vault = Vault.Create(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx"), _master);

        foreach (var entry in entries)
        {
            vault.AddEntry(entry);
        }

        vault.Save();
        return vault;
    }
}
