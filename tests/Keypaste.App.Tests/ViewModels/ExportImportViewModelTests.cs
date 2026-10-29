using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The export import dialog, driven through its view model against a Bitwarden export written by hand: the preview names
/// where each item lands and everything that does not, a name the vault has is kept beside it or skipped, the import
/// copies everything or nothing, the report says what it did and to delete the file, which it leaves, and an encrypted
/// export is refused in Bitwarden's words.
/// </summary>
public sealed class ExportImportViewModelTests : IDisposable
{
    // Every value in the export carries this mark, so nothing shown may hold it.
    private const string _valueMark = "bw-";
    private const string _secret = "GEZDGNBVGY3TQOJQ";

    private const string _export = """
        {
          "encrypted": false,
          "folders": [ { "id": "folder-work", "name": "Work" } ],
          "items": [
            {
              "id": "item-github", "folderId": "folder-work", "type": 1, "name": "GitHub",
              "fields": [ { "name": "Password", "value": "bw-field-standard", "type": 0, "linkedId": null } ],
              "login": {
                "username": "bw-user-github", "password": "bw-pw-github", "totp": "GEZDGNBVGY3TQOJQ",
                "uris": [ { "match": null, "uri": "https://github.example/bw-login" } ]
              }
            },
            { "id": "item-bank", "type": 1, "name": "Bank", "login": { "password": "bw-pw-bank" } },
            { "id": "item-bank-two", "type": 1, "name": "Bank", "login": { "password": "bw-pw-bank-two" } },
            { "id": "item-old", "type": 1, "name": "Old", "deletedDate": "2025-01-01T00:00:00.000Z", "login": { "password": "bw-pw-old" } }
          ]
        }
        """;

    private static readonly EntryName _held = new("imported/Work", "GitHub");

    private readonly TempVault _vault = new();
    private readonly AppVaultSession _session;
    private readonly string _path;
    private readonly List<string> _announced = [];

