using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Today's Core opens, reads and saves the vaults each published release wrote (law 14, Y.2); ReleaseVaults/README.md says how each was made.</summary>
public sealed class ReleaseVaultFixtureTests : IDisposable
{
    private const string _fixturePassword = "keypaste-fixture-password";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-release-vaults-").FullName;

    public static TheoryData<string> Releases => ["0.1.0", "0.2.0", "0.3.0"];

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void EveryCommittedReleaseVault_IsOpenedHere()
    {
        var committed = Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "ReleaseVaults"))
            .Select(directory => Path.GetFileName(directory)!.TrimStart('v'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(Releases.Select(row => row.Data).Order(StringComparer.Ordinal), committed);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void AReleaseVault_OpensWithEveryItemItWrote(string release)
    {
        using var vault = ClaimedVault.Open(FixturePath(release), _fixturePassword);

        AssertWrittenBy(release, vault);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void AReleaseVault_SavedByTodaysCore_Reopens(string release)
    {
        var copy = Path.Combine(_directory, "vault.kdbx");
        File.Copy(FixturePath(release), copy);

        using (var vault = ClaimedVault.Open(copy, _fixturePassword))
        {
            vault.AddEntry(new VaultEntry { Title = "Saved today", Password = "saved-today" });
            vault.Save();
        }

        using var reopened = ClaimedVault.Open(copy, _fixturePassword);
        AssertWrittenBy(release, reopened, "Saved today|||saved-today||");
    }

    private static string FixturePath(string release) =>
        Path.Combine(AppContext.BaseDirectory, "ReleaseVaults", "v" + release, "vault.kdbx");

    private static void AssertWrittenBy(string release, Vault vault, params string[] addedToday)
    {
        string[] expected =
        [
            .. addedToday,
            $"Mail|||mail-password-{release}||",
            $"env/app/API_KEY|||sk-fixture-{release}||",
            "env/app/DATABASE_URL|||postgres://fixture:pw@localhost/app||",
            $"web/Forge|octo|https://forge.example/login|forge-pässword-{release}|Fixture notes, {release}\nsecond line|",
        ];

        var entries = vault.ReadEntries(includeReserved: true);
        Assert.All(entries, entry => Assert.Empty(vault.Fields(new EntryName(entry.GroupPath, entry.Title))!));

        var actual = entries
            .Select(entry => $"{entry.Path}|{entry.Username}|{entry.Url}|{entry.Password}|{entry.Notes}|{entry.Expires}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
        Assert.Empty(vault.ReadRecycled());
    }
}
