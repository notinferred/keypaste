using System.Text;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Import;
using Keypaste.Core.Internal;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste import</c> of another password manager's export, over a fake reader and the readers keypaste ships: the
/// preview names where each item lands and what does not, <c>--skip-existing</c> skips what the vault already has, an
/// encrypted Bitwarden export is refused before any password is asked, a LastPass export lands, and nothing printed is
/// a value.
/// </summary>
public sealed class ImportExportVerbTests : IDisposable
{
    private const string _master = "import-export-master";

    private static readonly string[] _values =
    [
        "export-pw-github", "export-pw-bank", "export-user-bank", "export-notes-bank", "export-pin-bank", "export-old-pw-bank",
        "JBSWY3DPEHPK3PXP", "vault-pw-github", "export-standard-field", "export-user-mail", "export-pw-mail",
    ];

    private readonly CliHarness _cli = new();
    private readonly string _export;

    public ImportExportVerbTests()
    {
        _cli.SeedVault(_master, ("imported/Work/GitHub", "vault-pw-github"));
        _cli.Prompt.PromptsSeen.Clear();

        // A home the seeding's claims never touched, so a claim the import takes shows.
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Path.Combine(_cli.Directory, "home");
        _export = Path.Combine(_cli.Directory, "export.fake");
        File.WriteAllBytes(_export, ExportReader.Magic);
        _cli.ExportReaders = [new ExportReader()];
    }

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void A_dry_run_shows_where_each_item_lands_and_what_does_not_and_writes_nothing()
    {
        var before = File.ReadAllBytes(_cli.VaultPath);

        _cli.Prompt.Enqueue(_master);
        var exit = _cli.Run("import", _export, "--vault", _cli.VaultPath, "--into", "imported", "--dry-run");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Master password: "], _cli.Prompt.PromptsSeen);
        Assert.Contains("  export.fake · Fake · 2 items", _cli.Err, StringComparison.Ordinal);
        Assert.Equal(
            Lines(
                "  2 items of 2 to copy into imported",
                "    Work/GitHub  → imported/Work/GitHub (2)      ('GitHub' is already an item there)",
                "    Bank         → imported/Bank",
                "  not imported:",
                "    Bank: field Password ('Password' is one of the item's standard fields, not a custom field)",
                "  nothing was written (--dry-run)"),
            _cli.Out);
        Assert.Equal(before, File.ReadAllBytes(_cli.VaultPath));
        Assert.False(
            Directory.Exists(Path.Combine(_cli.Environment[KeypasteHome.EnvironmentVariable], VaultClaim.DirectoryName)),
            "a dry run took the vault's claim");
        AssertNoValue();
    }

    [Fact]
    public void Skip_existing_keeps_the_vaults_item_copies_the_rest_and_names_both()
    {
        _cli.Prompt.Enqueue(_master);
        var exit = _cli.Run("import", _export, "--vault", _cli.VaultPath, "--into", "imported", "--skip-existing");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Master password: "], _cli.Prompt.PromptsSeen);
        Assert.Contains(
            Lines(
                "  ✓ 1 item · 0 projects · copied into imported",
                "  skipped: Work/GitHub ('GitHub' is already an item there)",
                "  not imported: Bank: field Password ('Password' is one of the item's standard fields, not a custom field)"),
            _cli.Err,
            StringComparison.Ordinal);
        Assert.Empty(_cli.Out);
        AssertNoValue();

        using var vault = Vault.Open(_cli.VaultPath, _master);
        Assert.Equal("vault-pw-github", vault.Find(new EntryName("imported/Work", "GitHub"))!.Password);
        Assert.Null(vault.Find(new EntryName("imported/Work", "GitHub (2)")));
        Assert.Equal("export-pw-bank", vault.Find(new EntryName("imported", "Bank"))!.Password);
        Assert.Equal("export-pin-bank", vault.ReadField(new EntryName("imported", "Bank"), "PIN"));
        Assert.Equal("otpauth://totp/bank?secret=JBSWY3DPEHPK3PXP", vault.ReadField(new EntryName("imported", "Bank"), "otp"));
    }

