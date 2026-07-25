using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Covers the open-modify-save cycle, which Stage 0.2 never exercised: it only ever created a
/// vault and saved it once. Every CLI verb except <c>init</c> reopens an existing file, so the
/// durability of that path is now on the secret path (CORE.md law 4.5).
/// </summary>
public sealed class VaultRemoveTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private readonly string _directory;

    public VaultRemoveTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-remove-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void RemoveEntry_RemovesIt_AndItStaysGoneAfterReopen()
    {
        var path = Seed();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.True(vault.RemoveEntry("servers/production"));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Null(reopened.Find("servers/production"));
        Assert.NotNull(reopened.Find("solo"));
    }

    [Fact]
    public void RemoveEntry_ReturnsFalse_WhenNothingMatches()
    {
        var path = Seed();

        using var vault = Vault.Open(path, MasterPassword);

        Assert.False(vault.RemoveEntry("servers/does-not-exist"));
        Assert.False(vault.RemoveEntry("no-such-group/entry"));
    }

    /// <summary>
    /// A group that holds no entries is invisible to <see cref="Vault.ReadEntries"/> but is
    /// listed by <c>keepassxc-cli ls -R -f</c>. Listing the two separately is what lets keypaste
    /// agree with KeePassXC about the shape of the same file (CORE.md law 4.6).
    /// </summary>
    [Fact]
    public void ReadGroupPaths_IncludesNestedAndNowEmptyGroups()
    {
        var path = Seed();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            vault.RemoveEntry("servers/production");
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Contains("servers", reopened.ReadGroupPaths(), StringComparer.Ordinal);
        Assert.DoesNotContain("servers/production", reopened.ReadGroupPaths(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Guards a real defect: KeePassLib defaults <c>UseFileTransactions</c> to false and
    /// <c>PwDatabase.Close()</c> — which <c>Open()</c> calls first — resets it, so keypaste has
    /// to set it on the open path too. Without that, an interrupted <c>add</c> or <c>rm</c> would
    /// truncate a vault that was previously readable, which is a fail-open write (CORE.md law 3.7).
    /// <para>
    /// The flag is asserted directly. Observing the effect is not enough: an in-place save also
    /// leaves no debris behind, so a debris-only assertion would pass with the defect present.
    /// </para>
    /// </summary>
    [Fact]
    public void Save_AfterOpen_UsesFileTransactions_AndLeavesNoStrayFiles()
    {
        var path = Seed();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.True(
                vault.UsesFileTransactions,
                "Reopened vaults must save through a temporary file; KeePassLib resets this on Close().");

            vault.AddEntry(new VaultEntry { Title = "another", Password = "another-secret" });
            vault.Save();
        }

        var leftovers = Directory.GetFiles(_directory)
            .Where(f => !string.Equals(f, path, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(leftovers);

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.NotNull(reopened.Find("another"));
    }

    /// <summary>
    /// Reopening and saving must not re-randomise the key derivation: the KDF salt lives in the
    /// header, and rewriting it on every save would silently change how an existing vault is
    /// protected. This is why write-safety is applied separately from the format settings.
    /// </summary>
    [Fact]
    public void Save_AfterOpen_KeepsTheVaultReadableWithTheSameArgon2Parameters()
    {
        var path = Seed();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "third", Password = "third-secret" });
            vault.Save();
        }

        var header = KdbxHeader.Read(path);
        Assert.Equal(4, header.FormatMajorVersion);

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal("third-secret", reopened.Find("third")?.Password, StringComparer.Ordinal);
    }

    private string Seed()
    {
        var path = System.IO.Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "solo", Password = "solo-secret" });
        vault.AddEntry(new VaultEntry
        {
            Title = "production",
            Password = "prod-secret",
            GroupPath = "servers",
        });
        vault.Save();

        return path;
    }
}
