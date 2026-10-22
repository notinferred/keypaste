using System.Reflection;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A Twofish vault, as KeePass's Twofish plugin and KeePassXC write one, opens and saves keeping its cipher (D-0292).</summary>
/// <remarks>keypaste never chooses Twofish, so each vault here is written by the vendored library with the cipher set by hand.</remarks>
public sealed class TwofishVaultTests : IDisposable
{
    private const string _password = "twofish-master";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-twofish-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_vault_written_with_Twofish_reads_back_with_its_cipher_and_values()
    {
        var path = Write("written.kdbx", kdbx31: false);

        PwDatabase database = new();
        try
        {
            database.Open(IOConnectionInfo.FromPath(path), Key(), null);

            Assert.Equal(TwofishCipherEngine.TwofishUuid, database.DataCipherUuid);
            var entry = Assert.Single(database.RootGroup.GetEntries(true));
            Assert.Equal("twofish-value", entry.Strings.ReadSafe(PwDefs.PasswordField));
        }
        finally
        {
            database.Close();
        }
    }

    [Theory]
    [InlineData(false, "KeePass database 4.0")]
    [InlineData(true, "KeePass database 3.1")]
    public void An_existing_Twofish_vault_opens_and_saves_as_KDBX_4_keeping_its_cipher(bool kdbx31, string version)
    {
        var path = Write("existing.kdbx", kdbx31);
        Assert.True(KdbxImport.TryProbe(path, out var before, out var error), error);
        Assert.Equal(version, before.Version);
        Assert.Equal("Twofish", before.Cipher);

        using (var vault = Vault.Open(path, _password))
        {
            Assert.Equal("twofish-value", vault.Find("TOKEN")?.Password);
            vault.AddEntry(new VaultEntry { Title = "added", Password = "added-value" });
            vault.Save();
        }

        Assert.True(KdbxImport.TryProbe(path, out var after, out error), error);
        Assert.StartsWith("KeePass database 4.", after.Version, StringComparison.Ordinal);
        Assert.Equal("Twofish", after.Cipher);
        Assert.Equal("AES-KDF", after.Kdf);

        using var reopened = Vault.Open(path, _password);
        Assert.Equal("twofish-value", reopened.Find("TOKEN")?.Password);
        Assert.Equal("added-value", reopened.Find("added")?.Password);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_wrong_password_does_not_open_a_Twofish_vault(bool kdbx31)
    {
        var path = Write("locked.kdbx", kdbx31);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, "not-" + _password));
    }

    /// <summary>One entry, written by the vendored library under Twofish and AES-KDF, as KDBX 4 or, through its internal version switch, 3.1.</summary>
    private string Write(string name, bool kdbx31)
    {
        var path = Path.Combine(_directory, name);

        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), Key());
            database.DataCipherUuid = TwofishCipherEngine.TwofishUuid;
            database.KdfParameters = new AesKdf().GetDefaultParameters();
            database.KdfParameters.SetUInt64(AesKdf.ParamRounds, 1000);

            PwEntry entry = new(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "TOKEN"));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "twofish-value"));
            database.RootGroup.AddEntry(entry, true);

            KdbxFile writer = new(database);
            if (kdbx31)
            {
                typeof(KdbxFile).GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(writer, 0x00030001u);
            }

            using var stream = File.Create(path);
            writer.Save(stream, null, KeePassLib.Serialization.KdbxFormat.Default, null);
        }
        finally
        {
            database.Close();
        }

        return path;
    }

    private static CompositeKey Key()
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(_password));
        return key;
    }
}
