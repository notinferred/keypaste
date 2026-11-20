using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>KeePassXC opens a vault keypaste strengthened, at the Argon2 keypaste gave it, with the values it held.</summary>
public sealed class KeyDerivationTests
{
    [Theory]
    [InlineData("AES-KDF", "Argon2d")]
    [InlineData("Argon2id", "Argon2id")]
    public void KeePassXC_opens_a_strengthened_vault_with_its_values(string before, string after)
    {
        using var file = new KeePassXcVault();
        KdbxImportTests.WriteForeign(file.Path_, KeePassXcCli.Password, before, "AES-256");

        using (Internal.TestKeyDerivation.Production())
        using (var vault = file.Open())
        {
            Assert.True(vault.StrengthenKeyDerivation());
            vault.Save();
        }

        Assert.Matches($@"(?im)^\s*KDF:\s*{after}\b", KeePassXcCli.Run("db-info", file.Path_));
        Assert.Equal(["v2"], KeePassXcCli.Show(file.Path_, "Banking/Checking", "Password"));
    }
}