    public ExportImportViewModelTests()
    {
        using (var seeded = Vault.Open(_vault.Path_, TempVault.Password))
        {
            seeded.AddEntry(new VaultEntry { GroupPath = _held.GroupPath, Title = _held.Title, Password = "vault-pw-github" });
            seeded.Save();
        }

        _path = Path.Combine(_vault.Home, "bitwarden_export.json");
        File.WriteAllText(_path, _export);

        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _vault.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public async Task The_preview_says_where_each_item_lands_and_names_everything_that_does_not_and_writes_nothing()
    {
        var vaultBefore = File.ReadAllBytes(_vault.Path_);
        using var import = NewImport();
        Assert.True(import.IsReading);
        Assert.Equal("Reading…", import.CardDetail);

        await import.ReadAsync();

        Assert.True(import.IsPreviewing, import.Message);
        Assert.Equal("bitwarden_export.json", import.FileName);
        Assert.Equal("Bitwarden JSON · 3 items", import.CardDetail);
        Assert.Equal("bitwarden_export", import.Into);
        Assert.Equal(
            [("Work/GitHub", "bitwarden_export/Work/GitHub"), ("Bank", "bitwarden_export/Bank"), ("Bank", "bitwarden_export/Bank (2)")],
            import.Rows.Select(row => (row.Item, row.Lands)));
        Assert.Equal("'Bank' is another item of this export", import.Rows[2].Note);
        Assert.False(import.HasHeld);
        Assert.Equal("Import 3 items", import.ConfirmText);
        Assert.True(import.CanConfirm);

        Assert.Contains(import.NotImported, loss => loss is { Item: "Work/GitHub" } && loss.What.StartsWith("field Password: ", StringComparison.Ordinal));
        Assert.Contains(import.NotImported, loss => loss is { HasItem: false } && loss.What.StartsWith("item Old: ", StringComparison.Ordinal));
        Assert.Contains(import.NotImported, loss => loss is { HasItem: false } && loss.What.StartsWith("attachments: ", StringComparison.Ordinal));
        Assert.Equal($"Not imported ({import.NotImported.Count})", import.NotImportedLabel);
        Assert.True(import.HasNotImported);

        Assert.Equal(vaultBefore, File.ReadAllBytes(_vault.Path_));
        Assert.Equal(_export, File.ReadAllText(_path));
        AssertNoValue(import);
    }

    [Fact]
    public async Task A_name_the_vault_has_is_kept_beside_it_or_skipped_and_one_the_export_holds_twice_is_always_kept()
    {
        using var import = NewImport();
        await import.ReadAsync();

        import.Into = "imported";

        Assert.Equal("imported/Work/GitHub (2)", import.Rows[0].Lands);
        Assert.Equal("'GitHub' is already an item there", import.Rows[0].Note);
        Assert.True(import.HasHeld);
        Assert.Equal("1 name your vault already has", import.HeldLabel);
        Assert.True(import.KeepHeld);
        Assert.Equal("Import 3 items", import.ConfirmText);

        import.SkipHeld = true;

        Assert.False(import.KeepHeld);
        Assert.True(import.Rows[0].Skipped);
        Assert.Equal("skipped", import.Rows[0].Lands);
        Assert.Equal("imported/Bank (2)", import.Rows[2].Lands);
        Assert.Equal("Import 2 items", import.ConfirmText);
        Assert.Contains(new ExportLossRow("Work/GitHub", "skipped: 'GitHub' is already an item there"), import.NotImported);

        import.KeepHeld = true;

        Assert.Equal("imported/Work/GitHub (2)", import.Rows[0].Lands);
        Assert.DoesNotContain(import.NotImported, loss => loss.What.StartsWith("skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_import_copies_everything_saves_reports_what_it_did_and_leaves_the_file_to_be_deleted()
    {
        using var import = NewImport();
        await import.ReadAsync();
        import.Into = "imported";

        import.ConfirmCommand.Execute(null);

        Assert.True(import.IsImported, import.Message);
        Assert.False(import.IsPreviewing);
        Assert.Null(import.Source);
        Assert.Equal("Imported 3 items into imported", import.ImportedText);
        Assert.True(import.HasNotImported);
        Assert.Equal(Path.GetFullPath(_path), import.FilePath);
        Assert.Equal(_export, File.ReadAllText(_path));
        Assert.Empty(_announced);
        Assert.Equal(VaultSaveStatus.Saved, _session.Unlocked!.SaveState().Status);
        AssertNoValue(import);

        import.CancelCommand.Execute(null);

        Assert.Equal(["Imported 3 items from bitwarden_export.json"], _announced);
        Assert.Empty(import.NotImported);
        Assert.True(File.Exists(_path));

        _session.Lock(VaultLockReason.Manual);
        using var reopened = Vault.Open(_vault.Path_, TempVault.Password);
        var kept = new EntryName("imported/Work", "GitHub (2)");
        Assert.Equal("bw-pw-github", reopened.Find(kept)!.Password);
        Assert.Equal("bw-user-github", reopened.Find(kept)!.Username);
        Assert.Contains(reopened.Fields(kept)!, field => field is { Name: "otp", IsProtected: true });
        Assert.Equal("vault-pw-github", reopened.Find(_held)!.Password);
        Assert.Equal("bw-pw-bank", reopened.Find(new EntryName("imported", "Bank"))!.Password);
        Assert.Equal("bw-pw-bank-two", reopened.Find(new EntryName("imported", "Bank (2)"))!.Password);
    }

    /// <summary>The save is the commit's own, so a save another program's write refuses takes every item back out of the open vault.</summary>
    [Fact]
    public async Task A_save_that_fails_takes_everything_back_and_says_nothing_was_imported()
    {
        using var import = NewImport();
        await import.ReadAsync();

        using (var other = Vault.Open(_vault.Path_, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { GroupPath = "elsewhere", Title = "x", Password = "x" });
            other.Save();
        }

        import.ConfirmCommand.Execute(null);

        Assert.True(import.IsPreviewing);
        Assert.Contains("Reload to see it", import.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was imported.", import.Message, StringComparison.Ordinal);
        Assert.True(import.HasTrailingMessage);
        Assert.DoesNotContain(_session.Unlocked!.Search(string.Empty), match => match.Name.GroupPath.StartsWith("bitwarden_export", StringComparison.Ordinal));
        Assert.Empty(_announced);
    }

    [Theory]
    [InlineData(
        """{ "encrypted": true, "passwordProtected": true, "salt": "x", "kdfType": 0, "kdfIterations": 600000, "encKeyValidation_DO_NOT_EDIT": "x", "data": "x" }""",
        "This file is not a Bitwarden JSON export keypaste reads: it is password-protected. Export your vault from Bitwarden again with the .json file format, which is not encrypted.")]
    [InlineData(
        """{ "encrypted": true, "encKeyValidation_DO_NOT_EDIT": "x", "folders": [], "items": [] }""",
        "This file is not a Bitwarden JSON export keypaste reads: it is encrypted with your Bitwarden account's key, which only Bitwarden reads. Export your vault again with the .json file format, which is not encrypted.")]
    public async Task An_encrypted_export_is_refused_in_bitwardens_words_and_offers_only_another_file(string export, string refusal)
    {
        await File.WriteAllTextAsync(_path, export, TestContext.Current.CancellationToken);
        var asked = 0;
        using var import = new ExportImportViewModel(_session, _path, _announced.Add, chooseAnother: () => { asked++; return Task.CompletedTask; });

        await import.ReadAsync();

        Assert.True(import.IsUnreadable);
        Assert.Equal(refusal, import.Message);
        Assert.False(import.HasTrailingMessage);
        Assert.Equal(string.Empty, import.CardDetail);
        Assert.False(import.ConfirmCommand.CanExecute(null));
        Assert.True(import.CanChooseAnother);

        await import.ChooseAnotherCommand.ExecuteAsync();
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task A_group_keypaste_refuses_stops_the_import_and_says_why()
    {
        using var import = NewImport();
        await import.ReadAsync();

        import.Into = ".keypaste/x";
        Assert.StartsWith("Nothing can be imported: ", import.Message, StringComparison.Ordinal);
        Assert.False(import.ConfirmCommand.CanExecute(null));

        import.Into = string.Empty;
        Assert.Equal("Nothing can be imported: name a group to import into.", import.Message);
        Assert.False(import.CanConfirm);

        import.Into = "imported";
        Assert.False(import.HasMessage);
        Assert.True(import.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Locking_drops_the_export_and_closes_saying_nothing()
    {
        using var import = NewImport();
        await import.ReadAsync();
        var source = import.Source!;
        var closed = false;
        import.Closed += (_, _) => closed = true;

        _session.Lock(VaultLockReason.Idle);

        Assert.True(closed);
        Assert.False(import.IsPreviewing);
        Assert.Null(import.Source);
        Assert.Empty(import.Rows);
        Assert.Empty(import.NotImported);
        Assert.False(import.CanConfirm);
        Assert.Throws<ObjectDisposedException>(() => source.Plan(null!, null));
        Assert.Empty(_announced);
    }

    [Fact]
    public async Task Cancel_drops_the_export_and_copies_nothing()
    {
        var vaultBefore = File.ReadAllBytes(_vault.Path_);
        using var import = NewImport();
        await import.ReadAsync();
        var source = import.Source!;

        import.CancelCommand.Execute(null);

        Assert.Throws<ObjectDisposedException>(() => source.Plan(null!, null));
        Assert.Empty(_announced);
        Assert.Equal(vaultBefore, File.ReadAllBytes(_vault.Path_));
    }

    private ExportImportViewModel NewImport() => new(_session, _path, _announced.Add);

    /// <summary>Nothing the dialog shows holds a value from the export.</summary>
    private static void AssertNoValue(ExportImportViewModel import)
    {
        IEnumerable<string> shown =
        [
            import.FileName, import.CardDetail, import.Into, import.Message, import.ConfirmText, import.ImportedText, import.NotImportedLabel,
            .. import.Rows.SelectMany(row => new[] { row.Item, row.Lands, row.Note }),
            .. import.NotImported.SelectMany(loss => new[] { loss.Item, loss.What }),
        ];

        foreach (var text in shown)
        {
            Assert.DoesNotContain(_valueMark, text, StringComparison.Ordinal);
            Assert.DoesNotContain(_secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
