using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// 4.10a on the unlock screen: Windows Hello is asked after every password unlock before the shell shows, and a vault a
/// lock left resumable is offered it as the screen's amber action while the master password stays (D-0552, D-0553).
/// </summary>
public sealed class UnlockQuickUnlockTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly FakeQuickUnlockSigner _hello = new();
    private readonly AppVaultSession _session;
    private readonly FakeVaultFilePicker _picker = new();
    private int _landed;

    public UnlockQuickUnlockTests()
    {
        _fixture.RememberSelf();
        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home, quickUnlock: _hello);
    }

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task A_password_unlock_asks_Windows_Hello_before_the_shell_shows()
    {
        var askedAtLanding = -1;
        using var model = new UnlockViewModel(_session, _fixture.Home, _picker, () => askedAtLanding = _hello.Asked);
        var keeping = new List<bool>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UnlockViewModel.IsKeepingQuickUnlock))
            {
                keeping.Add(model.IsKeepingQuickUnlock);
            }
        };

        Assert.False(model.OffersQuickUnlock);
        Type(model, TempVault.Password);
        await model.UnlockAsync();

        Assert.Equal(1, askedAtLanding);
        Assert.Equal([true, false], keeping);
        Assert.Equal("Confirm with Windows Hello so it can unlock this vault after a lock.", model.KeepingNote);
        Assert.True(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public async Task After_an_idle_lock_the_screen_offers_Windows_Hello_beside_the_password_and_it_opens_the_vault()
    {
        await UnlockAndLockAsync(VaultLockReason.Idle);
        using var model = Screen(VaultLockReason.Idle);

        Assert.True(model.OffersQuickUnlock);
        Assert.Equal("Windows Hello", model.QuickUnlockName);
        Assert.Equal("Unlock it with Windows Hello or its master password.", model.Subtitle);
        Assert.True(model.QuickUnlockIsPrimary);
        Assert.False(model.UnlockIsPrimary);
        Assert.True(model.CanTypePassword);
        Assert.True(model.ResumeCommand.CanExecute(null));

        await model.ResumeAsync();

        Assert.Equal(1, _landed);
        Assert.True(_session.IsUnlocked);
        Assert.Equal(0, model.MaskedLength);
        Assert.Equal(2, _hello.Asked);
    }

    [Fact]
    public async Task The_master_password_still_unlocks_while_Windows_Hello_is_offered()
    {
        await UnlockAndLockAsync(VaultLockReason.Manual);
        using var model = Screen(VaultLockReason.Manual);
        Assert.True(model.OffersQuickUnlock);

        Type(model, TempVault.Password);
        await model.UnlockAsync();

        Assert.Equal(1, _landed);
        Assert.Equal(2, _hello.Asked);
    }

    [Theory]
    [InlineData(nameof(VaultLockReason.Requested))]
    [InlineData(nameof(VaultLockReason.ReloadRefused))]
    public async Task After_a_lock_that_forces_the_full_unlock_the_screen_offers_only_the_password(string locked)
    {
        var reason = Enum.Parse<VaultLockReason>(locked);
        await UnlockAndLockAsync(reason);
        using var model = Screen(reason);

        Assert.False(model.OffersQuickUnlock);
        Assert.False(model.ResumeCommand.CanExecute(null));
        Assert.True(model.UnlockIsPrimary);
        Assert.Equal("Enter its master password to open it.", model.Subtitle);
    }

    [Fact]
    public async Task A_cancelled_Windows_Hello_says_nothing_and_is_offered_again()
    {
        await UnlockAndLockAsync(VaultLockReason.Manual);
        using var model = Screen(VaultLockReason.Manual);
        _hello.Answer = SignOutcome.Cancelled;

        await model.ResumeAsync();

        Assert.Equal(0, _landed);
        Assert.False(model.HasMessage);
        Assert.True(model.OffersQuickUnlock);
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public async Task Windows_Hello_that_is_no_longer_set_up_says_to_use_the_password_and_keeps_the_offer()
    {
        await UnlockAndLockAsync(VaultLockReason.Manual);
        using var model = Screen(VaultLockReason.Manual);
        _hello.Available = false;

        await model.ResumeAsync();

        Assert.Equal(0, _landed);
        Assert.True(model.IsError);
        Assert.Equal("Windows Hello isn't available right now. Enter the master password to unlock.", model.Message);
        Assert.True(model.OffersQuickUnlock);
    }

    [Fact]
    public async Task A_key_changed_since_the_lock_is_said_and_the_screen_stops_offering_Windows_Hello()
    {
        await UnlockAndLockAsync(VaultLockReason.Manual);
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), "a-new-password", "a-new-password");
        }

        using var model = Screen(VaultLockReason.Manual);
        await model.ResumeAsync();

        Assert.Equal(0, _landed);
        Assert.True(model.IsError);
        Assert.Equal(
            "This vault's password or keyfile was changed after it locked, so Windows Hello can't unlock it. Unlock it with the new ones.",
            model.Message);
        Assert.False(model.OffersQuickUnlock);
        Assert.Equal("Enter its master password to open it.", model.Subtitle);
    }

    [Fact]
    public async Task A_copy_Windows_Hello_no_longer_opens_is_said_and_no_longer_offered()
    {
        await UnlockAndLockAsync(VaultLockReason.Manual);
        using var model = Screen(VaultLockReason.Manual);
        _hello.ReplaceKey();

        await model.ResumeAsync();

        Assert.Equal(
            "Windows Hello can't unlock this vault any more. Enter the master password; Windows Hello is offered again after that.",
            model.Message);
        Assert.False(model.OffersQuickUnlock);
    }

    [Fact]
    public async Task Without_Windows_Hello_the_screen_never_offers_it_and_a_password_unlock_asks_nothing()
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);
        using var model = new UnlockViewModel(session, _fixture.Home, _picker, () => _landed++);

        Type(model, TempVault.Password);
        await model.UnlockAsync();
        session.Lock(VaultLockReason.Manual);

        using var next = new UnlockViewModel(session, _fixture.Home, _picker, () => _landed++, lockedBy: VaultLockReason.Manual);
        Assert.Equal(1, _landed);
        Assert.False(next.OffersQuickUnlock);
        Assert.Equal(string.Empty, next.QuickUnlockName);
        Assert.Equal(0, _hello.Asked);
    }

    private async Task UnlockAndLockAsync(VaultLockReason reason)
    {
        using (var model = new UnlockViewModel(_session, _fixture.Home, _picker, () => { }))
        {
            Type(model, TempVault.Password);
            await model.UnlockAsync();
        }

        Assert.True(_session.IsUnlocked);
        _session.Lock(reason);
    }

    private UnlockViewModel Screen(VaultLockReason reason) =>
        new(_session, _fixture.Home, _picker, () => _landed++, lockedBy: reason);

    private static void Type(UnlockViewModel model, string password)
    {
        model.ClearPassword();

        foreach (var c in password)
        {
            model.Type(c);
        }
    }
}
