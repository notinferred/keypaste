using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Which key derivations are weaker than keypaste's, and strengthening one: the vault saves with keypaste's Argon2, keeps
/// Argon2id and any higher cost, opens with the same password and keeps the file it replaced whatever the backup floor says.
/// </summary>
public sealed class KeyDerivationStrengthTests : IDisposable
{
    private const string _password = "strengthen-password";

    private const ulong _mebibyte = 1024 * 1024;

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-kdf-strength-").FullName;

    private string VaultPath => Path.Combine(_directory, "old.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(KeyDerivationFunction.AesKdf, 60_000UL, 0UL, 0U)]
    [InlineData(KeyDerivationFunction.AesKdf, 100_000_000UL, 0UL, 0U)]
    [InlineData(KeyDerivationFunction.Argon2d, 1UL, 64 * _mebibyte, 2U)]
    [InlineData(KeyDerivationFunction.Argon2d, 2UL, 16 * _mebibyte, 2U)]
    [InlineData(KeyDerivationFunction.Argon2id, 2UL, 64 * _mebibyte, 1U)]
    [InlineData(KeyDerivationFunction.Argon2id, 10UL, 32 * _mebibyte, 8U)]
    public void AES_KDF_and_Argon2_below_keypastes_cost_in_any_parameter_are_weaker(KeyDerivationFunction function, ulong iterations, ulong memory, uint parallelism)
    {
        using var production = TestKeyDerivation.Production();

        Assert.True(new VaultKeyDerivation(function, iterations, memory, parallelism).IsWeakerThanKeypastes);
    }

    [Theory]
    [InlineData(KeyDerivationFunction.Argon2d, 2UL, 64 * _mebibyte, 2U)]
    [InlineData(KeyDerivationFunction.Argon2id, 2UL, 64 * _mebibyte, 2U)]
    [InlineData(KeyDerivationFunction.Argon2id, 4UL, 256 * _mebibyte, 4U)]
    [InlineData(KeyDerivationFunction.Unknown, 0UL, 0UL, 0U)]
    public void Keypastes_own_Argon2_and_any_stronger_are_not_weaker(KeyDerivationFunction function, ulong iterations, ulong memory, uint parallelism)
    {
        using var production = TestKeyDerivation.Production();

        Assert.False(new VaultKeyDerivation(function, iterations, memory, parallelism).IsWeakerThanKeypastes);
    }

    [Fact]
    public void Strengthening_Argon2id_keeps_it_and_every_cost_above_keypastes()
    {
        using var production = TestKeyDerivation.Production();

        Assert.Equal(
            new VaultKeyDerivation(KeyDerivationFunction.Argon2id, 2, 256 * _mebibyte, 4),
            new VaultKeyDerivation(KeyDerivationFunction.Argon2id, 1, 256 * _mebibyte, 4).Strengthened);
        Assert.Equal(
            new VaultKeyDerivation(KeyDerivationFunction.Argon2d, KdbxFormat.Argon2Iterations, KdbxFormat.Argon2Memory, KdbxFormat.Argon2Parallelism),
            new VaultKeyDerivation(KeyDerivationFunction.AesKdf, 60_000, 0, 0).Strengthened);
    }

    [Fact]
    public void An_AES_KDF_vault_strengthened_opens_with_its_password_at_keypastes_Argon2_and_keeps_the_file_it_replaced()
    {
        KdbxImportTests.WriteForeign(VaultPath, _password, "AES-KDF", "AES-256");
        Assert.Equal(KeyDerivationFunction.AesKdf, VaultKeyDerivation.Read(VaultPath)?.Function);
        IReadOnlyList<VaultEntry> before;

        using (var vault = ClaimedVault.Open(VaultPath, _password))
        {
            before = vault.ReadEntries();
            vault.AddEntry(new VaultEntry { Title = "added", Password = "added-password" });
            vault.Save();
        }

        Assert.Single(VaultBackups.List(VaultPath));

        using (var vault = ClaimedVault.Open(VaultPath, _password))
        {
            Assert.True(vault.KeyDerivation.IsWeakerThanKeypastes);
            Assert.True(vault.StrengthenKeyDerivation());
            Assert.False(vault.KeyDerivation.IsWeakerThanKeypastes);
            vault.Save();
        }

        Assert.Equal(VaultKeyDerivation.Keypastes, VaultKeyDerivation.Read(VaultPath));
        Assert.Equal(2, VaultBackups.List(VaultPath).Count);
        Assert.Equal(KeyDerivationFunction.AesKdf, VaultKeyDerivation.Read(VaultBackups.List(VaultPath)[0].Path)?.Function);

        using var reopened = ClaimedVault.Open(VaultPath, _password);
        Assert.Equal(before.Count + 1, reopened.ReadEntries().Count);
        Assert.False(reopened.KeyDerivation.IsWeakerThanKeypastes);
    }

    [Fact]
    public void A_weak_Argon2id_vault_strengthened_stays_Argon2id_at_keypastes_cost()
    {
        KdbxImportTests.WriteForeign(VaultPath, _password, "Argon2id", "ChaCha20");
        using var production = TestKeyDerivation.Production();

        using (var vault = ClaimedVault.Open(VaultPath, _password))
        {
            Assert.True(vault.StrengthenKeyDerivation());
            vault.Save();
        }

        Assert.Equal(
            new VaultKeyDerivation(KeyDerivationFunction.Argon2id, KdbxFormat.Argon2Iterations, KdbxFormat.Argon2Memory, KdbxFormat.Argon2Parallelism),
            VaultKeyDerivation.Read(VaultPath));
        using var reopened = ClaimedVault.Open(VaultPath, _password);
        Assert.NotEmpty(reopened.ReadEntries());
    }

    [Fact]
    public void A_vault_at_keypastes_cost_is_left_as_it_is()
    {
        using var vault = ClaimedVault.Create(VaultPath, _password);
        vault.Save();
        var saved = File.ReadAllBytes(VaultPath);

        Assert.False(vault.StrengthenKeyDerivation());
        Assert.Equal(VaultKeyDerivation.Keypastes, vault.KeyDerivation);
        Assert.Equal(saved, File.ReadAllBytes(VaultPath));
    }
}
