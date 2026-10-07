using Keypaste.Core.HardwareKeys;
using Xunit;

namespace Keypaste.Core.Tests.HardwareKeys;

/// <summary>
/// A vault protected by a hardware key opens only with its password and the right response, fails
/// closed when the key is wrong, missing or silent, asks the key on every save, and gains or loses
/// the factor only through an access change.
/// </summary>
public sealed class HardwareKeyVaultTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly byte[] _secret = Convert.FromHexString("0f1e2d3c4b5a69788796a5b4c3d2e1f00112233a");
    private static readonly byte[] _otherSecret = Convert.FromHexString("a0b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-hardware-key-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_vault_with_a_hardware_key_opens_with_the_password_and_the_right_response()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var vault = Vault.Open(path, _master, null, key);

        Assert.Equal("value", Assert.Single(vault.ReadEntries()).Password);
        Assert.Same(key, vault.HardwareKey);
    }

    [Fact]
    public void A_reload_asks_the_key_about_the_file_another_writer_saved()
    {
        var device = new SoftwareYubiKey(_secret);
        var path = HardwareKeyVault("vault.kdbx", device);
        using var key = new HardwareKey(device, 2);
        using var vault = Vault.Open(path, _master, null, key);

        using (var writerKey = new HardwareKey(device, 2))
        using (var writer = Vault.Open(path, _master, null, writerKey))
        {
            writer.UpdateEntry(new VaultEntry { Title = "TOKEN", Password = "external", GroupPath = "env/demo" });
            writer.Save();
        }

        var asked = device.Challenges.Count;

        using var reloaded = vault.Reload();

        Assert.True(device.Challenges.Count > asked);
        Assert.Equal("external", Assert.Single(reloaded.ReadEntries()).Password);
    }

    [Fact]
    public void Another_keys_secret_is_refused()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        using var key = new HardwareKey(new SoftwareYubiKey(_otherSecret), 2);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _master, null, key));
    }

    [Fact]
    public void A_slot_programmed_for_fixed_length_challenges_is_refused()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        using var key = new HardwareKey(new SoftwareYubiKey(_secret, variableLength: false), 2);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _master, null, key));
    }

    [Fact]
    public void The_password_alone_is_refused()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _master));
    }

    [Fact]
    public void The_right_key_with_the_wrong_password_is_refused()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 2);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, "not the password", null, key));
    }

    [Fact]
    public void A_missing_key_opens_nothing_and_says_so()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        using var key = new HardwareKey(new SoftwareYubiKey(_secret) { Connected = false }, 2);

        var refused = Assert.Throws<HardwareKeyException>(() => Vault.Open(path, _master, null, key));
        Assert.Equal(HardwareKeyFailure.NotFound, refused.Failure);
    }

    [Fact]
    public void Every_save_asks_the_key_with_a_new_challenge_and_the_vault_still_opens()
    {
        var device = new SoftwareYubiKey(_secret);
        var path = HardwareKeyVault("vault.kdbx", device);

        using (var key = new HardwareKey(device, 2))
        using (var vault = Vault.Open(path, _master, null, key))
        {
            var asked = device.Challenges.Count;
            vault.AddEntry(new VaultEntry { Title = "SECOND", Password = "two", GroupPath = "env/demo" });
            vault.Save();

            Assert.Equal(asked + 1, device.Challenges.Count);
            Assert.NotEqual(device.Challenges[^2], device.Challenges[^1]);
        }

        using var again = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var reopened = Vault.Open(path, _master, null, again);
        Assert.Equal(2, reopened.ReadEntries().Count);
    }

    [Fact]
    public void A_save_the_key_does_not_answer_writes_nothing()
    {
        var device = new SoftwareYubiKey(_secret);
        var path = HardwareKeyVault("vault.kdbx", device);

        using var key = new HardwareKey(device, 2);
        using var vault = Vault.Open(path, _master, null, key);
        var bytes = File.ReadAllBytes(path);

        device.Connected = false;
        vault.AddEntry(new VaultEntry { Title = "SECOND", Password = "two", GroupPath = "env/demo" });

        var refused = Assert.Throws<HardwareKeyException>(vault.Save);
        Assert.Equal(HardwareKeyFailure.NotFound, refused.Failure);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Adding_a_hardware_key_makes_it_required_and_asks_it_once()
    {
        var path = PasswordVault("vault.kdbx");
        var device = new SoftwareYubiKey(_secret);
        using var key = new HardwareKey(device, 2);

        VaultAccessResult result;
        using (var vault = Vault.Open(path, _master))
        {
            result = vault.ChangeAccess(Attach(key), [], []);
        }

        Assert.Equal(VaultAccessOutcome.Changed, result.Outcome);
        Assert.Single(device.Challenges);
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _master));

        using var fresh = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var opened = Vault.Open(path, _master, null, fresh);
        Assert.Single(opened.ReadEntries());

        // The copy the change kept is the vault as it was, under the password alone.
        using var kept = Vault.Open(result.Kept!.Path, _master);
        Assert.Single(kept.ReadEntries());
    }

    [Fact]
    public void Removing_the_hardware_key_leaves_the_password_alone_opening_the_vault()
    {
        var path = HardwareKeyVault("vault.kdbx", new SoftwareYubiKey(_secret));

        using (var key = new HardwareKey(new SoftwareYubiKey(_secret), 2))
        using (var vault = Vault.Open(path, _master, null, key))
        {
            var result = vault.ChangeAccess(
                new VaultAccessChange(false, AccessKeyfileChange.Keep) { HardwareKeyChange = AccessHardwareKeyChange.Remove }, [], []);

            Assert.Equal(VaultAccessOutcome.Changed, result.Outcome);
        }

        using var opened = Vault.Open(path, _master);
        Assert.Null(opened.HardwareKey);
        Assert.Single(opened.ReadEntries());
    }

    [Fact]
    public void A_key_that_is_not_connected_is_not_attached_and_nothing_is_written()
    {
        var path = PasswordVault("vault.kdbx");
        var bytes = File.ReadAllBytes(path);
        using var key = new HardwareKey(new SoftwareYubiKey(_secret) { Connected = false }, 2);

        using var vault = Vault.Open(path, _master);
        var result = vault.ChangeAccess(Attach(key), [], []);

        Assert.Equal(VaultAccessOutcome.HardwareKeyNotFound, result.Outcome);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(VaultBackups.List(path));
    }

    [Fact]
    public void A_slot_the_key_has_not_programmed_is_not_attached()
    {
        var path = PasswordVault("vault.kdbx");
        using var key = new HardwareKey(new SoftwareYubiKey(_secret) { Slots = [1] }, 2);

        using var vault = Vault.Open(path, _master);

        Assert.Equal(VaultAccessOutcome.HardwareKeyNotFound, vault.ChangeAccess(Attach(key), [], []).Outcome);
    }

    [Fact]
    public void A_vault_with_no_hardware_key_has_none_to_remove()
    {
        var path = PasswordVault("vault.kdbx");
        using var vault = Vault.Open(path, _master);

        var result = vault.ChangeAccess(
            new VaultAccessChange(false, AccessKeyfileChange.Keep) { HardwareKeyChange = AccessHardwareKeyChange.Remove }, [], []);

        Assert.Equal(VaultAccessOutcome.NoHardwareKeyToRemove, result.Outcome);
    }

    [Fact]
    public void The_keyfile_of_a_vault_with_no_password_is_not_removed_to_leave_the_hardware_key_alone()
    {
        var keyfile = Path.Combine(_directory, "vault.key");
        File.WriteAllBytes(keyfile, [.. Enumerable.Range(1, 32).Select(i => (byte)i)]);
        var path = Path.Combine(_directory, "vault.kdbx");
        using (var key = new HardwareKey(new SoftwareYubiKey(_secret), 2))
        using (var created = Vault.CreateWith(path, string.Empty, keyfile, key))
        {
            created.Save();
        }

        using var again = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var vault = Vault.Open(path, string.Empty, keyfile, again);
        var result = vault.ChangeAccess(new VaultAccessChange(false, AccessKeyfileChange.Remove), [], []);

        Assert.Equal(VaultAccessOutcome.WouldLeaveNoPassword, result.Outcome);
    }

    [Fact]
    public void A_backup_of_a_hardware_key_vault_is_checked_with_the_key()
    {
        var device = new SoftwareYubiKey(_secret);
        var path = HardwareKeyVault("vault.kdbx", device);
        using (var key = new HardwareKey(device, 2))
        using (var vault = Vault.Open(path, _master, null, key))
        {
            vault.AddEntry(new VaultEntry { Title = "SECOND", Password = "two", GroupPath = "env/demo" });
            vault.Save();
        }

        var backup = Assert.Single(VaultBackups.List(path));
        using var check = new HardwareKey(new SoftwareYubiKey(_secret), 2);

        Assert.Equal(1, VaultBackups.Inspect(path, backup, _master, null, check).Entries);
        Assert.Throws<InvalidMasterPasswordException>(() => VaultBackups.Inspect(path, backup, _master));
    }

    private static VaultAccessChange Attach(HardwareKey key) =>
        new(false, AccessKeyfileChange.Keep) { HardwareKeyChange = AccessHardwareKeyChange.Attach, HardwareKey = key };

    private string PasswordVault(string name)
    {
        var path = Path.Combine(_directory, name);
        using var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "value", GroupPath = "env/demo" });
        vault.Save();
        return path;
    }

    /// <summary>
    /// A save landing while the key is derived leaves the vault changed on disk.
    /// </summary>
    /// <remarks>
    /// The file is digested before it is read, as a reload does. Digested after the open, the vault
    /// stamped the new file with the contents of the old one, and its next save reverted the other
    /// write. The landing write renames over the vault, as savers do; Windows refuses that while the
    /// file is open for reading, so the race cannot be staged there.
    /// </remarks>
    [Fact]
    public void A_save_landing_while_the_key_is_derived_is_seen()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows refuses to rename over a file that is open for reading, so the write cannot land mid-open.");
        }

        var device = new SoftwareYubiKey(_secret);
        var path = HardwareKeyVault("vault.kdbx", device);
        var before = File.ReadAllBytes(path);

        using (var writerKey = new HardwareKey(device, 2))
        using (var writer = Vault.Open(path, _master, null, writerKey))
        {
            writer.UpdateEntry(new VaultEntry { Title = "TOKEN", Password = "external", GroupPath = "env/demo" });
            writer.Save();
        }

        var landed = Path.Combine(_directory, "landed.kdbx");
        File.Move(path, landed);
        File.WriteAllBytes(path, before);

        using var key = new HardwareKey(new Landing(device, () => File.Move(landed, path, overwrite: true)), 2);
        using var vault = Vault.Open(path, _master, null, key);

        Assert.Equal("value", Assert.Single(vault.ReadEntries()).Password);
        Assert.True(vault.HasFileChangedSinceOpen());
        Assert.Throws<VaultChangedOnDiskException>(vault.Save);

        using var readerKey = new HardwareKey(device, 2);
        using var reopened = Vault.Open(path, _master, null, readerKey);
        Assert.Equal("external", Assert.Single(reopened.ReadEntries()).Password);
    }

    private string HardwareKeyVault(string name, SoftwareYubiKey device)
    {
        var path = Path.Combine(_directory, name);
        using var key = new HardwareKey(device, 2);
        using var vault = Vault.CreateWith(path, _master, null, key);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "value", GroupPath = "env/demo" });
        vault.Save();
        return path;
    }

    /// <summary>A key that runs another program's save the first time it is asked, during the key derivation.</summary>
    private sealed class Landing(IChallengeResponseDevice device, Action land) : IChallengeResponseDevice
    {
        private Action? _land = land;

        public IReadOnlyList<HardwareKeyInfo> Find() => device.Find();

        public byte[] Respond(int slot, byte[] challenge, Action touchNeeded, CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _land, null)?.Invoke();
            return device.Respond(slot, challenge, touchNeeded, cancellationToken);
        }
    }
}
