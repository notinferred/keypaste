using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Importing a KeePass file that also needs a YubiKey: the dialog asks the key as the unlock screen does, once,
/// says to touch it while it waits and why it did not answer, and hands its slot on when the file is kept in place.
/// </summary>
public sealed class KdbxImportHardwareKeyTests : IDisposable
{
    private const string _sourcePassword = "import-source";
    private const string _imported = "gh-from-the-yubikey-file";

    private static readonly byte[] _secret = Convert.FromHexString("4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f70");

    private readonly TempVault _vault = new();
    private readonly SoftwareYubiKey _device = new(_secret);
    private readonly AppVaultSession _session;
    private readonly string _source;
    private readonly List<string> _announced = [];
    private readonly List<(string Path, string? Keyfile, int? Slot)> _opened = [];

    public KdbxImportHardwareKeyTests()
    {
        _source = Path.Combine(_vault.Home, "yubikey.kdbx");

        using (var key = new HardwareKey(new SoftwareYubiKey(_secret), 2))
        using (var source = Vault.CreateWith(_source, _sourcePassword, null, key))
        {
            source.AddEntry(new VaultEntry { GroupPath = "Work", Title = "github", Password = _imported });
            source.Save();
        }

        _session = Unlocked(_vault, _device);
    }

    public void Dispose()
    {
        _session.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public async Task A_file_that_needs_a_YubiKey_opens_only_with_it_asking_once_and_its_items_are_imported()
    {
        using var import = NewImport();
        Assert.True(import.OffersHardwareKey);
        Assert.False(import.UsesHardwareKey);

        await Unlock(import);
        Assert.False(import.IsDecrypted);
        Assert.Equal("That password does not open yubikey.kdbx.", import.Message);

        import.UseHardwareKeyCommand.Execute(null);
        Assert.Equal("YubiKey, slot 2", import.HardwareKeyLabel);
        await Unlock(import);

        Assert.True(import.IsDecrypted, import.Message);
        Assert.Equal("YubiKey + password", import.KeyFactors);
        Assert.Single(_device.Challenges);
        Assert.False(import.SwitchSlotCommand.CanExecute(null));

        import.ConfirmCommand.Execute(null);

        Assert.Equal(["Imported 1 item from yubikey.kdbx"], _announced);
        _session.Lock(VaultLockReason.Manual);
        using var reopened = Vault.Open(_vault.Path_, TempVault.Password);
        Assert.Equal(_imported, reopened.Find(new EntryName("yubikey/Work", "github"))!.Password);
    }

    [Fact]
    public async Task Another_keys_secret_is_refused_as_the_password_and_key_together()
    {
        using var other = new TempVault();
        using var session = Unlocked(other, new SoftwareYubiKey(new byte[20]));
        using var import = new KdbxImportViewModel(session, _source, (_, _, _) => { }, _ => { });
        import.UseHardwareKeyCommand.Execute(null);

        await Unlock(import);

        Assert.False(import.IsDecrypted);
        Assert.Equal("That password and YubiKey do not open yubikey.kdbx. If it uses the other slot, switch to it.", import.Message);
        Assert.Equal(0, import.PasswordLength);
    }

    [Fact]
    public async Task A_missing_key_and_an_empty_slot_are_said_plainly()
    {
        using var other = new TempVault();
        using var slotTwoOnly = Unlocked(other, new SoftwareYubiKey(_secret) { Slots = [2] });
        using (var empty = new KdbxImportViewModel(slotTwoOnly, _source, (_, _, _) => { }, _ => { }))
        {
            empty.UseHardwareKeyCommand.Execute(null);
            empty.SwitchSlotCommand.Execute(null);
            Assert.Equal(1, empty.HardwareKeySlot);
            Assert.Equal("Use slot 2", empty.OtherSlotLabel);

            await Unlock(empty);

            Assert.Equal(
                "That YubiKey's slot 1 isn't set up for challenge-response. If yubikey.kdbx uses the other slot, switch to it.",
                empty.Message);
        }

        _device.Connected = false;
        using var import = NewImport();
        import.UseHardwareKeyCommand.Execute(null);

        await Unlock(import);

        Assert.False(import.IsDecrypted);
        Assert.Equal("No YubiKey found. Plug it in and unlock again.", import.Message);
    }

    [Fact]
    public async Task The_dialog_says_to_touch_the_key_while_it_waits_and_cancel_opens_nothing()
    {
        var shellAsked = false;
        _session.WaitingForTouch += (_, waiting) => shellAsked |= waiting;
        _device.NeverTouched = true;
        using var import = NewImport();
        import.UseHardwareKeyCommand.Execute(null);
        Type(import);

        var unlocking = import.UnlockAsync();
        await Until(() => import.IsWaitingForTouch);
        Assert.False(import.UnlockIsPrimary);
        Assert.True(import.CancelTouchCommand.CanExecute(null));

        import.CancelTouchCommand.Execute(null);
        await unlocking;

        Assert.False(import.IsWaitingForTouch);
        Assert.True(import.UnlockIsPrimary);
        Assert.Equal("Stopped waiting for the YubiKey. Nothing was opened.", import.Message);
        Assert.False(import.IsDecrypted);
        Assert.Equal(0, import.PasswordLength);
        Assert.False(shellAsked, "the shell's dialog for a save waiting on the key showed for the import");
    }

    [Fact]
    public async Task Cancelling_the_dialog_while_the_key_waits_stops_waiting_and_opens_nothing()
    {
        _device.NeverTouched = true;
        using var import = NewImport();
        import.UseHardwareKeyCommand.Execute(null);
        Type(import);

        var unlocking = import.UnlockAsync();
        await Until(() => import.IsWaitingForTouch);
        import.CancelCommand.Execute(null);

        await unlocking.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(import.IsWaitingForTouch);
        Assert.Null(import.Source);
    }

    [Fact]
    public void Keeping_the_file_in_place_hands_its_slot_to_the_unlock_screen()
    {
        using var import = NewImport();
        import.UseHardwareKeyCommand.Execute(null);
        import.SwitchSlotCommand.Execute(null);
        import.KeepEditingInPlace = true;

        import.ConfirmCommand.Execute(null);

        Assert.Equal([(Path.GetFullPath(_source), (string?)null, (int?)1)], _opened);

        _session.Lock(VaultLockReason.Manual);
        using var unlock = new UnlockViewModel(_session, _vault.Home, new FakeVaultFilePicker(), () => { });
        Assert.True(unlock.Offer(_source, null, _opened[0].Slot));
        Assert.Equal(1, unlock.HardwareKeySlot);
    }

    [Fact]
    public void A_session_that_reaches_no_hardware_keys_offers_none()
    {
        using var other = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: other.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(other.Path_, master.Value));
        }

        using var import = new KdbxImportViewModel(session, _source, (_, _, _) => { }, _ => { });

        Assert.False(import.OffersHardwareKey);
        Assert.False(import.UseHardwareKeyCommand.CanExecute(null));
    }

    private static AppVaultSession Unlocked(TempVault vault, IChallengeResponseDevice keys)
    {
        var session = new AppVaultSession(new ManualClock(AppClock.Start), home: vault.Home, hardwareKeys: keys);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(vault.Path_, master.Value));
        return session;
    }

    private KdbxImportViewModel NewImport() =>
        new(_session, _source, (path, keyfile, slot) => _opened.Add((path, keyfile, slot)), _announced.Add);

    private static void Type(KdbxImportViewModel import)
    {
        foreach (var c in _sourcePassword)
        {
            import.TypePassword(c);
        }
    }

    private static async Task Unlock(KdbxImportViewModel import)
    {
        Type(import);
        await import.UnlockAsync();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 500 && !condition(); tries++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
