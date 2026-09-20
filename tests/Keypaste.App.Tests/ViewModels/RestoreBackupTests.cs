using System.Security.Cryptography;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Restoring a whole-vault backup from the locked screen, from the save that made the backup to the
/// vault the restore lands in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every backup here was made by a save through a screen.</b> A backup planted beside a fixture
/// would test a reader; what V-V.4b asks for is the journey — change a credential in the app, lock,
/// restore the copy that change left behind, and read the earlier value in the vault the app then
/// opens. The damaged backups are real ones, damaged afterwards.
/// </para>
/// <para>
/// <b>What the file holds is asserted by opening it again</b>, through a separate
/// <see cref="Vault.Open"/> or by its digest, never by what a view model says it did. Every refusal
/// asserts the live file byte for byte, and asserts first that the step it refused was reached.
/// </para>
/// </remarks>
public sealed class RestoreBackupTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";
    private const string _entry = "servers/production";

    private readonly string _directory;
    private readonly string _home;
    private readonly string _vaultPath;
    private readonly ManualClock _clock = new();

    public RestoreBackupTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-restore-app-tests-").FullName;
        _home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using (var vault = Vault.Create(_vaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "production", GroupPath = "servers", Password = "v0" });
            new EnvStore(vault).TrySet("billing", "API_KEY", "k0", out _);
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

    // ------------------------------------------------------------------ the journey

    [Fact]
    public async Task A_credential_changed_in_the_app_is_restored_from_the_locked_screen()
    {
        ChangeThroughTheApp("v1");
        var replaced = File.ReadAllBytes(_vaultPath);

        using var session = new AppVaultSession(_clock);
        var landed = 0;
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => landed++);

        Assert.Equal(_vaultPath, unlock.SelectedPath);
        Assert.True(unlock.OffersRestore);

        unlock.StartRestoreCommand.Execute(null);
        var restore = Assert.IsType<RestoreBackupViewModel>(unlock.Restore);
        Assert.False(unlock.IsOpening);

        var row = Assert.Single(restore.Rows);
        Assert.Same(row, restore.Selected);

        Enter(restore, _master);
        await restore.CheckAsync();

        Assert.True(restore.IsConfirming);
        // The env variable is an entry, and env and env/billing are groups: counts, and no names.
        Assert.Equal("It holds 2 entries in 3 groups, 1 env project among them.", restore.Holds);
        Assert.DoesNotContain("production", restore.Holds + restore.Taken + restore.Replaces, StringComparison.Ordinal);
        Assert.Contains("vault.kdbx", restore.Replaces, StringComparison.Ordinal);
        Assert.Equal(replaced, File.ReadAllBytes(_vaultPath));

        await restore.ConfirmAsync();

        Assert.Equal(1, landed);
        Assert.True(session.IsUnlocked);
        Assert.Equal("v0", session.Unlocked!.Find(_entry)?.Password);
        Assert.Null(unlock.Restore);
        Assert.Contains("is kept beside it", unlock.RestoreNotice, StringComparison.Ordinal);

        session.Lock(VaultLockReason.Manual);

        // Read back without the app: the vault holds the earlier value, and the file the restore
        // replaced is listed and opens with the later one.
        Assert.Equal("v0", Read(_vaultPath));

        var backups = VaultBackups.List(_vaultPath);
        Assert.Equal(2, backups.Count);
        Assert.Equal(replaced, File.ReadAllBytes(backups[0].Path));
        Assert.Equal("v1", Read(backups[0].Path));
    }

    [Fact]
    public async Task The_notice_reaches_the_shell_once_and_does_not_survive_the_lock()
    {
        ChangeThroughTheApp("v1");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        await RestoreNewest(unlock);

        var shell = new ShellViewModel(session, _home, null, restoreNotice: unlock.RestoreNotice);
        Assert.True(shell.HasRestoreNotice);
        Assert.Equal(unlock.RestoreNotice, shell.RestoreNotice);

        session.Lock(VaultLockReason.Manual);
        shell.Dispose();

        Assert.False(shell.HasRestoreNotice);
        Assert.Null(shell.RestoreNotice);
    }

    [Fact]
    public async Task The_notice_can_be_dismissed()
    {
        ChangeThroughTheApp("v1");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });
        await RestoreNewest(unlock);

        using var shell = new ShellViewModel(session, _home, null, restoreNotice: unlock.RestoreNotice);
        shell.DismissRestoreNoticeCommand.Execute(null);

        Assert.False(shell.HasRestoreNotice);
    }

    // ------------------------------------------------------------------ a vault that will not open

    [Fact]
    public async Task A_vault_whose_body_is_damaged_points_to_its_backups_and_is_restored()
    {
        ChangeThroughTheApp("v1");
        DamageBody(_vaultPath);

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        Type(unlock, _master);
        await unlock.UnlockAsync();

        Assert.False(session.IsUnlocked);
        Assert.Contains("restore a backup", unlock.Message, StringComparison.Ordinal);

        await RestoreNewest(unlock);

        Assert.Equal("v0", session.Unlocked!.Find(_entry)?.Password);
    }

    [Fact]
    public async Task A_file_that_is_no_longer_a_vault_is_selectable_for_restore_only()
    {
        ChangeThroughTheApp("v1");
        File.WriteAllText(_vaultPath, "a sync tool's conflict note, where a vault was");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        // Still not a vault, so the offer is still refused; what changes is that it is selected.
        Assert.False(unlock.Offer(_vaultPath));
        Assert.Equal(_vaultPath, unlock.SelectedPath);
        Assert.True(unlock.IsRestoreOnly);
        Assert.False(unlock.CanTypePassword);
        Assert.Contains("kept beside it", unlock.Message, StringComparison.Ordinal);

        Type(unlock, _master);
        Assert.False(unlock.UnlockCommand.CanExecute(null));

        await RestoreNewest(unlock);

        Assert.Equal("v0", session.Unlocked!.Find(_entry)?.Password);
    }

    [Fact]
    public async Task A_missing_vault_is_restored_from_the_recent_list()
    {
        ChangeThroughTheApp("v1");
        File.Delete(_vaultPath);

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        var missing = Assert.Single(unlock.Recent);
        Assert.False(missing.Exists);

        unlock.SelectedRecent = missing;

        Assert.Equal(_vaultPath, unlock.SelectedPath);
        Assert.True(unlock.IsRestoreOnly);

        unlock.StartRestoreCommand.Execute(null);
        Enter(unlock.Restore!, _master);
        await unlock.Restore!.CheckAsync();

        Assert.Contains("no file", unlock.Restore!.Replaces, StringComparison.Ordinal);

        await unlock.Restore!.ConfirmAsync();

        Assert.Equal("v0", session.Unlocked!.Find(_entry)?.Password);
        Assert.Contains("no file to keep", unlock.RestoreNotice, StringComparison.Ordinal);
        Assert.Single(VaultBackups.List(_vaultPath));
    }

    [Fact]
    public void A_missing_vault_with_no_backups_is_still_refused()
    {
        File.Delete(_vaultPath);

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.SelectedRecent = Assert.Single(unlock.Recent);

        Assert.Null(unlock.SelectedPath);
        Assert.Equal("That file isn't there any more.", unlock.Message);
        Assert.False(unlock.StartRestoreCommand.CanExecute(null));
    }

    // ------------------------------------------------------------------ what leaves the live file alone

    [Fact]
    public async Task Cancelling_the_confirmation_changes_nothing_and_forgets_the_password()
    {
        ChangeThroughTheApp("v1");
        var digest = Digest(_vaultPath);

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;
        Enter(restore, _master);
        await restore.CheckAsync();
        Assert.True(restore.IsConfirming);

        restore.CancelCommand.Execute(null);

        Assert.True(restore.IsChoosing);
        Assert.Equal(0, restore.MaskedLength);
        Assert.Equal(digest, Digest(_vaultPath));
        Assert.Single(VaultBackups.List(_vaultPath));

        unlock.CloseRestoreCommand.Execute(null);

        Assert.Null(unlock.Restore);
        Assert.True(unlock.IsOpening);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task A_wrong_password_and_a_damaged_backup_read_the_same_and_change_nothing()
    {
        ChangeThroughTheApp("v1");
        var digest = Digest(_vaultPath);

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;

        Enter(restore, "not the password");
        await restore.CheckAsync();

        var wrong = restore.Message;
        Assert.NotEmpty(wrong);
        Assert.True(restore.IsChoosing);
        Assert.Equal(0, restore.MaskedLength);

        DamageBody(VaultBackups.List(_vaultPath)[0].Path);

        Enter(restore, _master);
        await restore.CheckAsync();

        Assert.Equal(wrong, restore.Message);
        Assert.True(restore.IsChoosing);
        Assert.False(restore.ConfirmCommand.CanExecute(null));
        Assert.Equal(digest, Digest(_vaultPath));
    }

    [Fact]
    public void A_backup_with_no_kdbx_header_is_marked_before_any_password_is_asked()
    {
        ChangeThroughTheApp("v1");

        var backup = VaultBackups.List(_vaultPath)[0].Path;
        var bytes = File.ReadAllBytes(backup);
        Array.Clear(bytes, 0, 8);
        File.WriteAllBytes(backup, bytes);

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;

        var row = Assert.Single(restore.Rows);
        Assert.True(row.IsDamaged);
        Assert.Null(restore.Selected);

        restore.Selected = row;
        Enter(restore, _master);

        Assert.False(restore.CheckCommand.CanExecute(null));
        Assert.NotEmpty(restore.Message);
    }

    [Fact]
    public async Task A_backup_changed_after_it_was_checked_is_not_restored()
    {
        ChangeThroughTheApp("v1");
        var digest = Digest(_vaultPath);

        using var session = new AppVaultSession(_clock);
        var landed = 0;
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => landed++);

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;
        Enter(restore, _master);
        await restore.CheckAsync();
        Assert.True(restore.IsConfirming);

        // The live vault is a real vault under the same password, so everything about the swapped
        // file would pass a fresh look except that it is not what was checked.
        File.Copy(_vaultPath, VaultBackups.List(_vaultPath)[0].Path, overwrite: true);

        await restore.ConfirmAsync();

        Assert.Equal(0, landed);
        Assert.False(session.IsUnlocked);
        Assert.Contains("Nothing was replaced", restore.Message, StringComparison.Ordinal);
        Assert.Equal(0, restore.MaskedLength);
        Assert.Equal(digest, Digest(_vaultPath));
    }

    [Fact]
    public async Task A_vault_that_cannot_be_replaced_is_left_as_it_was()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows refuses a rename onto a file another handle holds; the core suite arranges the failure everywhere.");
            return;
        }

        ChangeThroughTheApp("v1");
        var digest = Digest(_vaultPath);

        using var session = new AppVaultSession(_clock);
        var landed = 0;
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => landed++);

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;
        Enter(restore, _master);
        await restore.CheckAsync();

        using (new FileStream(_vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await restore.ConfirmAsync();
        }

        Assert.Equal(0, landed);
        Assert.False(session.IsUnlocked);
        Assert.Contains("Nothing was replaced", restore.Message, StringComparison.Ordinal);
        Assert.Equal(digest, Digest(_vaultPath));
        Assert.Equal("v1", Read(_vaultPath));
    }

    // ------------------------------------------------------------------ a pending restore does not wait

    [Fact]
    public async Task A_checked_restore_left_idle_forgets_the_password()
    {
        ChangeThroughTheApp("v1");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;
        Enter(restore, _master);
        await restore.CheckAsync();
        Assert.True(restore.IsConfirming);

        _clock.Advance(session.IdleTimeout - TimeSpan.FromSeconds(1));
        Assert.True(restore.IsConfirming);

        _clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(restore.IsChoosing);
        Assert.Equal(0, restore.MaskedLength);
        Assert.False(restore.ConfirmCommand.CanExecute(null));
        Assert.NotEmpty(restore.Message);
    }

    [Fact]
    public void A_typed_password_left_idle_is_forgotten_too()
    {
        ChangeThroughTheApp("v1");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        Enter(unlock.Restore!, _master);

        _clock.Advance(session.IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(0, unlock.Restore!.MaskedLength);
    }

    [Fact]
    public async Task A_minimize_drops_a_pending_restore()
    {
        ChangeThroughTheApp("v1");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;
        Enter(restore, _master);
        await restore.CheckAsync();

        unlock.CancelPendingRestore();

        Assert.True(restore.IsChoosing);
        Assert.Equal(0, restore.MaskedLength);
    }

    [Fact]
    public async Task Choosing_another_copy_abandons_the_check()
    {
        ChangeThroughTheApp("v1");
        AgeBackups();
        ChangeThroughTheApp("v2");

        using var session = new AppVaultSession(_clock);
        using var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { });

        unlock.StartRestoreCommand.Execute(null);
        var restore = unlock.Restore!;
        Assert.Equal(2, restore.Rows.Count);

        Enter(restore, _master);
        await restore.CheckAsync();
        Assert.True(restore.IsConfirming);

        restore.Selected = restore.Rows[1];

        Assert.True(restore.IsChoosing);
        Assert.Equal(0, restore.MaskedLength);
    }

    [Fact]
    public async Task Disposing_the_screen_forgets_everything_and_every_property_still_answers()
    {
        ChangeThroughTheApp("v1");

        using var session = new AppVaultSession(_clock);
        RestoreBackupViewModel restore;

        using (var unlock = new UnlockViewModel(session, _home, new FakeVaultFilePicker(), () => { }))
        {
            unlock.StartRestoreCommand.Execute(null);
            restore = unlock.Restore!;
            Enter(restore, _master);
            await restore.CheckAsync();
            Assert.True(restore.IsConfirming);
        }

        Assert.Empty(restore.Rows);
        Assert.Null(restore.Selected);
        Assert.Equal(0, restore.MaskedLength);
        Assert.True(restore.IsChoosing);
        Assert.Empty(restore.Taken);
        Assert.Empty(restore.Holds);
        Assert.Empty(restore.Replaces);
        Assert.False(restore.ConfirmCommand.CanExecute(null));

        // Expiry arriving after the screen has gone must do nothing, and must not throw.
        _clock.Advance(session.IdleTimeout + TimeSpan.FromSeconds(1));
        Assert.Empty(restore.Message);
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>Changes the password on the Entries screen and locks, which is a real save and so a real backup.</summary>
    private void ChangeThroughTheApp(string password)
    {
        using var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value));
        }

        using var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
        using var entries = new EntriesViewModel(session, countdown);

        entries.Selected = entries.Rows.Single(row => row.Path == _entry);
        var detail = entries.Detail!;

        detail.EditCommand.Execute(null);

        foreach (var c in password)
        {
            detail.NewPassword.Type(c);
        }

        detail.SaveCommand.Execute(null);

        session.Lock(VaultLockReason.Manual);

        Assert.Equal(password, Read(_vaultPath));
    }

    private static async Task RestoreNewest(UnlockViewModel unlock)
    {
        unlock.StartRestoreCommand.Execute(null);
        var restore = Assert.IsType<RestoreBackupViewModel>(unlock.Restore);

        Enter(restore, _master);
        await restore.CheckAsync();
        Assert.True(restore.IsConfirming, restore.Message);

        await restore.ConfirmAsync();
    }

    /// <summary>Moves every backup's stamp back an hour, so the next save is outside the floor (D-0263).</summary>
    private void AgeBackups()
    {
        foreach (var backup in VaultBackups.List(_vaultPath).Reverse())
        {
            var stamp = (backup.TakenAt - TimeSpan.FromHours(1))
                .ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

            File.Move(backup.Path, Path.Combine(Path.GetDirectoryName(backup.Path)!, $"vault.{stamp}.kdbx"));
        }
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
        foreach (var c in password)
        {
            unlock.Type(c);
        }
    }

    private static string Read(string path)
    {
        using var vault = Vault.Open(path, _master);
        return vault.Find(_entry)?.Password ?? throw new InvalidOperationException("no entry");
    }

    private static void DamageBody(string path)
    {
        var bytes = File.ReadAllBytes(path);

        for (var index = bytes.Length * 2 / 3; index < (bytes.Length * 2 / 3) + 64; index++)
        {
            bytes[index] ^= 0xFF;
        }

        File.WriteAllBytes(path, bytes);
    }

    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
