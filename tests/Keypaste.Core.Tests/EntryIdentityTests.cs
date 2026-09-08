using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An entry is where it lives and what it is called. Nothing addresses one by the two joined.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VaultEntry.Path"/> is <c>GroupPath + "/" + Title</c> with no escaping, so an entry
/// titled <c>nested/TOKEN</c> in <c>env/dev</c> and an entry titled <c>TOKEN</c> in
/// <c>env/dev/nested</c> produce the same string and are different entries. Every resolver that
/// takes that string apart again invents its own rule for where the boundary was, and the rules
/// disagreed: one compared the joined path and took the first match, another split on the last
/// slash. A listing found one entry and a removal deleted the other one.
/// </para>
/// <para>
/// This file holds every resolver to one answer. The fixtures are what KeePassXC puts in a file
/// and keypaste's own writers refuse to create, which is why nothing here goes through
/// <see cref="EnvStore.TrySet"/>.
/// </para>
/// </remarks>
public sealed class EntryIdentityTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private readonly string _directory;

    public EntryIdentityTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-identity-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Removing_TheSlashedTitle_LeavesTheNestedEntry()
    {
        var path = NewVaultPath();

        using (var vault = Collision(path))
        {
            Assert.True(vault.RemoveEntry(new EntryName("env/dev", "nested/TOKEN")));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal(
            new[] { ("env/dev", "KEEP", "keep"), ("env/dev/nested", "TOKEN", "nested") },
            Shape(reopened));
    }

    [Fact]
    public void Removing_TheNestedEntry_LeavesTheSlashedTitle()
    {
        var path = NewVaultPath();

        using (var vault = Collision(path))
        {
            Assert.True(vault.RemoveEntry(new EntryName("env/dev/nested", "TOKEN")));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal(
            new[] { ("env/dev", "KEEP", "keep"), ("env/dev", "nested/TOKEN", "slashed") },
            Shape(reopened));
    }

    [Fact]
    public void Finding_ByName_TellsASlashedTitleApartFromANestedGroup()
    {
        using var vault = Collision(NewVaultPath());

        Assert.Equal("slashed", vault.Find(new EntryName("env/dev", "nested/TOKEN"))?.Password);
        Assert.Equal("nested", vault.Find(new EntryName("env/dev/nested", "TOKEN"))?.Password);
        Assert.Null(vault.Find(new EntryName("env/dev", "TOKEN")));
    }

    /// <summary>
    /// The joined form stays addressable — a person types it and a policy file holds it — so it
    /// refuses a collision rather than resolving to whichever entry the file happens to list
    /// first. F.1a left this one: it repaired removal, and until F.1e this returned
    /// <c>slashed</c> and <c>keypaste get</c> handed that secret out (docs/PRODUCT.md law 3.7).
    /// </summary>
    [Fact]
    public void Finding_ByPath_WhenTwoEntriesAnswerToIt_IsRefused()
    {
        using var vault = Collision(NewVaultPath());

        Assert.Throws<VaultException>(() => vault.Find("env/dev/nested/TOKEN"));
    }

    /// <summary>
    /// The other half, without which refusing everything would pass: a path only one entry
    /// answers to still resolves, and one no entry answers to is still absence rather than error.
    /// </summary>
    [Fact]
    public void Finding_ByPath_WhenOneEntryAnswersToIt_StillResolves()
    {
        using var vault = Collision(NewVaultPath());

        Assert.Equal("keep", vault.Find("env/dev/KEEP")?.Password);
        Assert.Null(vault.Find("env/dev/NOTHING"));
    }

    [Fact]
    public void Removing_AName_ThatIsNotThere_ChangesNothing()
    {
        using var vault = Collision(NewVaultPath());

        Assert.False(vault.RemoveEntry(new EntryName("env/dev", "TOKEN")));
        Assert.Equal(3, vault.ReadEntries().Count);
    }

    /// <summary>
    /// KDBX permits two entries with one title in one group, and KeePassXC will make them. There
    /// is no correct answer to which of them was meant, so both survive (docs/PRODUCT.md law 3.7).
    /// </summary>
    [Fact]
    public void Removing_ATitleTwoEntriesInOneGroupShare_IsRefused()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "first", GroupPath = "env/dev" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "second", GroupPath = "env/dev" });

        Assert.Throws<VaultException>(() => vault.RemoveEntry(new EntryName("env/dev", "TOKEN")));
        Assert.Equal(2, vault.ReadEntries().Count);
    }

    [Fact]
    public void Updating_WritesToTheNamedEntry_NotItsCollidingNeighbour()
    {
        using var vault = Collision(NewVaultPath());

        Assert.True(vault.UpdateEntry(new VaultEntry
        {
            Title = "TOKEN",
            Password = "rewritten",
            GroupPath = "env/dev/nested",
        }));

        Assert.Equal(
            new[]
            {
                ("env/dev", "KEEP", "keep"),
                ("env/dev", "nested/TOKEN", "slashed"),
                ("env/dev/nested", "TOKEN", "rewritten"),
            },
            Shape(vault));
    }

    /// <summary>
    /// The property rather than one worked example of it: whatever the vault holds, locating an
    /// entry and removing it agree about which one that is. This is the assertion that would have
    /// caught the original defect without anyone having thought of <c>nested/TOKEN</c>.
    /// </summary>
    [Fact]
    public void FindingAndRemoving_AgreeAboutEveryEntryInACollidingVault()
    {
        List<EntryName> names = [];
        using (var survey = Collision(NewVaultPath()))
        {
            foreach (var entry in survey.ReadEntries())
            {
                names.Add(EntryName.Of(entry));
            }
        }

        foreach (var name in names)
        {
            using var vault = Collision(NewVaultPath());

            var found = vault.Find(name);
            var removed = vault.RemoveEntry(name);

            Assert.Equal(found is not null, removed);
            Assert.DoesNotContain(
                Shape(vault),
                row => string.Equals(row.GroupPath, name.GroupPath, StringComparison.Ordinal)
                    && string.Equals(row.Title, name.Title, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Two entries sharing the path <c>env/dev/nested/TOKEN</c>, and a bystander. The passwords
    /// differ so a test can say which one survived rather than only how many did.
    /// </summary>
    private static Vault Collision(string path)
    {
        var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "nested/TOKEN", Password = "slashed", GroupPath = "env/dev" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "nested", GroupPath = "env/dev/nested" });
        vault.AddEntry(new VaultEntry { Title = "KEEP", Password = "keep", GroupPath = "env/dev" });
        return vault;
    }

    private static List<(string GroupPath, string Title, string Password)> Shape(Vault vault)
    {
        var rows = vault.ReadEntries()
            .Select(entry => (entry.GroupPath, entry.Title, entry.Password))
            .ToList();

        rows.Sort();
        return rows;
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }
}
