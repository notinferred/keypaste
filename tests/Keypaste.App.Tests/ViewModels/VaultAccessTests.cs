using System.Security.Cryptography;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Changing a vault's master password and keyfile from Settings, then locking, reopening and
/// restoring through the unlock screen (V-V.1b).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every change is made by the form and every result is read from the file.</b> A reopen goes
/// through a separate <see cref="Vault.Open(string, ReadOnlySpan{char}, string?)"/> or through the unlock
/// screen, never through what a view model says it did.
/// </para>
/// <para>
/// Every refusal asserts the vault byte for byte and the backup list unchanged, after asserting that
/// the step it refused was reached.
/// </para>
/// </remarks>
public sealed class VaultAccessTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";
    private const string _later = "a-later-master-password";

    private readonly string _directory;
    private readonly string _home;
    private readonly string _vaultPath;
    private readonly string _keyA;
    private readonly string _keyB;
    private readonly ManualClock _clock = new();
    private readonly FakeVaultFilePicker _picker = new();

    public VaultAccessTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-access-app-tests-").FullName;
        _home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");
        _keyA = Path.Combine(_directory, "a.key");
        _keyB = Path.Combine(_directory, "b.key");
        File.WriteAllBytes(_keyA, RandomNumberGenerator.GetBytes(32));
        File.WriteAllBytes(_keyB, RandomNumberGenerator.GetBytes(32));

        using (var vault = Vault.Create(_vaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            vault.Save();
        }

        RecentVaults.Save(KeypasteHome.RecentPath(_home), [new RecentVault(_vaultPath, DateTimeOffset.UtcNow)]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------------ the journeys

    [Fact]
    public async Task A_password_changed_in_settings_keeps_the_app_open_and_only_the_new_one_opens_after_a_lock()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);
        var access = settings.Access;

        Enter(access, _master, _later);
        access.ReviewCommand.Execute(null);

        Assert.True(access.IsConfirming);
        Assert.Contains(VaultBackups.DirectoryFor(_vaultPath), access.BackupCost, StringComparison.Ordinal);
        Assert.Contains("still opens with the current password", access.BackupCost, StringComparison.Ordinal);

        await access.ChangeAsync();

        Assert.StartsWith("Changed.", access.Message, StringComparison.Ordinal);
        Assert.True(session.IsUnlocked);
        Assert.Equal(0, access.CurrentMaskedLength);
        Assert.Equal(0, access.NewMaskedLength);

        // The vault the session carries on with is not the sealed one: it still saves.
        session.Unlocked!.AddEntry(new VaultEntry { Title = "after", Password = "saved" });
        session.Unlocked.Save();
        session.Lock(VaultLockReason.Manual);

        AssertRefused(_vaultPath, _master, null);

        var landed = 0;
        using var unlock = new UnlockViewModel(session, _home, _picker, () => landed++);
        Assert.Equal(_vaultPath, unlock.SelectedPath);

        Type(unlock, _master);
        await unlock.UnlockAsync();
        Assert.Equal(0, landed);
        Assert.StartsWith("That password didn't open this vault.", unlock.Message, StringComparison.Ordinal);

        Type(unlock, _later);
        await unlock.UnlockAsync();
        Assert.Equal(1, landed);
        Assert.Equal("saved", session.Unlocked!.Find("after")?.Password);
    }

    [Fact]
    public async Task A_keyfile_added_replaced_and_removed_in_settings_is_what_the_next_unlock_needs()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);
        var access = settings.Access;

        // Add.
        _picker.KeyfilePath = _keyA;
        Enter(access, _master);
        await access.ChooseKeyfileCommand.ExecuteAsync();
        Assert.True(access.AttachKeyfile);
        Assert.Contains("a.key", access.KeyfileWarning, StringComparison.Ordinal);
        await Confirm(access);

        Assert.Equal(_keyA, session.Unlocked!.KeyfilePath);
        Assert.Equal(_keyA, Remembered().KeyfilePath);
        session.Lock(VaultLockReason.Manual);
        AssertRefused(_vaultPath, _master, null);

        // The unlock screen offers the remembered keyfile, and without it the password is refused.
        var landed = 0;
        using (var unlock = new UnlockViewModel(session, _home, _picker, () => landed++))
        {
            Assert.Equal(_keyA, unlock.KeyfilePath);

            unlock.ClearKeyfileCommand.Execute(null);
            Type(unlock, _master);
            await unlock.UnlockAsync();
            Assert.Equal(0, landed);

            _picker.KeyfilePath = _keyA;
            await unlock.ChooseKeyfileAsync();
            Type(unlock, _master);
            await unlock.UnlockAsync();
            Assert.Equal(1, landed);
        }

        // Replace.
        using (var again = Settings(session))
        {
            Assert.True(again.Access.HasKeyfile);
            _picker.KeyfilePath = _keyB;
            Enter(again.Access, _master);
            await again.Access.ChooseKeyfileCommand.ExecuteAsync();
            await Confirm(again.Access);
            Assert.StartsWith("Changed.", again.Access.Message, StringComparison.Ordinal);
        }

        AssertRefused(_vaultPath, _master, _keyA);
        AssertOpens(_vaultPath, _master, _keyB);

        // Remove.
        using (var last = Settings(session))
        {
            Enter(last.Access, _master);
            last.Access.RemoveKeyfile = true;
            await Confirm(last.Access);
            Assert.StartsWith("Changed.", last.Access.Message, StringComparison.Ordinal);
        }

        Assert.Null(session.Unlocked!.KeyfilePath);
        Assert.Null(Remembered().KeyfilePath);
        AssertRefused(_vaultPath, _master, _keyB);
        AssertOpens(_vaultPath, _master, null);
    }

    [Fact]
    public async Task A_backup_made_under_the_earlier_password_and_keyfile_is_restored_from_the_unlock_screen()
    {
        using var session = Unlocked(_master);

        using (var first = Settings(session))
        {
            _picker.KeyfilePath = _keyA;
            Enter(first.Access, _master);
            await first.Access.ChooseKeyfileCommand.ExecuteAsync();
            await Confirm(first.Access);
        }

        using (var second = Settings(session))
        {
            Enter(second.Access, _master, _later);
            await Confirm(second.Access);
        }

        // The newest copy was kept by the second change, so it opens with the password and keyfile
        // the vault had between the two.
        var newest = VaultBackups.List(_vaultPath)[0];
        AssertOpens(newest.Path, _master, _keyA);
        session.Lock(VaultLockReason.Manual);

        var landed = 0;
        using var unlock = new UnlockViewModel(session, _home, _picker, () => landed++);
        Assert.Equal(_keyA, unlock.KeyfilePath);

        unlock.StartRestoreCommand.Execute(null);
        var restore = Assert.IsType<RestoreBackupViewModel>(unlock.Restore);
        Assert.Equal(_keyA, restore.KeyfilePath);
        Assert.Equal(newest.Path, restore.Selected!.Backup.Path);

        Enter(restore, _master);
        await restore.CheckAsync();
        Assert.True(restore.IsConfirming);

        await restore.ConfirmAsync();

        Assert.Equal(1, landed);
        Assert.True(session.IsUnlocked);
        Assert.Equal(_keyA, session.Unlocked!.KeyfilePath);
        session.Lock(VaultLockReason.Manual);

        AssertOpens(_vaultPath, _master, _keyA);
        AssertRefused(_vaultPath, _later, _keyA);
    }

    // ------------------------------------------------------------------ refusals leave the vault as it was

    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);
        var before = Snapshot();

        Enter(settings.Access, "not-the-password", _later);
        await Confirm(settings.Access);

        Assert.Equal("That isn't the vault's current password. Nothing was changed.", settings.Access.Message);
        Assert.Equal(before, Snapshot());
        Assert.True(session.IsUnlocked);
        Assert.Equal(0, settings.Access.CurrentMaskedLength);
        Assert.Equal(0, settings.Access.NewMaskedLength);
    }

    [Fact]
    public void A_cancelled_confirmation_changes_nothing_and_forgets_every_password()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);
        var access = settings.Access;
        var before = Snapshot();

        Enter(access, _master, _later);
        access.ReviewCommand.Execute(null);
        Assert.True(access.IsConfirming);

        access.CancelCommand.Execute(null);

        Assert.False(access.IsConfirming);
        Assert.False(access.SetPassword);
        Assert.Equal(0, access.CurrentMaskedLength);
        Assert.Equal(0, access.NewMaskedLength);
        Assert.Equal(0, access.ConfirmMaskedLength);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_mismatched_confirmation_changes_nothing()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);
        var before = Snapshot();

        Enter(settings.Access, _master, _later, "a-different-one");
        await Confirm(settings.Access);

        Assert.Equal("Those two new passwords aren't the same. Nothing was changed.", settings.Access.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_keyfile_keyed_by_its_hash_is_refused_at_the_picker_and_by_the_core()
    {
        var document = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(document, "an ordinary document someone might edit");

        using var session = Unlocked(_master);
        using var settings = Settings(session);
        var before = Snapshot();

        _picker.KeyfilePath = document;
        Enter(settings.Access, _master);
        await settings.Access.ChooseKeyfileCommand.ExecuteAsync();

        Assert.Equal(1, _picker.KeyfileCalls);
        Assert.Null(settings.Access.NewKeyfilePath);
        Assert.Contains("won't attach that file", settings.Access.Message, StringComparison.Ordinal);

        // The screen is not the only guard: the session's own route to the core refuses it too.
        using (var current = TempVault.Secret(_master))
        {
            var result = session.ChangeAccess(
                current.Value, new VaultAccessChange(false, AccessKeyfileChange.Attach, document), default, default);

            Assert.Equal(AccessChangeOutcome.Refused, result.Outcome);
            Assert.Equal(VaultAccessOutcome.KeyfileIsFragile, result.Result!.Outcome);
        }

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task A_vault_changed_on_disk_since_the_unlock_is_left_alone()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);

        using (var elsewhere = Vault.Open(_vaultPath, _master))
        {
            elsewhere.AddEntry(new VaultEntry { Title = "from KeePassXC", Password = "x" });
            elsewhere.Save();
        }

        var before = Snapshot();

        Enter(settings.Access, _master, _later);
        await Confirm(settings.Access);

        Assert.StartsWith("The vault file changed since it was unlocked", settings.Access.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
        AssertOpens(_vaultPath, _master, null);
    }

    [Fact]
    public async Task A_failed_save_leaves_the_vault_byte_identical()
    {
        using var session = Unlocked(_master);
        using var settings = Settings(session);

        // The change keeps a copy first; a file where the backup directory must go fails that step.
        File.WriteAllText(VaultBackups.DirectoryFor(_vaultPath), "not a directory");
        var before = Snapshot();

        Enter(settings.Access, _master, _later);
        await Confirm(settings.Access);

        Assert.False(settings.Access.Message.StartsWith("Changed.", StringComparison.Ordinal), settings.Access.Message);
        Assert.Equal(before, Snapshot());
        AssertOpens(_vaultPath, _master, null);
        Assert.True(session.IsUnlocked);
    }

    // ------------------------------------------------------------------ helpers

    private AppVaultSession Unlocked(string password, string? keyfile = null)
    {
        var session = new AppVaultSession(_clock);

        using var master = TempVault.Secret(password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value, keyfile));

        return session;
    }

    private SettingsViewModel Settings(AppVaultSession session) =>
        new(session, _home, new DesktopPreferences(_home), _ => { }, _picker);

    private RecentVault Remembered() =>
        RecentVaults.Load(KeypasteHome.RecentPath(_home)).Single(vault => vault.Path == _vaultPath);

    private static void Enter(VaultAccessViewModel access, string current, string? next = null, string? confirmation = null)
    {
        foreach (var c in current)
        {
            access.TypeCurrent(c);
        }

        if (next is null)
        {
            return;
        }

        access.SetPassword = true;

        foreach (var c in next)
        {
            access.TypeNew(c);
        }

        foreach (var c in confirmation ?? next)
        {
            access.TypeConfirm(c);
        }
    }

    private static async Task Confirm(VaultAccessViewModel access)
    {
        access.ReviewCommand.Execute(null);
        Assert.True(access.IsConfirming, access.Message);
        await access.ChangeAsync();
    }

    private static void Enter(RestoreBackupViewModel restore, string password)
    {
        foreach (var c in password)
        {
            restore.Type(c);
        }
    }

    private static void Type(UnlockViewModel unlock, string password)
    {
        unlock.ClearPassword();

        foreach (var c in password)
        {
            unlock.Type(c);
        }
    }

    /// <summary>The vault's digest and the backups beside it, so a refusal that kept a copy is caught too.</summary>
    private string Snapshot() =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_vaultPath))) + " " +
        string.Join(",", VaultBackups.List(_vaultPath).Select(backup => Path.GetFileName(backup.Path)));

    private static void AssertOpens(string path, string password, string? keyfile)
    {
        using var vault = Vault.Open(path, password, keyfile);
        Assert.NotNull(vault.Find("github"));
    }

    private static void AssertRefused(string path, string password, string? keyfile) =>
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, password, keyfile));
}
