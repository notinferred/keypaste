using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Opening a vault a keyfile protects: which files count as one, which are refused, and the
/// difference between a vault with an empty password and a vault with none.
/// </summary>
/// <remarks>
/// <para>
/// Two claims carry this file. The first is that <see cref="VaultKeyfile.Inspect"/> names the same
/// form the vendored <c>KcpKeyFile</c> actually keys with — they are two readings of one file and a
/// disagreement between them is a vault that opens in one program and not the other. Every
/// classification test here is paired with an open, so neither half can drift alone.
/// </para>
/// <para>
/// The second is about a composite key rather than a file. <c>KcpPassword</c> hashes whatever bytes
/// it is handed, so an empty password contributes SHA-256 of nothing — a real component — and
/// <c>CompositeKey</c> concatenates the components before hashing. A vault with no password is
/// therefore not a vault with an empty one, and treating them alike locks out every passwordless
/// vault KeePassXC ever wrote. <see cref="A_vault_with_no_password_is_not_a_vault_with_an_empty_one"/>
/// is the regression; without the guard in <c>BuildKey</c> it fails.
/// </para>
/// </remarks>
public sealed class VaultKeyfileTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory;

    public VaultKeyfileTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-keyfile-tests-").FullName;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    /// <summary>A KeePass XML keyfile, the form keypaste will itself write in V.1a2.</summary>
    private string XmlKeyfile(string name = "key.keyx")
    {
        var path = Path(name);
        File.WriteAllText(
            path,
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <KeyFile>
                <Meta><Version>2.0</Version></Meta>
                <Key><Data Hash="1E281D73">B243DCB7 D3F97ECC E1DB3620 C8B7D53B A0CE206E 92889C8C 75755038 EE5DCCDD</Data></Key>
            </KeyFile>
            """);
        return path;
    }

    private string Bytes(string name, byte[] content)
    {
        var path = Path(name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>A vault written to disk, protected by a master password alone.</summary>
    private string Vault(string name, string password)
    {
        var path = Path(name);
        using var vault = Core.Vault.Create(path, password);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "value", GroupPath = "env/demo" });
        vault.Save();
        return path;
    }

    // ---------------------------------------------------------------------------------------
    // What a keyfile is
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void An_xml_keyfile_is_recognised_as_one()
    {
        var inspection = VaultKeyfile.Inspect(XmlKeyfile());

        Assert.Equal(KeyfileOutcome.Accepted, inspection.Outcome);
        Assert.Equal(KeyfileForm.Xml, inspection.Form);
        Assert.False(inspection.IsFragile);
    }

    [Fact]
    public void A_file_of_exactly_thirty_two_bytes_is_key_material_itself()
    {
        var inspection = VaultKeyfile.Inspect(Bytes("raw.key", new byte[32]));

        Assert.Equal(KeyfileForm.Binary32, inspection.Form);
        Assert.False(inspection.IsFragile);
    }

    [Fact]
    public void A_file_of_sixty_four_hex_characters_is_decoded_rather_than_hashed()
    {
        var hex = System.Text.Encoding.ASCII.GetBytes(new string('a', 64));

        Assert.Equal(KeyfileForm.Hex64, VaultKeyfile.Inspect(Bytes("hex.key", hex)).Form);
    }

    [Fact]
    public void A_file_of_sixty_four_bytes_that_are_not_hex_is_hashed()
    {
        var notHex = System.Text.Encoding.ASCII.GetBytes(new string('z', 64));

        Assert.Equal(KeyfileForm.HashedFile, VaultKeyfile.Inspect(Bytes("notHex.key", notHex)).Form);
    }

    /// <summary>
    /// The edge the length rules create: an ordinary file is only hashed when it is not 32 bytes
    /// and not 64 hex characters. A note that happens to be 32 bytes long is read as raw key
    /// material by keypaste and by KeePassXC alike, and neither will say so.
    /// </summary>
    [Fact]
    public void An_arbitrary_file_that_happens_to_be_thirty_two_bytes_is_not_hashed()
    {
        var text = System.Text.Encoding.ASCII.GetBytes("32 bytes of perfectly ordinary t");
        Assert.Equal(32, text.Length);

        Assert.Equal(KeyfileForm.Binary32, VaultKeyfile.Inspect(Bytes("note.txt", text)).Form);
    }

    [Fact]
    public void Any_other_file_is_keyed_by_its_contents_and_says_it_is_fragile()
    {
        var inspection = VaultKeyfile.Inspect(
            Bytes("photo.jpg", System.Text.Encoding.ASCII.GetBytes("an ordinary file somebody picked, which is exactly the problem")));

        Assert.Equal(KeyfileForm.HashedFile, inspection.Form);
        Assert.True(inspection.IsFragile);
    }

    // ---------------------------------------------------------------------------------------
    // What a keyfile is not
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_keyfile_that_is_not_there_is_refused_by_name()
    {
        var inspection = VaultKeyfile.Inspect(Path("absent.keyx"));

        Assert.Equal(KeyfileOutcome.Missing, inspection.Outcome);
        Assert.False(inspection.Accepted);
        Assert.False(inspection.IsFragile);
    }

    [Fact]
    public void A_directory_is_not_a_keyfile()
    {
        var path = Path("a-folder");
        Directory.CreateDirectory(path);

        Assert.Equal(KeyfileOutcome.Unreadable, VaultKeyfile.Inspect(path).Outcome);
    }

    [Fact]
    public void An_empty_file_is_not_a_keyfile()
    {
        Assert.Equal(KeyfileOutcome.Empty, VaultKeyfile.Inspect(Bytes("empty.key", [])).Outcome);
    }

    /// <summary>
    /// A vault is never its own second factor. Somebody pointing the keyfile picker at the file
    /// they just chose as the vault is an ordinary slip, and one that would otherwise produce a
    /// vault whose key is derived from a file that changes every time it is saved.
    /// </summary>
    [Fact]
    public void A_vault_is_refused_as_its_own_keyfile()
    {
        var vault = Vault("self.kdbx", _master);

        var inspection = VaultKeyfile.Inspect(vault);

        Assert.Equal(KeyfileOutcome.IsAVault, inspection.Outcome);
        Assert.False(inspection.Accepted);
    }

    // ---------------------------------------------------------------------------------------
    // Opening, saving, and the factors a save keeps
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(KeyfileForm.Xml)]
    [InlineData(KeyfileForm.Binary32)]
    [InlineData(KeyfileForm.Hex64)]
    [InlineData(KeyfileForm.HashedFile)]
    public void Every_form_opens_the_vault_it_protects_and_keeps_protecting_it_after_a_save(KeyfileForm form)
    {
        var keyfile = KeyfileOf(form);
        Assert.Equal(form, VaultKeyfile.Inspect(keyfile).Form);

        var path = CreateWith(form + ".kdbx", _master, keyfile);

        using (var opened = Core.Vault.Open(path, _master, keyfile))
        {
            opened.AddEntry(new VaultEntry { Title = "SECOND", Password = "another", GroupPath = "env/demo" });
            opened.Save();
        }

        using (var reopened = Core.Vault.Open(path, _master, keyfile))
        {
            Assert.Equal(2, reopened.ReadEntries().Count);
        }

        // A save must not quietly re-key the file from the password alone.
        Assert.Throws<InvalidMasterPasswordException>(() => Core.Vault.Open(path, _master));
    }

    [Fact]
    public void The_wrong_keyfile_does_not_open_a_vault_and_changes_nothing()
    {
        var keyfile = XmlKeyfile();
        var path = CreateWith("wrong.kdbx", _master, keyfile);
        var before = File.ReadAllBytes(path);

        var other = Bytes("other.key", new byte[32]);

        Assert.Throws<InvalidMasterPasswordException>(() => Core.Vault.Open(path, _master, other));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void The_right_keyfile_with_the_wrong_password_does_not_open_a_vault()
    {
        var keyfile = XmlKeyfile();
        var path = CreateWith("wrongpw.kdbx", _master, keyfile);

        Assert.Throws<InvalidMasterPasswordException>(() => Core.Vault.Open(path, "not it", keyfile));
    }

    /// <summary>
    /// The regression for the whole of V.1a1's key handling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vault with no password at all is keyed by the keyfile alone. Adding an empty
    /// <c>KcpPassword</c> beside it contributes SHA-256 of nothing — a real 32-byte component —
    /// and <c>CompositeKey</c> concatenates the components before hashing, so the vault is refused,
    /// with exactly the message a wrong password gets, on a file that opens perfectly in KeePassXC.
    /// </para>
    /// <para>
    /// <b>The fixture must not come from keypaste.</b> A vault written through <c>BuildKey</c> and
    /// read back through <c>BuildKey</c> agrees with itself whatever <c>BuildKey</c> does, so a
    /// test built that way passes with the guard removed and proves nothing — which is exactly what
    /// the first version of this test did. <see cref="PasswordlessVault"/> therefore builds the
    /// composite key with the vendored library directly, the way KeePassXC does.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_vault_with_no_password_is_not_a_vault_with_an_empty_one()
    {
        var keyfile = XmlKeyfile();
        var path = PasswordlessVault("passwordless.kdbx", keyfile);

        using (var opened = Core.Vault.Open(path, string.Empty, keyfile))
        {
            Assert.Empty(opened.ReadEntries());
        }

        // Not merely "some password fails": the empty-password key is a different key, and the
        // vault must not answer to a typed one either.
        Assert.Throws<InvalidMasterPasswordException>(() => Core.Vault.Open(path, "anything", keyfile));

        // And with no keyfile there is nothing left, so an empty password stays a wrong one.
        Assert.Throws<InvalidMasterPasswordException>(() => Core.Vault.Open(path, string.Empty));
    }

    /// <summary>
    /// An empty password beside a keyfile is a different composite key from the keyfile alone.
    /// </summary>
    /// <remarks>
    /// The arithmetic the test above depends on, asserted directly against the vendored library so
    /// that a change in KeePassLib's key derivation is reported here rather than as a mysterious
    /// unlock failure.
    /// </remarks>
    [Fact]
    public void An_empty_password_component_changes_the_composite_key()
    {
        var keyfile = XmlKeyfile();

        KeePassLib.Keys.CompositeKey alone = new();
        alone.AddUserKey(new KeePassLib.Keys.KcpKeyFile(keyfile));

        KeePassLib.Keys.CompositeKey withEmptyPassword = new();
        withEmptyPassword.AddUserKey(new KeePassLib.Keys.KcpPassword([], false));
        withEmptyPassword.AddUserKey(new KeePassLib.Keys.KcpKeyFile(keyfile));

        Assert.False(alone.EqualsValue(withEmptyPassword));
    }

    [Fact]
    public void A_vault_with_a_password_and_no_keyfile_still_opens_with_neither_changed()
    {
        var path = Vault("plain.kdbx", _master);

        using var opened = Core.Vault.Open(path, _master);

        Assert.Single(opened.ReadEntries());
    }

    /// <summary>A copy kept beside a vault opens under exactly the factors the vault had.</summary>
    [Fact]
    public void A_backup_of_a_keyfile_vault_needs_the_same_two_factors()
    {
        var keyfile = XmlKeyfile();
        var path = CreateWith("backed.kdbx", _master, keyfile);

        using (var opened = Core.Vault.Open(path, _master, keyfile))
        {
            opened.AddEntry(new VaultEntry { Title = "SECOND", Password = "another", GroupPath = "env/demo" });
            opened.Save();
        }

        var backup = Assert.Single(VaultBackups.List(path));

        var summary = VaultBackups.Inspect(path, backup, _master, keyfile);
        Assert.Equal(1, summary.Entries);

        Assert.Throws<InvalidMasterPasswordException>(
            () => VaultBackups.Inspect(path, backup, _master));
    }

    // ---------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------

    private string KeyfileOf(KeyfileForm form) => form switch
    {
        KeyfileForm.Xml => XmlKeyfile(),
        KeyfileForm.Binary32 => Bytes("raw.key", new byte[32]),
        KeyfileForm.Hex64 => Bytes("hex.key", System.Text.Encoding.ASCII.GetBytes(new string('a', 64))),
        _ => Bytes("any.txt", System.Text.Encoding.ASCII.GetBytes("an ordinary file somebody picked, which is exactly the problem")),
    };

    /// <summary>
    /// A vault keyed by a keyfile and nothing else, written with the vendored library directly.
    /// </summary>
    /// <remarks>
    /// Deliberately not through <c>Vault.CreateWith</c>. This is the one fixture in the file that
    /// must be independent of the code under test: its whole purpose is to be a vault keypaste did
    /// not key, so that what <c>BuildKey</c> does on the way in cannot cancel out what it does on
    /// the way back. It is the in-process stand-in for what the compatibility gate does properly,
    /// where KeePassXC writes the file.
    /// </remarks>
    private string PasswordlessVault(string name, string keyfile)
    {
        var path = Path(name);

        KeePassLib.Keys.CompositeKey key = new();
        key.AddUserKey(new KeePassLib.Keys.KcpKeyFile(keyfile));

        KeePassLib.PwDatabase database = new();
        try
        {
            database.New(KeePassLib.Serialization.IOConnectionInfo.FromPath(path), key);
            database.Save(null);
        }
        finally
        {
            database.Close();
        }

        return path;
    }

    /// <summary>
    /// A vault protected by a password and a keyfile.
    /// </summary>
    /// <remarks>
    /// Written through <c>Vault.CreateWith</c>, the internal seam V.1a2 will build its
    /// <c>keypaste access</c> verb on. There is no public way to attach a keyfile yet, and a test
    /// that reached around the writer to make its own fixture would be testing its own fixture.
    /// </remarks>
    private string CreateWith(string name, string password, string keyfile)
    {
        var path = Path(name);
        using var vault = Core.Vault.CreateWith(path, password, keyfile);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "value", GroupPath = "env/demo" });
        vault.Save();
        return path;
    }

}
