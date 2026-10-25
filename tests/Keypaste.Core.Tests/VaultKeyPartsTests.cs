using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault's key parts, as the desktop's quick unlock keeps them (D-0550): exported after a password unlock, they open
/// the vault as that unlock did, hold the password only as its SHA-256 and a hardware key only as its slot, and stop
/// opening it once its key is changed.
/// </summary>
public sealed class VaultKeyPartsTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _next = "a later master password";

    private static readonly byte[] _secret = Convert.FromHexString("0f1e2d3c4b5a69788796a5b4c3d2e1f00112233a");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-key-parts-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void The_parts_a_password_unlock_exports_open_the_vault_as_it_did(bool password, bool keyfile)
    {
        var key = keyfile ? Keyfile("vault.key") : null;
        var typed = password ? _master : string.Empty;
        var path = Create("vault.kdbx", typed, key);

        using var parts = Export(path, typed, key);
        using var vault = Vault.Open(path, parts, null);

        Assert.Equal("value", vault.Find("github")?.Password);
        Assert.Equal(password, vault.HasPassword);
        Assert.Equal(key, vault.KeyfilePath);
        Assert.Equal(key, parts.KeyfilePath);
        Assert.Null(parts.HardwareKeySlot);
    }

    [Fact]
    public void The_parts_hold_the_passwords_SHA_256_and_never_the_password()
    {
        var path = Create("vault.kdbx", _master, null);

        using var parts = Export(path, _master, null);
        var bytes = parts.ToBytes();

        Assert.True(Holds(bytes, SHA256.HashData(Encoding.UTF8.GetBytes(_master))));
        Assert.False(Holds(bytes, Encoding.UTF8.GetBytes(_master)));
    }

    [Fact]
    public void A_hardware_key_vault_keeps_only_the_slot_and_asks_the_key_again()
    {
        var device = new SoftwareYubiKey(_secret);
        var path = System.IO.Path.Combine(_directory, "yubikey.kdbx");

        using (var key = new HardwareKey(device, 2))
        using (var created = Vault.CreateWith(path, _master, null, key))
        {
            created.AddEntry(new VaultEntry { Title = "github", Password = "value" });
            created.Save();
        }

        VaultKeyParts parts;
        byte[] answer;

        using (var key = new HardwareKey(device, 2))
        using (var opened = Vault.Open(path, _master, null, key))
        {
            parts = opened.ExportKeyParts();
            answer = SoftwareYubiKey.Answer(_secret, device.Challenges[^1], variableLength: true);
        }

        using (parts)
        {
            var bytes = parts.ToBytes();
            Assert.Equal(2, parts.HardwareKeySlot);
            Assert.False(Holds(bytes, answer));
            Assert.False(Holds(bytes, SHA256.HashData(answer)));

            var asked = device.Challenges.Count;
            using var key = new HardwareKey(device, 2);
            using var resumed = Vault.Open(path, parts, key);

            Assert.Equal(asked + 1, device.Challenges.Count);
            Assert.Equal("value", resumed.Find("github")?.Password);
            Assert.Same(key, resumed.HardwareKey);
            Assert.Throws<ArgumentException>(() => Vault.Open(path, parts, null));
        }
    }

    [Fact]
    public void A_vault_opened_from_its_parts_saves_reloads_and_changes_its_keyfile_keeping_its_password()
    {
        var first = Keyfile("first.key");
        var second = Keyfile("second.key");
        var path = Create("vault.kdbx", _master, first);
        using var parts = Export(path, _master, first);

        using (var resumed = Vault.Open(path, parts, null))
        {
            resumed.AddEntry(new VaultEntry { Title = "added", Password = "saved" });
            resumed.Save();

            using var reloaded = resumed.Reload();
            Assert.Equal(first, reloaded.KeyfilePath);
            Assert.Equal("saved", reloaded.Find("added")?.Password);
        }

        using (var resumed = Vault.Open(path, parts, null))
        {
            var change = new VaultAccessChange(false, AccessKeyfileChange.Attach, second);
            Assert.Equal(VaultAccessOutcome.Changed, resumed.ChangeAccess(change, string.Empty, string.Empty).Outcome);
        }

        using var reopened = Vault.Open(path, _master, second);
        Assert.Equal("saved", reopened.Find("added")?.Password);
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, string.Empty, second));
    }

    [Fact]
    public void A_vault_opened_from_its_parts_changes_its_password_keeping_its_keyfile()
    {
        var keyfile = Keyfile("vault.key");
        var path = Create("vault.kdbx", _master, keyfile);
        using var parts = Export(path, _master, keyfile);

        using (var resumed = Vault.Open(path, parts, null))
        {
            var change = new VaultAccessChange(true, AccessKeyfileChange.Keep);
            Assert.Equal(VaultAccessOutcome.Changed, resumed.ChangeAccess(change, _next, _next).Outcome);
        }

        using var reopened = Vault.Open(path, _next, keyfile);
        Assert.Equal("value", reopened.Find("github")?.Password);
    }

    [Fact]
    public void The_parts_no_longer_open_a_vault_whose_key_was_changed()
    {
        var path = Create("vault.kdbx", _master, null);
        using var parts = Export(path, _master, null);

        using (var vault = Vault.Open(path, _master))
        {
            vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next);
        }

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, parts, null));
    }

    [Fact]
    public void The_bytes_come_back_as_the_parts_and_anything_else_is_refused()
    {
        var keyfile = Keyfile("vault.key");
        var path = Create("vault.kdbx", _master, keyfile);
        using var parts = Export(path, _master, keyfile);
        var bytes = parts.ToBytes();

        using (var read = VaultKeyParts.FromBytes(bytes))
        {
            Assert.Equal(bytes, read.ToBytes());
            Assert.Equal(keyfile, read.KeyfilePath);
        }

        Assert.Throws<VaultException>(() => VaultKeyParts.FromBytes(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Throws<VaultException>(() => VaultKeyParts.FromBytes([.. bytes, 0]));
        Assert.Throws<VaultException>(() => VaultKeyParts.FromBytes([2, .. bytes[1..]]));
        Assert.Throws<VaultException>(() => VaultKeyParts.FromBytes([1, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Throws<VaultException>(() => VaultKeyParts.FromBytes([]));
    }

    [Fact]
    public void Disposing_the_parts_ends_them()
    {
        var path = Create("vault.kdbx", _master, null);
        var parts = Export(path, _master, null);

        parts.Dispose();

        Assert.Throws<ObjectDisposedException>(parts.ToBytes);
        Assert.Throws<ObjectDisposedException>(() => Vault.Open(path, parts, null));
    }

    private string Create(string name, string password, string? keyfile)
    {
        var path = System.IO.Path.Combine(_directory, name);
        using var vault = Vault.CreateWith(path, password, keyfile);
        vault.AddEntry(new VaultEntry { Title = "github", Password = "value" });
        vault.Save();
        return path;
    }

    private string Keyfile(string name)
    {
        var path = System.IO.Path.Combine(_directory, name);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
        return path;
    }

    private static VaultKeyParts Export(string path, string password, string? keyfile)
    {
        using var vault = Vault.Open(path, password, keyfile);
        return vault.ExportKeyParts();
    }

    private static bool Holds(byte[] bytes, byte[] part) => bytes.AsSpan().IndexOf(part) >= 0;
}
