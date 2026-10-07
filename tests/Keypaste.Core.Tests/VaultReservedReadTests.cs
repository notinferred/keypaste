using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// keypaste's own groups are left out of every whole-vault read unless a caller asks for them, so no
/// front end has to filter them (F.55).
/// </summary>
public sealed class VaultReservedReadTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-reserved-read-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Reads_leave_out_keypastes_own_groups_by_default()
    {
        using var vault = Seeded();

        Assert.Equal(["Work/github"], vault.ReadEntries().Select(entry => entry.Path));
        Assert.Equal(["Work"], vault.ReadGroupPaths());
        Assert.Equal([new EntryName("Work", "github")], vault.Search(string.Empty).Select(match => match.Name));
        Assert.Empty(vault.Search("record"));
    }

    [Fact]
    public void Reads_include_keypastes_own_groups_when_asked()
    {
        using var vault = Seeded();

        Assert.Equal(3, vault.ReadEntries(includeReserved: true).Count);
        Assert.Contains(ReservedGroups.Tokens, vault.ReadGroupPaths(includeReserved: true));
        Assert.Contains(".Keypaste/shares", vault.ReadGroupPaths(includeReserved: true));
        Assert.Equal(2, vault.Search("record", includeReserved: true).Count);
    }

    private Vault Seeded()
    {
        var vault = Vault.Create(Path.Combine(_directory, "reserved.kdbx"), _master);
        vault.AddEntry(new VaultEntry { Title = "github", GroupPath = "Work", Password = "pw" });
        vault.AddEntry(new VaultEntry { Title = "token record", GroupPath = ReservedGroups.Tokens, Password = "verifier" });
        vault.AddEntry(new VaultEntry { Title = "share record", GroupPath = ".Keypaste/shares", Password = "revoke" });
        return vault;
    }
}
