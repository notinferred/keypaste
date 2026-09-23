using System.Security.Cryptography;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Choosing a keyfile on the unlock screen: to open a vault that needs one, to create one that will,
/// and the keyfile the recent list remembers for next time (V.1b).
/// </summary>
/// <remarks>
/// The keyfile vaults here are made by <see cref="VaultCreation"/> or by the library's own writer,
/// never by a view model, and every create is checked by opening the file again outside the app.
/// </remarks>
public sealed class UnlockKeyfileTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";

    private readonly TempHome _home = new();
    private readonly AppVaultSession _session = new(new ManualClock());
    private readonly FakeVaultFilePicker _picker = new();
    private readonly string _keyfile;
    private int _unlockedCalls;

    public UnlockKeyfileTests()
    {
        _keyfile = Path.Combine(_home.Path, "vault.key");
        File.WriteAllBytes(_keyfile, RandomNumberGenerator.GetBytes(32));
    }

    public void Dispose()
    {
        _session.Dispose();
        _home.Dispose();
    }

    [Fact]
    public async Task A_vault_needing_a_password_and_a_keyfile_opens_with_both_and_remembers_the_keyfile()
    {
        var path = MakeVault(_master, _keyfile);
        using var model = NewModel();
        Assert.True(model.Offer(path));
        Assert.Null(model.KeyfilePath);

        Type(model, _master);
        await model.UnlockAsync();
        Assert.Equal(0, _unlockedCalls);
        Assert.Equal("That password didn't open this vault.", model.Message);

        _picker.KeyfilePath = _keyfile;
        await model.ChooseKeyfileAsync();
        Assert.Equal("vault.key", model.KeyfileName);

        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(1, _unlockedCalls);
        Assert.Equal(_keyfile, _session.Unlocked!.KeyfilePath);
        Assert.Equal(_keyfile, Assert.Single(Remembered()).KeyfilePath);

        _session.Lock(VaultLockReason.Manual);
        using var next = NewModel();
        Assert.Equal(path, next.SelectedPath);
        Assert.Equal(_keyfile, next.KeyfilePath);
    }

    [Fact]
    public async Task A_vault_a_keyfile_alone_opens_needs_no_typed_password()
    {
        var path = MakeVault(string.Empty, _keyfile);
        using var model = NewModel();
        model.Offer(path);

        Assert.False(model.UnlockCommand.CanExecute(null));

        _picker.KeyfilePath = _keyfile;
        await model.ChooseKeyfileAsync();
        Assert.True(model.UnlockCommand.CanExecute(null));

        await model.UnlockAsync();

        Assert.Equal(1, _unlockedCalls);
        Assert.False(_session.Unlocked!.HasPassword);
    }

    [Fact]
    public async Task A_wrong_keyfile_names_both_factors()
    {
        var path = MakeVault(_master, _keyfile);
        var other = Path.Combine(_home.Path, "other.key");
        File.WriteAllBytes(other, RandomNumberGenerator.GetBytes(32));

        using var model = NewModel();
        model.Offer(path);
        _picker.KeyfilePath = other;
        await model.ChooseKeyfileAsync();
        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.Equal("That password and keyfile didn't open this vault.", model.Message);
    }

    [Fact]
    public async Task A_file_that_is_not_a_keyfile_is_refused_when_it_is_chosen()
    {
        var path = MakeVault(_master, _keyfile);
        var empty = Path.Combine(_home.Path, "empty.key");
        File.WriteAllBytes(empty, []);

        using var model = NewModel();
        model.Offer(path);

        _picker.KeyfilePath = Path.Combine(_home.Path, "absent.key");
        await model.ChooseKeyfileAsync();
        Assert.Equal("That keyfile isn't there any more.", model.Message);
        Assert.Null(model.KeyfilePath);

        _picker.KeyfilePath = empty;
        await model.ChooseKeyfileAsync();
        Assert.Equal("That file is empty, so it can't be a keyfile.", model.Message);

        _picker.KeyfilePath = path;
        await model.ChooseKeyfileAsync();
        Assert.Equal("That's a KeePass vault, not a keyfile.", model.Message);

        Assert.Equal(3, _picker.KeyfileCalls);
        Assert.Null(model.KeyfilePath);
    }

    [Fact]
    public async Task A_remembered_keyfile_that_has_gone_is_said_before_any_unlock()
    {
        var path = MakeVault(_master, _keyfile);
        RecentVaults.Save(KeypasteHome.RecentPath(_home.Path), [new RecentVault(path, DateTimeOffset.UtcNow, _keyfile)]);
        File.Delete(_keyfile);

        using var model = NewModel();
        Assert.Equal(_keyfile, model.KeyfilePath);

        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.False(_session.IsUnlocked);
        Assert.Equal("That keyfile isn't there any more.", model.Message);
    }

    [Fact]
    public async Task A_vault_keyed_to_an_ordinary_file_opens_with_a_warning_the_shell_shows()
    {
        var document = Path.Combine(_home.Path, "notes.txt");
        File.WriteAllText(document, "an ordinary document someone might edit");
        var path = MakeVault(_master, document);

        using var model = NewModel();
        model.Offer(path);
        _picker.KeyfilePath = document;
        await model.ChooseKeyfileAsync();
        Assert.Contains("keyed by its exact bytes", model.Message, StringComparison.Ordinal);

        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(1, _unlockedCalls);
        Assert.Contains("notes.txt", model.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vault_created_with_a_keyfile_reopens_only_with_both()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;
        await model.StartCreateAsync();

        _picker.KeyfilePath = _keyfile;
        await model.ChooseKeyfileAsync();
        TypeNew(model, _master);
        await model.CreateAsync();

        Assert.Equal(1, _unlockedCalls);
        Assert.Equal(_keyfile, Assert.Single(Remembered()).KeyfilePath);
        _session.Lock(VaultLockReason.Manual);

        using (var reopened = Vault.Open(_home.FreeVaultPath, _master, _keyfile))
        {
            Assert.Empty(reopened.ReadEntries());
        }

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(_home.FreeVaultPath, _master));
    }

    [Fact]
    public async Task A_create_with_a_keyfile_keyed_by_its_hash_writes_nothing()
    {
        var document = Path.Combine(_home.Path, "notes.txt");
        File.WriteAllText(document, "an ordinary document someone might edit");

        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;
        await model.StartCreateAsync();

        _picker.KeyfilePath = document;
        await model.ChooseKeyfileAsync();
        Assert.Equal(document, model.KeyfilePath);
        var before = _home.Snapshot();

        TypeNew(model, _master);
        await model.CreateAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.Contains("won't make a vault that needs this file", model.Message, StringComparison.Ordinal);
        Assert.Equal(before, _home.Snapshot());
        Assert.Equal(0, model.NewMaskedLength);
    }

    [Fact]
    public async Task Starting_a_create_drops_the_keyfile_of_the_selected_vault_and_cancelling_brings_it_back()
    {
        var path = MakeVault(_master, _keyfile);
        RecentVaults.Save(KeypasteHome.RecentPath(_home.Path), [new RecentVault(path, DateTimeOffset.UtcNow, _keyfile)]);

        using var model = NewModel();
        Assert.Equal(_keyfile, model.KeyfilePath);

        _picker.NewPath = Path.Combine(_home.Path, "second.kdbx");
        await model.StartCreateAsync();
        Assert.Null(model.KeyfilePath);

        await model.CancelCreateAsync();
        Assert.Equal(_keyfile, model.KeyfilePath);
    }

    private string MakeVault(string password, string keyfile)
    {
        var path = Path.Combine(_home.Path, $"{Guid.NewGuid():n}.kdbx");

        using var vault = Vault.CreateWith(path, password, keyfile);
        vault.Save();

        return path;
    }

    private UnlockViewModel NewModel() =>
        new(_session, _home.Path, _picker, () => _unlockedCalls++);

    private IReadOnlyList<RecentVault> Remembered() =>
        RecentVaults.Load(KeypasteHome.RecentPath(_home.Path));

    private static void Type(UnlockViewModel model, string password)
    {
        model.ClearPassword();

        foreach (var c in password)
        {
            model.Type(c);
        }
    }

    private static void TypeNew(UnlockViewModel model, string password)
    {
        foreach (var c in password)
        {
            model.TypeNew(c);
            model.TypeConfirm(c);
        }
    }
}
