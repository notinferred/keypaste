using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The Stage 0.2 secret-path tests: a vault keypaste writes must be a real KDBX4 container,
/// must give every field back unchanged when reopened, and must refuse a wrong password.
/// CORE.md law 4.5 makes tests on this path mandatory; law 3.7 is what the wrong-password
/// test enforces.
/// </summary>
/// <remarks>
/// Argon2 at 64 MiB costs real time per derivation, so these tests create and open vaults
/// sparingly. If this file ever needs to run faster, reduce the number of open/save cycles —
/// never the KDF parameters, which are the thing under test.
/// </remarks>
public sealed class VaultRoundTripTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private readonly string _directory;

    public VaultRoundTripTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// The core promise of Stage 0.2: create, add, save, reopen, read back — with every field
    /// identical, including a multi-line value and non-ASCII text.
    /// </summary>
    [Fact]
    public void Vault_RoundTripsEveryFieldVerbatim()
    {
        var path = VaultPath("round-trip.kdbx");
        var expected = new VaultEntry
        {
            Title = "Example Site",
            Username = "me@example.invalid",
            Password = "s3cr3t-P@ssw0rd",
            Url = "https://example.invalid/login",
            Notes = "first line\nsecond line: , ; = \" ' punctuation\ncafé — 日本語 — 🔑",
            GroupPath = "servers/production",
        };

        using (var created = Vault.Create(path, MasterPassword))
        {
            created.AddEntry(expected);
            created.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var actual = reopened.Find("servers/production/Example Site");

        Assert.NotNull(actual);
        Assert.Equal(expected.Title, actual.Title, StringComparer.Ordinal);
        Assert.Equal(expected.Username, actual.Username, StringComparer.Ordinal);
        Assert.Equal(expected.Password, actual.Password, StringComparer.Ordinal);
        Assert.Equal(expected.Url, actual.Url, StringComparer.Ordinal);
        Assert.Equal(expected.Notes, actual.Notes, StringComparer.Ordinal);
        Assert.Equal(expected.GroupPath, actual.GroupPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// Proves the container really is KDBX4 by reading the unencrypted twelve-byte prefix.
    /// A silent downgrade to KDBX 3.1 would still round-trip perfectly and still open in
    /// KeePassXC — but KDBX 3.1 cannot carry Argon2, so it would quietly weaken every vault
    /// keypaste writes. Only this assertion catches it.
    /// </summary>
    [Fact]
    public void Vault_WritesAKdbx4Container()
    {
        var path = VaultPath("format.kdbx");
        CreateVaultWithOneEntry(path);

        var header = KdbxHeader.Read(path);

        Assert.Equal(4, header.FormatMajorVersion);
    }

    /// <summary>
    /// The bytes on disk must carry the KDBX signature, and <see cref="KdbxHeader.Read"/> must
    /// reject anything that does not — otherwise it would happily "validate" a text file.
    /// </summary>
    [Fact]
    public void KdbxHeader_RejectsAFileThatIsNotKdbx()
    {
        var path = VaultPath("not-a-vault.kdbx");
        File.WriteAllText(path, "this is plainly not a KDBX file, but it is long enough");

        Assert.Throws<VaultException>(() => KdbxHeader.Read(path));
    }

    /// <summary>
    /// CORE.md law 3.7, fail closed. A wrong password must raise, not return an empty vault:
    /// a caller that treats "no entries" as "nothing to see" would turn a failed unlock into
    /// a silent success.
    /// </summary>
    [Fact]
    public void Vault_RejectsAWrongMasterPassword()
    {
        var path = VaultPath("wrong-password.kdbx");
        CreateVaultWithOneEntry(path);

        Assert.Throws<InvalidMasterPasswordException>(
            () => Vault.Open(path, "not the master password"));
    }

    /// <summary>
    /// Two saves of identical content must produce different bytes — the salt and nonces are
    /// regenerated per save — and both must still open and carry the same field values. This
    /// is what "byte-level reopenability" can mean for a format that is randomised by design;
    /// asserting byte-equality instead would be asserting a security defect.
    /// </summary>
    [Fact]
    public void Vault_ReopensAfterEverySave_EvenThoughTheBytesDiffer()
    {
        var first = VaultPath("save-1.kdbx");
        var second = VaultPath("save-2.kdbx");

        CreateVaultWithOneEntry(first);
        CreateVaultWithOneEntry(second);

        Assert.NotEqual(File.ReadAllBytes(first), File.ReadAllBytes(second));

        using var a = Vault.Open(first, MasterPassword);
        using var b = Vault.Open(second, MasterPassword);

        Assert.Equal(
            a.Find("only")?.Password,
            b.Find("only")?.Password,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Saving uses a file transaction so an interrupted write cannot truncate a readable
    /// vault. That mechanism is Windows-specific in places and degrades silently elsewhere,
    /// so this asserts on all three operating systems that it leaves nothing behind.
    /// </summary>
    [Fact]
    public void Save_LeavesNoStrayFilesBesideTheVault()
    {
        var path = VaultPath("clean.kdbx");
        CreateVaultWithOneEntry(path);

        var leftovers = Directory.GetFiles(_directory)
            .Where(f => !string.Equals(f, path, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(leftovers);
    }

    /// <summary>
    /// A disposed vault must not keep serving entries: the whole point of disposal is that the
    /// decrypted contents and key material are gone.
    /// </summary>
    [Fact]
    public void Vault_ThrowsAfterDispose()
    {
        var path = VaultPath("disposed.kdbx");
        CreateVaultWithOneEntry(path);

        var vault = Vault.Open(path, MasterPassword);
        vault.Dispose();

        Assert.Throws<ObjectDisposedException>(() => vault.ReadEntries());
    }

    private string VaultPath(string fileName)
    {
        return System.IO.Path.Combine(_directory, fileName);
    }

    private static void CreateVaultWithOneEntry(string path)
    {
        using var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "only", Password = "only-secret" });
        vault.Save();
    }
}
