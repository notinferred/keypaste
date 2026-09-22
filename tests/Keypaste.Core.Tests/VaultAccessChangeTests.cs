using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Changing what unlocks a vault (V.1a2): the new credentials open it, the old ones do not, every
/// refusal leaves the file byte-identical, and the file the change replaced is kept.
/// </summary>
public sealed class VaultAccessChangeTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _next = "a different horse entirely";

    private readonly string _directory;

    public VaultAccessChangeTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-access-tests-").FullName;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    // ---------------------------------------------------------------------------------------
    // Changes that succeed
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_new_password_opens_the_vault_and_the_old_one_does_not()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Change(path, _master, null, new VaultAccessChange(true, AccessKeyfileChange.Keep), _next);

        Assert.Equal(VaultAccessOutcome.Changed, result.Outcome);
        AssertOpens(path, _next, null);
        AssertRefused(path, _master, null);
    }

    [Theory]
    [InlineData(KeyfileForm.Xml)]
    [InlineData(KeyfileForm.Binary32)]
    [InlineData(KeyfileForm.Hex64)]
    public void An_attached_keyfile_is_required_from_then_on(KeyfileForm form)
    {
        var path = PasswordVault("vault.kdbx");
        var keyfile = KeyfileOf(form, "attached");

        var result = Change(path, _master, null, Attach(keyfile));

        Assert.Equal(VaultAccessOutcome.Changed, result.Outcome);
        AssertOpens(path, _master, keyfile);
        AssertRefused(path, _master, null);
    }

    [Fact]
    public void A_replaced_keyfile_opens_the_vault_and_the_old_one_does_not()
    {
        var first = KeyfileOf(KeyfileForm.Xml, "first");
        var second = KeyfileOf(KeyfileForm.Binary32, "second");
        var path = KeyfileVault("vault.kdbx", _master, first);

        Change(path, _master, first, Attach(second));

        AssertOpens(path, _master, second);
        AssertRefused(path, _master, first);
    }

    [Fact]
    public void A_removed_keyfile_leaves_the_password_alone_opening_the_vault()
    {
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");
        var path = KeyfileVault("vault.kdbx", _master, keyfile);

        Change(path, _master, keyfile, new VaultAccessChange(false, AccessKeyfileChange.Remove));

        AssertOpens(path, _master, null);
        AssertRefused(path, _master, keyfile);
    }

    [Fact]
    public void A_keyfile_only_vault_can_swap_its_keyfile_and_stay_keyfile_only()
    {
        var first = KeyfileOf(KeyfileForm.Xml, "first");
        var second = KeyfileOf(KeyfileForm.Hex64, "second");
        var path = PasswordlessVault("vault.kdbx", first);

        Change(path, string.Empty, first, Attach(second));

        AssertOpens(path, string.Empty, second);
        AssertRefused(path, string.Empty, first);
    }

    [Fact]
    public void A_keyfile_only_vault_can_gain_a_password_and_keep_its_keyfile()
    {
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");
        var path = PasswordlessVault("vault.kdbx", keyfile);

        Change(path, string.Empty, keyfile, new VaultAccessChange(true, AccessKeyfileChange.Keep), _next);

        AssertOpens(path, _next, keyfile);
        AssertRefused(path, string.Empty, keyfile);
    }

    [Fact]
    public void A_keyfile_only_vault_can_trade_its_keyfile_for_a_password()
    {
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");
        var path = PasswordlessVault("vault.kdbx", keyfile);

        Change(path, string.Empty, keyfile, new VaultAccessChange(true, AccessKeyfileChange.Remove), _next);

        AssertOpens(path, _next, null);
        AssertRefused(path, string.Empty, keyfile);
    }

    [Fact]
    public void Attaching_a_keyfile_keeps_the_password_in_the_key()
    {
        var path = PasswordVault("vault.kdbx");
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");

        Change(path, _master, null, Attach(keyfile));

        AssertRefused(path, string.Empty, keyfile);
    }

    [Fact]
    public void A_password_change_on_a_hashed_keyfile_vault_keeps_that_keyfile()
    {
        var hashed = KeyfileOf(KeyfileForm.HashedFile, "hashed");
        var path = KeyfileVault("vault.kdbx", _master, hashed);

        Change(path, _master, hashed, new VaultAccessChange(true, AccessKeyfileChange.Keep), _next);

        AssertOpens(path, _next, hashed);
        AssertRefused(path, _next, null);
    }

    [Fact]
    public void The_key_derivation_cipher_and_version_are_the_vaults_own_after_a_change()
    {
        var path = Path("aes.kdbx");
        WriteRaw(path, key => key.AddUserKey(new KcpPassword(System.Text.Encoding.UTF8.GetBytes(_master), false)));
        var before = Settings(path, Key(_master, null));
        var version = KdbxHeader.Read(path);

        Change(path, _master, null, new VaultAccessChange(true, AccessKeyfileChange.Keep), _next);

        Assert.Equal(before, Settings(path, Key(_next, null)));
        Assert.Equal(version, KdbxHeader.Read(path));
    }

    [Fact]
    public void The_change_keeps_the_file_it_replaced_even_inside_the_floor()
    {
        var path = PasswordVault("vault.kdbx");

        using (var vault = Vault.Open(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "SECOND", Password = "value", GroupPath = "env/demo" });
            vault.Save();

            var result = vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next);

            Assert.Equal(VaultAccessOutcome.Changed, result.Outcome);
            var kept = Assert.IsType<VaultBackup>(result.Kept);
            Assert.Equal(kept, VaultBackups.List(path)[0]);
        }

        var backups = VaultBackups.List(path);
        Assert.Equal(2, backups.Count);
        Assert.Equal(2, VaultBackups.Inspect(path, backups[0], _master).Entries);
    }

    [Fact]
    public void A_backup_taken_before_the_change_still_opens_under_the_old_credentials()
    {
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");
        var path = PasswordVault("vault.kdbx");

        Change(path, _master, null, Attach(keyfile));

        var backup = Assert.Single(VaultBackups.List(path));
        Assert.Equal(1, VaultBackups.Inspect(path, backup, _master).Entries);
        Assert.Throws<InvalidMasterPasswordException>(() => VaultBackups.Inspect(path, backup, _master, keyfile));
    }

    [Fact]
    public void A_change_leaves_the_unsaved_changes_flag_clear()
    {
        var path = PasswordVault("vault.kdbx");

        using var vault = Vault.Open(path, _master);
        vault.Modified = true;

        vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next);

        Assert.False(vault.Modified);
    }

    // ---------------------------------------------------------------------------------------
    // Refusals: the file is byte-identical and no backup is written
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_hashed_keyfile_is_not_attached()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Refused(path, _master, null, Attach(KeyfileOf(KeyfileForm.HashedFile, "any")));

        Assert.Equal(VaultAccessOutcome.KeyfileIsFragile, result.Outcome);
        Assert.Equal(KeyfileForm.HashedFile, result.Keyfile.Form);
    }

    [Fact]
    public void A_missing_keyfile_is_not_attached()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Refused(path, _master, null, Attach(Path("nothing-here.key")));

        Assert.Equal(VaultAccessOutcome.KeyfileUnusable, result.Outcome);
        Assert.Equal(KeyfileOutcome.Missing, result.Keyfile.Outcome);
    }

    [Fact]
    public void The_vault_itself_is_not_attached_as_its_keyfile()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Refused(path, _master, null, Attach(path));

        Assert.Equal(VaultAccessOutcome.KeyfileIsThisVault, result.Outcome);
    }

    [Fact]
    public void A_file_in_the_vaults_backup_directory_is_not_attached()
    {
        var path = PasswordVault("vault.kdbx");
        var directory = Directory.CreateDirectory(VaultBackups.DirectoryFor(path)).FullName;
        var inside = System.IO.Path.Combine(directory, "raw.key");
        File.WriteAllBytes(inside, new byte[32]);

        var result = Refused(path, _master, null, Attach(inside));

        Assert.Equal(VaultAccessOutcome.KeyfileIsThisVault, result.Outcome);
    }

    [Fact]
    public void Removing_the_only_keyfile_without_a_password_is_refused()
    {
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");
        var path = PasswordlessVault("vault.kdbx", keyfile);

        var result = Refused(path, string.Empty, keyfile, new VaultAccessChange(false, AccessKeyfileChange.Remove));

        Assert.Equal(VaultAccessOutcome.WouldLeaveNoPassword, result.Outcome);
    }

    [Fact]
    public void Removing_a_keyfile_the_vault_does_not_have_is_refused()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Refused(path, _master, null, new VaultAccessChange(false, AccessKeyfileChange.Remove));

        Assert.Equal(VaultAccessOutcome.NoKeyfileToRemove, result.Outcome);
    }

    [Fact]
    public void An_empty_new_password_is_refused()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Refused(path, _master, null, new VaultAccessChange(true, AccessKeyfileChange.Keep), string.Empty);

        Assert.Equal(VaultAccessOutcome.EmptyPassword, result.Outcome);
    }

    [Fact]
    public void A_confirmation_that_does_not_match_is_refused()
    {
        var path = PasswordVault("vault.kdbx");
        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, _master))
        {
            var result = vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next + "!");
            Assert.Equal(VaultAccessOutcome.PasswordsDoNotMatch, result.Outcome);
        }

        AssertUntouched(path, bytes);
    }

    [Fact]
    public void Asking_for_no_change_is_refused()
    {
        var path = PasswordVault("vault.kdbx");

        var result = Refused(path, _master, null, new VaultAccessChange(false, AccessKeyfileChange.Keep));

        Assert.Equal(VaultAccessOutcome.NothingToChange, result.Outcome);
    }

    [Fact]
    public void A_wrong_current_secret_opens_nothing_to_change()
    {
        var path = PasswordVault("vault.kdbx");
        var bytes = File.ReadAllBytes(path);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, "not the password"));

        AssertUntouched(path, bytes);
    }

    [Fact]
    public void A_vault_changed_on_disk_is_not_rekeyed()
    {
        var path = PasswordVault("vault.kdbx");

        using var vault = Vault.Open(path, _master);
        using (var other = Vault.Open(path, _master))
        {
            other.AddEntry(new VaultEntry { Title = "THEIRS", Password = "value", GroupPath = "env/demo" });
            other.Save();
        }

        var bytes = File.ReadAllBytes(path);
        var backups = VaultBackups.List(path);

        Assert.Throws<VaultChangedOnDiskException>(
            () => vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next));

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(backups, VaultBackups.List(path));
        AssertOpens(path, _master, null);
    }

    [Fact]
    public void A_failed_backup_changes_nothing_and_the_open_vault_keeps_its_old_key()
    {
        var path = PasswordVault("vault.kdbx");
        var bytes = File.ReadAllBytes(path);
        var directory = VaultBackups.DirectoryFor(path);
        File.WriteAllText(directory, "not a directory");

        using var vault = Vault.Open(path, _master);
        Assert.Throws<VaultBackupException>(
            () => vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next));

        Assert.Equal(bytes, File.ReadAllBytes(path));

        File.Delete(directory);
        vault.AddEntry(new VaultEntry { Title = "LATER", Password = "value", GroupPath = "env/demo" });
        vault.Save();

        AssertOpens(path, _master, null);
        AssertRefused(path, _next, null);
    }

    [Fact]
    public void A_write_that_fails_after_the_copy_leaves_the_vault_and_the_copy()
    {
        var path = PasswordVault("vault.kdbx");
        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, _master))
        {
            var failure = Assert.Throws<VaultException>(() => vault.ChangeAccess(
                new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next,
                duringAttempt: _ => throw new InvalidOperationException("injected")));

            var copy = Assert.Single(VaultBackups.List(path));
            Assert.Contains(copy.Path, failure.Message, StringComparison.Ordinal);
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(VaultBackups.List(path)).Path));
    }

    [Fact]
    public void Bytes_the_new_credentials_do_not_open_never_replace_the_vault()
    {
        var path = PasswordVault("vault.kdbx");
        var keyfile = KeyfileOf(KeyfileForm.Binary32, "key");
        var bytes = File.ReadAllBytes(path);

        using (var vault = Vault.Open(path, _master))
        {
            // The keyfile changes between being read into the key and being read again to check the
            // bytes, which is the one way the new credentials can disagree with what was written.
            var failure = Assert.Throws<VaultException>(() => vault.ChangeAccess(
                Attach(keyfile), [], [],
                duringAttempt: _ => File.WriteAllBytes(keyfile, Enumerable.Repeat((byte)7, 32).ToArray())));

            Assert.Contains("did not open", failure.Message, StringComparison.Ordinal);
            Assert.Contains(Assert.Single(VaultBackups.List(path)).Path, failure.Message, StringComparison.Ordinal);
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));
        AssertOpens(path, _master, null);
    }

    // ---------------------------------------------------------------------------------------
    // A build whose XML keyfile loader falls back to hashing the file (D-0294)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void This_build_reads_the_key_inside_an_xml_keyfile()
    {
        Assert.True(VaultKeyfile.ReadsXmlKeyfiles);
    }

    [Fact]
    public void An_xml_keyfile_is_not_attached_by_a_build_that_would_hash_it()
    {
        var path = PasswordVault("vault.kdbx");
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");

        using var fallback = VaultKeyfile.SimulateXmlFallback();
        var result = Refused(path, _master, null, Attach(keyfile));

        Assert.Equal(VaultAccessOutcome.KeyfileUnusable, result.Outcome);
        Assert.Equal(KeyfileOutcome.XmlUnreadable, result.Keyfile.Outcome);
    }

    [Fact]
    public void An_xml_keyfile_vault_is_not_opened_by_a_build_that_would_hash_it()
    {
        var keyfile = KeyfileOf(KeyfileForm.Xml, "key");
        var path = KeyfileVault("vault.kdbx", _master, keyfile);

        using var fallback = VaultKeyfile.SimulateXmlFallback();
        var refused = Assert.Throws<UnreadableKeyfileException>(() => Vault.Open(path, _master, keyfile));

        Assert.Contains("cannot read one", refused.Message, StringComparison.Ordinal);
        Assert.Contains("The password is not the problem", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_xml_keyfile_still_keys_by_its_hash_in_a_working_build()
    {
        // KeePassXC keys a document it cannot parse as an XML keyfile by the file's hash, and so does
        // a working build of keypaste; only a build whose XML branch cannot run refuses (D-0292).
        var malformed = Text("malformed.keyx", "<KeyFile><Meta>cut off before it closes");
        Assert.Equal(new KeyfileInspection(KeyfileOutcome.Accepted, KeyfileForm.Xml), VaultKeyfile.Inspect(malformed));

        var path = PasswordlessVault("vault.kdbx", malformed);

        AssertOpens(path, string.Empty, malformed);
    }

    // ---------------------------------------------------------------------------------------
    // After a change
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_rekeyed_vault_refuses_every_later_write()
    {
        var path = PasswordVault("vault.kdbx");

        using var vault = Vault.Open(path, _master);
        vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next);
        var bytes = File.ReadAllBytes(path);

        Assert.Contains("open it again", Assert.Throws<VaultException>(vault.Save).Message, StringComparison.Ordinal);
        Assert.Throws<VaultException>(vault.SaveOverwriting);
        Assert.Throws<VaultException>(() => vault.ExportTo(Path("copy.kdbx")));
        Assert.Throws<VaultException>(
            () => vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), "third", "third"));

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path("copy.kdbx")));
    }

    [Fact]
    public void A_refused_change_does_not_seal_the_vault()
    {
        var path = PasswordVault("vault.kdbx");

        using var vault = Vault.Open(path, _master);
        vault.ChangeAccess(new VaultAccessChange(false, AccessKeyfileChange.Keep), [], []);
        vault.AddEntry(new VaultEntry { Title = "LATER", Password = "value", GroupPath = "env/demo" });
        vault.Save();

        AssertOpens(path, _master, null);
    }

    [Fact]
    public void Another_open_copy_of_the_vault_cannot_save_over_a_rekey()
    {
        var path = PasswordVault("vault.kdbx");

        using var stale = Vault.Open(path, _master);
        Change(path, _master, null, new VaultAccessChange(true, AccessKeyfileChange.Keep), _next);
        var bytes = File.ReadAllBytes(path);

        stale.AddEntry(new VaultEntry { Title = "STALE", Password = "value", GroupPath = "env/demo" });
        Assert.Throws<VaultChangedOnDiskException>(stale.Save);

        Assert.Equal(bytes, File.ReadAllBytes(path));
        AssertOpens(path, _next, null);
    }

    // ---------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------

    private static VaultAccessChange Attach(string keyfile) => new(false, AccessKeyfileChange.Attach, keyfile);

    private static VaultAccessResult Change(
        string path, string current, string? keyfile, VaultAccessChange change, string next = "")
    {
        using var vault = Vault.Open(path, current, keyfile);
        return vault.ChangeAccess(change, next, next);
    }

    private static VaultAccessResult Refused(
        string path, string current, string? keyfile, VaultAccessChange change, string next = "")
    {
        var bytes = File.ReadAllBytes(path);

        var result = Change(path, current, keyfile, change, next);

        Assert.Null(result.Kept);
        AssertUntouched(path, bytes);
        return result;
    }

    private static void AssertUntouched(string path, byte[] bytes)
    {
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(VaultBackups.List(path));
    }

    private static void AssertOpens(string path, string password, string? keyfile)
    {
        using var vault = Vault.Open(path, password, keyfile);
        Assert.NotEmpty(vault.ReadEntries());
    }

    private static void AssertRefused(string path, string password, string? keyfile) =>
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, password, keyfile));

    private string PasswordVault(string name)
    {
        var path = Path(name);
        using var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "value", GroupPath = "env/demo" });
        vault.Save();
        return path;
    }

    private string KeyfileVault(string name, string password, string keyfile)
    {
        var path = Path(name);
        using var vault = Vault.CreateWith(path, password, keyfile);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "value", GroupPath = "env/demo" });
        vault.Save();
        return path;
    }

    /// <summary>A vault a keyfile alone protects, written by the vendored library rather than keypaste.</summary>
    private string PasswordlessVault(string name, string keyfile)
    {
        var path = Path(name);
        WriteRaw(path, key => key.AddUserKey(new KcpKeyFile(keyfile)));
        return path;
    }

    /// <summary>
    /// A vault written by the vendored library with settings keypaste never chooses — AES-KDF and
    /// ChaCha20 — so keeping them proves they came from the file.
    /// </summary>
    private static void WriteRaw(string path, Action<CompositeKey> addFactors)
    {
        CompositeKey key = new();
        addFactors(key);

        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), key);
            database.DataCipherUuid = new ChaCha20Engine().CipherUuid;
            var kdf = new AesKdf();
            var parameters = kdf.GetDefaultParameters();
            parameters.SetUInt64(AesKdf.ParamRounds, 1000);
            database.KdfParameters = parameters;

            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new KeePassLib.Security.ProtectedString(false, "TOKEN"));
            database.RootGroup.AddEntry(entry, true);
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }

    private static CompositeKey Key(string password, string? keyfile)
    {
        CompositeKey key = new();
        if (password.Length > 0 || keyfile is null)
        {
            key.AddUserKey(new KcpPassword(System.Text.Encoding.UTF8.GetBytes(password), false));
        }

        if (keyfile is not null)
        {
            key.AddUserKey(new KcpKeyFile(keyfile));
        }

        return key;
    }

    /// <summary>The cipher and the key derivation with its parameters, leaving out the salt every save redraws.</summary>
    private static string Settings(string path, CompositeKey key)
    {
        PwDatabase database = new();
        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            var kdf = database.KdfParameters;
            var rounds = kdf.KdfUuid.Equals(new AesKdf().Uuid) ? kdf.GetUInt64(AesKdf.ParamRounds, 0) : 0;
            return $"{database.DataCipherUuid.ToHexString()} {kdf.KdfUuid.ToHexString()} {rounds}";
        }
        finally
        {
            database.Close();
        }
    }

    private string KeyfileOf(KeyfileForm form, string stem) => form switch
    {
        KeyfileForm.Xml => Text($"{stem}.keyx",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <KeyFile>
                <Meta><Version>2.0</Version></Meta>
                <Key><Data Hash="1E281D73">B243DCB7 D3F97ECC E1DB3620 C8B7D53B A0CE206E 92889C8C 75755038 EE5DCCDD</Data></Key>
            </KeyFile>
            """),
        KeyfileForm.Binary32 => Bytes($"{stem}.key", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
        KeyfileForm.Hex64 => Text($"{stem}.hex", new string('b', 64)),
        _ => Text($"{stem}.txt", "an ordinary file somebody picked, which is exactly the problem"),
    };

    private string Text(string name, string content)
    {
        var path = Path(name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Bytes(string name, byte[] content)
    {
        var path = Path(name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
