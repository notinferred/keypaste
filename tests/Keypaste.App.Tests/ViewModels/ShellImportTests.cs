using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>The sidebar's "Import .kdbx": the picker, the dialog over the shell, and every way it closes.</summary>
public sealed class ShellImportTests : IDisposable
{
    private const string _sourcePassword = "import-source";

    private readonly TempVault _vault = new();
    private readonly AppVaultSession _session;
    private readonly FakeVaultFilePicker _picker = new();
    private readonly List<(string Path, string? Keyfile)> _opened = [];
    private readonly string _source;
    private readonly string _keyfile;

    public ShellImportTests()
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
    public void The_sidebar_offers_import()
    {
        using var shell = NewShell();

        Assert.True(shell.ImportAvailable);
        Assert.True(shell.ImportCommand.CanExecute(null));
        Assert.False(shell.HasImport);
    }

    [Fact]
    public async Task A_cancelled_picker_opens_nothing()
    {
        using var shell = NewShell();

        await shell.ImportCommand.ExecuteAsync();

        Assert.Equal(1, _picker.ExistingCalls);
        Assert.Null(shell.Import);
    }

    [Fact]
    public async Task The_picked_file_opens_in_the_dialog_and_cancel_closes_it()
    {
        using var shell = NewShell();
        _picker.ExistingPath = _source;

        await shell.ImportCommand.ExecuteAsync();

        var import = Assert.IsType<KdbxImportViewModel>(shell.Import);
        Assert.True(shell.HasImport);
        Assert.Equal("foreign.kdbx", import.FileName);
        Assert.True(import.NeedsUnlock);
        Assert.False(shell.ImportCommand.CanExecute(null));

        import.CancelCommand.Execute(null);

        Assert.Null(shell.Import);
        Assert.False(shell.HasImport);
        Assert.True(shell.ImportCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_import_closes_the_dialog_saves_and_says_so()
    {
        using var shell = NewShell();
        shell.OpenImport(_source);
        var import = shell.Import!;
        await Unlock(import);

        Assert.True(import.ShowsRows);
        Assert.Equal("KDBX 4.0 · Argon2id · 3 entries · key file + password", import.CardDetail);

        import.ConfirmCommand.Execute(null);

        Assert.Null(shell.Import);
        Assert.Equal("Imported 3 entries into foreign", shell.Toast);
        Assert.Equal(VaultSaveStatus.Saved, _session.Unlocked!.SaveState().Status);
        Assert.NotNull(_session.Unlocked!.Find(new EntryName("foreign/Banking", "Checking")));
    }

    [Fact]
    public void Keeping_the_file_in_place_hands_it_to_the_unlock_screen_and_closes()
    {
        using var shell = NewShell();
        shell.OpenImport(_source);
        var import = shell.Import!;

        import.KeepEditingInPlace = true;
        Assert.False(import.NeedsUnlock);
        Assert.Contains("opens as the vault", import.InPlaceNote, StringComparison.Ordinal);

        import.ConfirmCommand.Execute(null);

        Assert.Equal([(Path.GetFullPath(_source), (string?)_keyfile)], _opened);
        Assert.Null(shell.Import);
    }

    [Fact]
    public async Task Locking_drops_the_dialog_and_its_source()
    {
        using var shell = NewShell();
        shell.OpenImport(_source);
        var import = shell.Import!;
        await Unlock(import);

        _session.Lock(VaultLockReason.Idle);

        Assert.Null(shell.Import);
        Assert.False(import.IsDecrypted);
    }

    [Fact]
    public async Task Disposing_the_shell_drops_the_dialog()
    {
        var shell = NewShell();
        shell.OpenImport(_source);
        var import = shell.Import!;
        await Unlock(import);

        shell.Dispose();

        Assert.Null(shell.Import);
        Assert.False(import.IsDecrypted);
        Assert.Equal(0, import.PasswordLength);
    }

    [Fact]
    public async Task A_keyfile_is_chosen_through_the_picker_and_a_vault_is_refused_as_one()
    {
        using var shell = NewShell();
        shell.OpenImport(_source);
        var import = shell.Import!;

        import.ClearKeyfileCommand.Execute(null);
        Assert.False(import.HasKeyfile);

        _picker.KeyfilePath = _vault.Path_;
        await import.ChooseKeyfileCommand.ExecuteAsync();
        Assert.Equal(1, _picker.KeyfileCalls);
        Assert.False(import.HasKeyfile);
        Assert.Equal("That's a KeePass vault, not a keyfile.", import.Message);

        _picker.KeyfilePath = _keyfile;
        await import.ChooseKeyfileCommand.ExecuteAsync();
        Assert.Equal(_keyfile, import.KeyfilePath);
        Assert.Equal("foreign.keyx", import.KeyfileName);
        Assert.False(import.HasMessage);
    }

    [Fact]
    public async Task The_password_arrives_through_the_field_sink_and_paste_adds_nothing()
    {
        using var shell = NewShell();
        shell.OpenImport(_source);
        ISecretSink sink = shell.Import!;

        sink.Type('a');
        sink.Type('b');
        await sink.Paste();
        Assert.Equal(2, shell.Import!.PasswordLength);

        sink.Backspace();
        Assert.Equal(1, shell.Import.PasswordLength);

        sink.Clear();
        Assert.Equal(0, shell.Import.PasswordLength);
    }

    private ShellViewModel NewShell() =>
        new(_session, _vault.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(), picker: _picker,
            openInPlace: (path, keyfile) => _opened.Add((path, keyfile)));

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
