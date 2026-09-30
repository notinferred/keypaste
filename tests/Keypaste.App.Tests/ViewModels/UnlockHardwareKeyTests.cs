using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Unlocking with a YubiKey on the lock screen: the vault opens only with the key, the screen says
/// to touch it while it waits and why it did not answer, and the recent list offers it next time.
/// </summary>
public sealed class UnlockHardwareKeyTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";

    private static readonly byte[] _secret = Convert.FromHexString("3c1d5e7f90a2b4c6d8e0f1325476980badcfe102");

    private readonly TempHome _home = new();
    private readonly SoftwareYubiKey _device = new(_secret);
    private readonly AppVaultSession _session;
    private readonly FakeVaultFilePicker _picker = new();
    private int _unlockedCalls;

    public UnlockHardwareKeyTests() => _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _home.Path, hardwareKeys: _device);

    public void Dispose()
    {
        _session.Dispose();
        _home.Dispose();
    }

    [Fact]
    public async Task A_vault_with_a_yubikey_opens_only_with_it_and_the_recent_list_offers_it_next_time()
    {
        var path = MakeVault();
        using var model = NewModel();
        model.Offer(path);
        Assert.True(model.OffersHardwareKey);
        Assert.False(model.UsesHardwareKey);

        Type(model, _master);
        await model.UnlockAsync();
        Assert.Equal(0, _unlockedCalls);
        Assert.Equal("That password didn't open this vault.", model.Message);

        model.UseHardwareKeyCommand.Execute(null);
        Assert.Equal("YubiKey, slot 2", model.HardwareKeyLabel);
        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(1, _unlockedCalls);
        Assert.Equal(2, _session.HardwareKeySlot);
        Assert.Equal(2, RecentVaults.Load(KeypasteHome.RecentPath(_home.Path)).Single().HardwareKeySlot);

        _session.Lock(VaultLockReason.Manual);
        using var next = NewModel();
        Assert.Equal(path, next.SelectedPath);
        Assert.True(next.UsesHardwareKey);
        Assert.Equal(2, next.HardwareKeySlot);
    }

    [Fact]
    public async Task Another_keys_secret_is_refused_as_the_password_and_key_together()
    {
        var path = MakeVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: _home.Path, hardwareKeys: new SoftwareYubiKey(new byte[20]));
        using var model = new UnlockViewModel(session, _home.Path, _picker, () => _unlockedCalls++);
        model.Offer(path);
        model.UseHardwareKeyCommand.Execute(null);

        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.Equal(
            "That password and YubiKey didn't open this vault. If the vault uses the other slot, switch to it.", model.Message);
        Assert.Equal(0, model.MaskedLength);
    }

    [Fact]
    public async Task A_missing_key_and_an_empty_slot_are_said_plainly()
    {
        var path = MakeVault();
        using var model = NewModel();
        model.Offer(path);
        model.UseHardwareKeyCommand.Execute(null);
        model.SwitchSlotCommand.Execute(null);
        Assert.Equal(1, model.HardwareKeySlot);

        using var slotOnlyTwo = new AppVaultSession(
            new ManualClock(AppClock.Start), home: _home.Path, hardwareKeys: new SoftwareYubiKey(_secret) { Slots = [2] });
        using var empty = new UnlockViewModel(slotOnlyTwo, _home.Path, _picker, () => _unlockedCalls++);
        empty.Offer(path);
        empty.UseHardwareKeyCommand.Execute(null);
        empty.SwitchSlotCommand.Execute(null);
        Type(empty, _master);
        await empty.UnlockAsync();
        Assert.Equal(
            "That YubiKey's slot 1 isn't set up for challenge-response. If the vault uses the other slot, switch to it.",
            empty.Message);

        _device.Connected = false;
        model.SwitchSlotCommand.Execute(null);
        Type(model, _master);
        await model.UnlockAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.Equal("No YubiKey found. Plug it in and unlock again.", model.Message);
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public async Task The_screen_says_to_touch_the_key_while_it_waits_and_cancel_opens_nothing()
    {
        var path = MakeVault();
        _device.NeverTouched = true;
        using var model = NewModel();
        model.Offer(path);
        model.UseHardwareKeyCommand.Execute(null);
        Type(model, _master);

        var unlocking = model.UnlockAsync();
        await Until(() => model.IsWaitingForTouch);
        Assert.True(model.CancelTouchCommand.CanExecute(null));

        model.CancelTouchCommand.Execute(null);
        await unlocking;

        Assert.False(model.IsWaitingForTouch);
        Assert.Equal("Stopped waiting for the YubiKey. Nothing was opened.", model.Message);
        Assert.False(_session.IsUnlocked);
        Assert.Equal(0, _unlockedCalls);
    }

    [Fact]
    public void A_session_that_reaches_no_hardware_keys_offers_none()
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: _home.Path);
        using var model = new UnlockViewModel(session, _home.Path, _picker, () => { });
        model.Offer(MakeVault());

        Assert.False(model.OffersHardwareKey);
        Assert.False(model.UseHardwareKeyCommand.CanExecute(null));
    }

    private string MakeVault()
    {
        var path = Path.Combine(_home.Path, $"{Guid.NewGuid():n}.kdbx");
        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var vault = Vault.CreateWith(path, _master, null, key);
        vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
        vault.Save();
        return path;
    }

    private UnlockViewModel NewModel() =>
        new(_session, _home.Path, _picker, () => _unlockedCalls++);

    private static void Type(UnlockViewModel model, string password)
    {
        model.ClearPassword();

        foreach (var c in password)
        {
            model.Type(c);
        }
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