    [Fact]
    public void An_export_is_copied_into_a_vault_and_skip_existing_is_for_an_export_only()
    {
        var kdbx = Path.Combine(_cli.Directory, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(kdbx, Encoding.UTF8.GetBytes("foreign-password"), null, "Argon2id", "ChaCha20");

        Assert.Equal(CliApp.ExitUsageError, _cli.Run("import", _export, "--vault", _cli.VaultPath, "--in-place"));
        Assert.Contains("--in-place and --source-keyfile are for a KeePass file", _cli.Err, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitUsageError, _cli.Run("import", _export));
        Assert.Contains("an export is copied into a vault: pass --vault or set KEYPASTE_VAULT", _cli.Err, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitUsageError, _cli.Run("import", kdbx, "--vault", _cli.VaultPath, "--skip-existing"));
        Assert.Contains("--skip-existing is for another password manager's export", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_cli.Prompt.PromptsSeen);
    }

    [Fact]
    public void A_LastPass_export_is_copied_in_through_the_readers_keypaste_ships()
    {
        _cli.ExportReaders = ExportImport.Readers;
        var export = Path.Combine(_cli.Directory, "lastpass_export.csv");
        File.WriteAllText(export, "url,username,password,totp,extra,name,grouping,fav\r\nhttps://example.com,ada,lp-pw-example,,lp-notes-example,Example,Work,1\r\n");

        _cli.Prompt.Enqueue(_master);
        var exit = _cli.Run("import", export, "--vault", _cli.VaultPath, "--into", "imported");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("  lastpass_export.csv · LastPass CSV · 1 item", _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("lp-pw-example", _cli.Out + _cli.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(_cli.VaultPath, _master);
        var name = new EntryName("imported/Work", "Example");
        var item = vault.Find(name)!;
        Assert.Equal(("ada", "lp-pw-example", "https://example.com", "lp-notes-example"), (item.Username, item.Password, item.Url, item.Notes));
        Assert.Equal(["Favorite"], vault.Tags(name));
    }

    [Fact]
    public void A_file_no_reader_recognizes_is_refused_as_it_was_before()
    {
        var notes = Path.Combine(_cli.Directory, "notes.txt");
        File.WriteAllText(notes, "not a vault and not an export");
        Assert.False(KdbxImport.TryProbe(notes, out _, out var notKdbx));

        var exit = _cli.Run("import", notes, "--vault", _cli.VaultPath);

        _cli.AssertExit(CliApp.ExitInternalError, exit);
        Assert.Equal($"keypaste import: {notKdbx}{Environment.NewLine}", _cli.Err);
        Assert.Empty(_cli.Prompt.PromptsSeen);
    }

    [Fact]
    public void A_bitwarden_export_is_copied_by_the_reader_keypaste_ships_with_its_passkey_naming_what_it_leaves_out()
    {
        _cli.ExportReaders = ExportImport.Readers;
        var export = Path.Combine(_cli.Directory, "bitwarden_export.json");
        File.WriteAllText(export, """
            {
              "encrypted": false,
              "folders": [ { "id": "work", "name": "Work" } ],
              "items": [
                {
                  "type": 1, "name": "Mail", "folderId": "work",
                  "login": {
                    "username": "export-user-mail", "password": "export-pw-mail", "totp": "JBSWY3DPEHPK3PXP",
                    "fido2Credentials": [ { "credentialId": "b64.AQIDBA", "keyValue": "BAMCAQ", "rpId": "mail.example", "userHandle": "BQYHCA", "userName": "export-user-mail" } ]
                  }
                }
              ]
            }
            """);

        _cli.Prompt.Enqueue(_master);
        var exit = _cli.Run("import", export, "--vault", _cli.VaultPath, "--into", "bitwarden");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("  bitwarden_export.json · Bitwarden JSON · 1 item", _cli.Err, StringComparison.Ordinal);
        Assert.Contains(
            Lines(
                "  ✓ 1 item · 0 projects · copied into bitwarden",
                "  not imported: attachments (a Bitwarden .json export holds none; save each from Bitwarden and add it to its item)"),
            _cli.Err,
            StringComparison.Ordinal);
        Assert.DoesNotContain("passkey", _cli.Err, StringComparison.Ordinal);
        AssertNoValue();

        using var vault = Vault.Open(_cli.VaultPath, _master);
        var mail = new EntryName("bitwarden/Work", "Mail");
        Assert.Equal(("export-user-mail", "export-pw-mail"), (vault.Find(mail)!.Username, vault.Find(mail)!.Password));
        Assert.Equal("otpauth://totp/Mail:export-user-mail?secret=JBSWY3DPEHPK3PXP", vault.ReadField(mail, "otp"));
        Assert.Equal(("AQIDBA", "mail.example"), (vault.ReadField(mail, "KPEX_PASSKEY_CREDENTIAL_ID"), vault.ReadField(mail, "KPEX_PASSKEY_RELYING_PARTY")));
    }

    [Fact]
    public void An_encrypted_bitwarden_export_is_refused_before_any_password_is_asked()
    {
        _cli.ExportReaders = ExportImport.Readers;
        var export = Path.Combine(_cli.Directory, "bitwarden_encrypted_export.json");
        File.WriteAllText(export, """{ "encrypted": true, "passwordProtected": true, "salt": "c2FsdA==", "kdfType": 0, "kdfIterations": 600000, "data": "2.a|b|c" }""");

        var exit = _cli.Run("import", export, "--vault", _cli.VaultPath);

        _cli.AssertExit(CliApp.ExitInternalError, exit);
        Assert.Equal(
            "keypaste import: bitwarden_encrypted_export.json is not a Bitwarden JSON export keypaste reads: it is password-protected. "
                + "Export your vault from Bitwarden again with the .json file format, which is not encrypted" + Environment.NewLine,
            _cli.Err);
        Assert.Empty(_cli.Prompt.PromptsSeen);
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + Environment.NewLine));

    private void AssertNoValue() =>
        Assert.All(_values, value => Assert.DoesNotContain(value, _cli.Out + _cli.Err, StringComparison.Ordinal));

    /// <summary>A reader of a format nobody exports, recognized by its first bytes, holding one item the vault has and one with a field keypaste does not write.</summary>
    private sealed class ExportReader : IExportReader
    {
        internal static readonly byte[] Magic = "FAKE-EXPORT\n"u8.ToArray();

        public string Format => "Fake";

        public int MaximumBytes => 1024;

        public bool Recognizes(string fileName, ReadOnlySpan<byte> start) => start.StartsWith(Magic);

        public ExportContents Read(ReadOnlySpan<byte> file) =>
            new(
                [
                    new ExportItem { Title = "GitHub", Folder = ["Work"], Password = "export-pw-github" },
                    new ExportItem
                    {
                        Title = "Bank",
                        Username = "export-user-bank",
                        Password = "export-pw-bank",
                        Notes = "export-notes-bank",
                        Fields = [new ExportField("PIN", "export-pin-bank"), new ExportField("Password", "export-standard-field")],
                        Totp = "otpauth://totp/bank?secret=JBSWY3DPEHPK3PXP",
                        History = [new ExportVersion(DateTimeOffset.UnixEpoch, "export-user-bank", "export-old-pw-bank", string.Empty, string.Empty)],
                    },
                ],
                []);
    }
}
