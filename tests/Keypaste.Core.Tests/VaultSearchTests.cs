using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Finding an entry by something about it, and the fields that are not something about it.
/// </summary>
/// <remarks>
/// <para>
/// Half of this file is negative. <see cref="Vault.Search"/> exists so that a front end never has
/// to hold a username in order to match one, and the value of that is entirely in what it will not
/// compare: a password, a note, and any protected field somebody else's client wrote. A search that
/// matched a note would let anyone at an unlocked vault confirm a recovery code by typing it,
/// without ever selecting the entry that holds it — and notes are where recovery codes go.
/// </para>
/// <para>
/// The positive assertions come first in each test so a negative sweep can never pass because the
/// query found nothing at all.
/// </para>
/// </remarks>
public sealed class VaultSearchTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private readonly string _directory;

    public VaultSearchTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-search-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ATitleIsMatched()
    {
        using var vault = Seeded();

        var match = Assert.Single(vault.Search("PROD_DB"));

        Assert.Equal(new EntryName("servers", "PROD_DB"), match.Name);
        Assert.Equal(MatchedFields.Title, match.Fields);
    }

    [Fact]
    public void AGroupPathIsMatched()
    {
        using var vault = Seeded();

        var names = vault.Search("servers").Select(match => match.Name.Title).ToList();

        Assert.Contains("PROD_DB", names);
        Assert.All(vault.Search("servers"), match => Assert.True(match.Fields.HasFlag(MatchedFields.GroupPath)));
    }

    [Fact]
    public void AUsernameIsMatched()
    {
        using var vault = Seeded();

        var match = Assert.Single(vault.Search("dba@example.test"));

        Assert.Equal(new EntryName("servers", "PROD_DB"), match.Name);
        Assert.Equal(MatchedFields.Username, match.Fields);
    }

    [Fact]
    public void AUrlIsMatched()
    {
        using var vault = Seeded();

        var match = Assert.Single(vault.Search("db.example.test"));

        Assert.Equal(new EntryName("servers", "PROD_DB"), match.Name);
        Assert.Equal(MatchedFields.Url, match.Fields);
    }

    /// <summary>
    /// The load-bearing negative. The value is in the vault, and nothing finds it.
    /// </summary>
    [Fact]
    public void APasswordIsNotMatched()
    {
        using var vault = Seeded();

        // The positive control: the entry holding it is findable by something else.
        Assert.NotEmpty(vault.Search("PROD_DB"));

        Assert.Empty(vault.Search("hunter2-secret-value"));
    }

    /// <summary>
    /// Notes are where a recovery code goes, so they are not a field a query may confirm.
    /// </summary>
    [Fact]
    public void ANoteIsNotMatched()
    {
        using var vault = Seeded();

        Assert.NotEmpty(vault.Search("PROD_DB"));

        Assert.Empty(vault.Search("recovery-code-in-the-notes"));
    }

    /// <summary>
    /// A field keypaste does not model, written by another client, is preserved and not searched.
    /// </summary>
    [Fact]
    public void AProtectedCustomFieldIsNotMatched()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            vault.AddProtectedFieldUnchecked(new EntryName("servers", "PROD_DB"), "TOTP Seed", "custom-field-secret");
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.NotEmpty(reopened.Search("PROD_DB"));
        Assert.Empty(reopened.Search("custom-field-secret"));
    }

    [Fact]
    public void AnEntryMatchingSeveralFieldsReportsAllOfThem()
    {
        using var vault = Seeded();

        var match = Assert.Single(vault.Search("example"), m => m.Name.Title == "PROD_DB");

        Assert.Equal(MatchedFields.Username | MatchedFields.Url, match.Fields);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        using var vault = Seeded();

        Assert.NotEmpty(vault.Search("prod_db"));
        Assert.NotEmpty(vault.Search("PROD_db"));
    }

    /// <summary>
    /// A recycled entry is not in the vault as far as every other reader is concerned, and a search
    /// that found one would put a deleted name back on screen (D-0248).
    /// </summary>
    [Fact]
    public void ARecycledEntryIsNotFound()
    {
        using var vault = Seeded();

        Assert.NotEmpty(vault.Search("PROD_DB"));
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("servers", "PROD_DB")));

        Assert.Empty(vault.Search("PROD_DB"));
        Assert.Empty(vault.Search("dba@example.test"));
    }

    /// <summary>
    /// An empty query is every entry and no claim about any field, so a caller can use one method
    /// for a filtered list and an unfiltered one.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyQueryIsEveryEntryAndNoMatchedField(string query)
    {
        using var vault = Seeded();

        var matches = vault.Search(query);

        Assert.Equal(vault.ReadEntries().Count, matches.Count);
        Assert.All(matches, match => Assert.Equal(MatchedFields.None, match.Fields));
    }

    [Fact]
    public void AQueryNothingHoldsFindsNothing()
    {
        using var vault = Seeded();

        Assert.Empty(vault.Search("no-entry-has-this"));
    }

    private Vault Seeded() => Seeded(NewVaultPath());

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, MasterPassword);

        vault.AddEntry(new VaultEntry
        {
            Title = "PROD_DB",
            Username = "dba@example.test",
            Password = "hunter2-secret-value",
            Url = "https://db.example.test/admin",
            Notes = "recovery-code-in-the-notes",
            GroupPath = "servers",
        });

        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "p", GroupPath = "env/billing" });

        vault.Save();
        return vault;
    }

    private string NewVaultPath() =>
        System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
}
