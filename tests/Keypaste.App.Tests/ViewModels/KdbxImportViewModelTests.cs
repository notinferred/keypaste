using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
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

        Assert.Equal(["Imported 3 entries into foreign"], _announced);
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

        _session.Lock(VaultLockReason.Idle);

        Assert.False(import.IsDecrypted);
        Assert.Empty(import.Rows);
        Assert.False(import.CanConfirm);
        Assert.Equal(0, import.EntryCount);
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
