using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The one keyfile keypaste writes: a KeePass XML keyfile, version 2.0, around a random key, written once where the
/// person chose and never over anything (D-0287).
/// </summary>
public sealed class KeyfileCreationTests : IDisposable
{
    private const string _password = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-keyfile-creation-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_created_keyfile_is_a_KeePass_2_0_XML_keyfile_whose_hash_matches_its_random_key()
    {
        var first = Path.Combine(_directory, "first.keyx");
        var second = Path.Combine(_directory, "second.keyx");

        Assert.Equal(KeyfileCreationOutcome.Created, VaultKeyfile.Create(first, out var failure));
        Assert.Empty(failure);
        Assert.Equal(KeyfileCreationOutcome.Created, VaultKeyfile.Create(second, out _));

        Assert.Equal(new KeyfileInspection(KeyfileOutcome.Accepted, KeyfileForm.Xml), VaultKeyfile.Inspect(first));

        var (version, key, hash) = Read(first);
        Assert.Equal("2.0", version);
        Assert.Equal(32, key.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(key).AsSpan(0, 4)), hash, ignoreCase: true);
        Assert.NotEqual(key, Read(second).Key);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(first));
        }

        Assert.Equal([first, second], Directory.EnumerateFiles(_directory).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_vault_created_with_a_created_keyfile_opens_with_it_and_not_with_another()
    {
        var keyfile = Path.Combine(_directory, "vault.keyx");
        var other = Path.Combine(_directory, "other.keyx");
        var path = Path.Combine(_directory, "vault.kdbx");
        Assert.Equal(KeyfileCreationOutcome.Created, VaultKeyfile.Create(keyfile, out _));
        Assert.Equal(KeyfileCreationOutcome.Created, VaultKeyfile.Create(other, out _));

        var outcome = VaultCreation.TryCreate(path, _password, _password, keyfile, out var created, out _);

        using (created)
        {
            Assert.Equal(VaultCreationOutcome.Created, outcome);
        }

        using (var reopened = Vault.Open(path, _password, keyfile))
        {
            Assert.Empty(reopened.ReadEntries());
        }

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _password, other));
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _password));
    }

    [Fact]
    public void Nothing_at_the_path_is_ever_replaced_and_a_failed_write_leaves_nothing()
    {
        var taken = Path.Combine(_directory, "taken.keyx");
        File.WriteAllText(taken, "somebody's file");
        var folder = Path.Combine(_directory, "folder.keyx");
        Directory.CreateDirectory(folder);
        var nowhere = Path.Combine(_directory, "missing", "vault.keyx");

        Assert.Equal(KeyfileCreationOutcome.PathAlreadyExists, VaultKeyfile.Create(taken, out _));
        Assert.Equal("somebody's file", File.ReadAllText(taken));
        Assert.Equal(KeyfileCreationOutcome.PathAlreadyExists, VaultKeyfile.Create(folder, out _));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));

        Assert.Equal(KeyfileCreationOutcome.Failed, VaultKeyfile.Create(nowhere, out var failure));
        Assert.NotEmpty(failure);
        Assert.False(Directory.Exists(Path.GetDirectoryName(nowhere)));
        Assert.Equal([taken], Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public void A_build_that_cannot_read_an_XML_keyfile_writes_none()
    {
        var path = Path.Combine(_directory, "vault.keyx");

        using (VaultKeyfile.SimulateXmlFallback())
        {
            Assert.Equal(KeyfileCreationOutcome.Unsupported, VaultKeyfile.Create(path, out _));
        }

        Assert.False(File.Exists(path));
    }

    private static (string Version, byte[] Key, string Hash) Read(string path)
    {
        var root = XDocument.Load(path).Root!;
        var data = root.Element("Key")!.Element("Data")!;

        return (
            root.Element("Meta")!.Element("Version")!.Value,
            Convert.FromHexString(string.Concat(data.Value.Where(char.IsAsciiHexDigit))),
            data.Attribute("Hash")!.Value);
    }
}
