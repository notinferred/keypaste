using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Import;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>The KDBX import dialog, driven through its view model against real files.</summary>
public sealed class KdbxImportViewModelTests : IDisposable
{
    private const string _sourcePassword = "import-source";

    private readonly TempVault _vault = new();
    private readonly AppVaultSession _session;
    private readonly string _source;
    private readonly string _keyfile;
    private readonly List<string> _announced = [];
    private readonly List<(string Path, string? Keyfile)> _opened = [];

    public KdbxImportViewModelTests()
    {
        _keyfile = Path.Combine(_vault.Home, "foreign.keyx");
        File.WriteAllBytes(_keyfile, RandomNumberGenerator.GetBytes(64));
        _source = Path.Combine(_vault.Home, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(_source, Encoding.UTF8.GetBytes(_sourcePassword), _keyfile, "Argon2id", "ChaCha20");

        _session = new AppVaultSession(new ManualClock(), home: _vault.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public async Task CardText_AfterUnlock()
    {
        using var import = NewImport();
        Assert.Equal("foreign.kdbx · KDBX 4.0 · Argon2id · Locked", import.CardText);
        Assert.Equal(_keyfile, import.KeyfilePath);

        await Unlock(import);

        Assert.True(import.IsDecrypted);
        Assert.Equal("key file + password", import.KeyFactors);
        Assert.Equal("foreign.kdbx · KDBX 4.0 · Argon2id · 3 entries · key file + password · Decrypted", import.CardText);
        Assert.Equal(0, import.PasswordLength);
    }

    [Fact]
    public async Task ConfirmText_CountsEntries()
    {
        using var import = NewImport();
        await Unlock(import);

        Assert.Equal("Import 3 entries", import.ConfirmText);

        import.Rows.Single(row => row.SourceGroup == "Banking").Include = false;
        Assert.Equal("Import 1 entry", import.ConfirmText);

        import.KeepEditingInPlace = true;
        Assert.Equal("Open foreign.kdbx", import.ConfirmText);
    }

    [Fact]
    public async Task ABlockedRow_DisablesConfirm()
    {
        using var import = NewImport();
        await Unlock(import);
        Assert.True(import.CanConfirm);

        var banking = import.Rows.Single(row => row.SourceGroup == "Banking");
        banking.Destination = ".keypaste/tokens";

        Assert.False(import.CanConfirm);
        Assert.False(import.ConfirmCommand.CanExecute(null));
        Assert.True(banking.Blocks);
        Assert.Contains("keypaste's own group", banking.Problem, StringComparison.Ordinal);

        banking.Include = false;
        Assert.True(import.CanConfirm);
    }

    [Fact]
    public async Task Import_SavesThroughTheSession()
    {
        using var import = NewImport();
        await Unlock(import);

        import.ConfirmCommand.Execute(null);

        Assert.Equal(["Imported 3 entries from foreign.kdbx"], _announced);
        Assert.False(import.IsDecrypted);
        Assert.Empty(_opened);

        _session.Lock(VaultLockReason.Manual);
        using var reopened = Vault.Open(_vault.Path_, TempVault.Password);
        Assert.Equal("v2", reopened.Find(new EntryName("foreign/Banking", "Checking"))!.Password);
        Assert.NotNull(reopened.Find(new EntryName("foreign/Banking/Cards", "Visa")));
        Assert.NotNull(reopened.Find(new EntryName(string.Empty, "example")));
    }

    [Fact]
    public void KeepEditingInPlace_OpensTheFile()
    {
        var before = File.ReadAllBytes(_vault.Path_);
        using var import = NewImport();

        import.KeepEditingInPlace = true;
        Assert.True(import.CanConfirm);
        import.ConfirmCommand.Execute(null);

        Assert.Equal([(Path.GetFullPath(_source), (string?)_keyfile)], _opened);
        Assert.Empty(_announced);
        Assert.Equal(before, File.ReadAllBytes(_vault.Path_));
    }

    [Fact]
    public async Task Lock_DisposesTheSource()
    {
        using var import = NewImport();
        await Unlock(import);
        Assert.NotEmpty(import.Rows);
        var source = import.Source!;

        _session.Lock(VaultLockReason.Idle);

        Assert.False(import.IsDecrypted);
        Assert.Empty(import.Rows);
        Assert.False(import.CanConfirm);
        Assert.Equal(0, import.EntryCount);
        AssertDisposed(source);
    }

    [Fact]
    public async Task Cancel_DisposesTheSource()
    {
        using var import = NewImport();
        await Unlock(import);
        var source = import.Source!;

        import.CancelCommand.Execute(null);

        Assert.False(import.IsDecrypted);
        AssertDisposed(source);
    }

    [Fact]
    public async Task Confirm_DisposesTheSource()
    {
        using var import = NewImport();
        await Unlock(import);
        var source = import.Source!;

        import.ConfirmCommand.Execute(null);

        Assert.Single(_announced);
        AssertDisposed(source);
    }

    private static void AssertDisposed(ImportSource source) =>
        Assert.Throws<ObjectDisposedException>(() => source.Interop);

    [Fact]
    public async Task A_wrong_password_is_an_error_under_the_field()
    {
        using var import = NewImport();
        import.TypePassword('x');

        await import.UnlockAsync();

        Assert.Equal("That password and key file do not open foreign.kdbx.", import.Message);
        Assert.True(import.NeedsUnlock);
        Assert.False(import.HasTrailingMessage);
    }

    [Fact]
    public async Task An_unreadable_file_is_named_and_offers_only_another_file()
    {
        var notes = Path.Combine(_vault.Home, "notes.kdbx");
        await File.WriteAllTextAsync(notes, "not a vault", TestContext.Current.CancellationToken);
        var asked = 0;

        using var import = new KdbxImportViewModel(
            _session, notes, (_, _) => { }, _announced.Add, chooseAnother: () => { asked++; return Task.CompletedTask; });

        Assert.True(import.IsUnreadable);
        Assert.Equal("notes.kdbx", import.FileName);
        Assert.Equal("This file is not a KDBX vault.", import.Message);
        Assert.False(import.ShowsConfirm);
        Assert.False(import.NeedsUnlock);
        Assert.True(import.CanChooseAnother);

        await import.ChooseAnotherCommand.ExecuteAsync();
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task A_blocked_row_says_how_to_unblock_it()
    {
        using var import = NewImport();
        await Unlock(import);
        var row = import.Rows.First(row => row.SourceGroup == "Banking");

        row.Destination = ".keypaste/tokens";

        Assert.True(row.Blocks);
        Assert.EndsWith("Type another destination or untick Banking.", row.Problem, StringComparison.Ordinal);
    }

    private KdbxImportViewModel NewImport() =>
        new(_session, _source, (path, keyfile) => _opened.Add((path, keyfile)), _announced.Add);

    private static async Task Unlock(KdbxImportViewModel import)
    {
        foreach (var c in _sourcePassword)
        {
            import.TypePassword(c);
        }

        await import.UnlockAsync();
        Assert.True(import.IsDecrypted, import.Message);
    }
}
