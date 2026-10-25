using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// 4.10a's quick unlock in the session (D-0550 to D-0553): a password unlock keeps the key's parts sealed in memory under
/// Windows Hello, a lock the person made or idleness leaves them and any other lock drops them, and a resume takes the
/// vault's claim, asks Windows Hello and opens the file as an unlock does.
/// </summary>
public sealed class QuickUnlockTests : IDisposable
{
    private const string _next = "a-later-master-password";

    private readonly TempVault _fixture = new();
    private readonly FakeQuickUnlockSigner _hello = new();
    private readonly AppVaultSession _session;

    public QuickUnlockTests() =>
        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home, quickUnlock: _hello);

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void A_password_unlock_keeps_a_copy_that_opens_the_vault_after_a_lock_with_no_password()
    {
        UnlockAndKeep();
        var first = _session.SessionId;
        _session.Lock(VaultLockReason.Manual);

        Assert.True(_session.CanResume(_fixture.Path_));
        Assert.Equal(UnlockOutcome.Opened, _session.TryResume(_fixture.Path_));

        Assert.Equal(2, _hello.Asked);
        Assert.Equal(TempVault.ExamplePassword, _session.Unlocked!.Find("example")?.Password);
        Assert.NotNull(_session.Owner);
        Assert.NotEqual(first, _session.SessionId);

        // The copy stays, so the next lock the person makes resumes again.
        _session.Lock(VaultLockReason.Idle);
        Assert.Equal(UnlockOutcome.Opened, _session.TryResume(_fixture.Path_));
    }

    [Theory]
    [InlineData(nameof(VaultLockReason.Idle))]
    [InlineData(nameof(VaultLockReason.Manual))]
    [InlineData(nameof(VaultLockReason.Minimized))]
    [InlineData(nameof(VaultLockReason.Closed))]
    public void A_lock_the_person_made_or_idleness_keeps_the_copy(string locked)
    {
        var reason = Enum.Parse<VaultLockReason>(locked);
        UnlockAndKeep();

        _session.Lock(reason);

        Assert.True(AppVaultSession.AllowsQuickResume(reason));
        Assert.True(_session.CanResume(_fixture.Path_));
    }

    [Theory]
    [InlineData(nameof(VaultLockReason.Requested))]
    [InlineData(nameof(VaultLockReason.Replaced))]
    [InlineData(nameof(VaultLockReason.AccessChanged))]
    [InlineData(nameof(VaultLockReason.ReloadRefused))]
    [InlineData(nameof(VaultLockReason.Shutdown))]
    public void Any_other_lock_drops_the_copy_and_asks_nobody(string locked)
    {
        var reason = Enum.Parse<VaultLockReason>(locked);
        UnlockAndKeep();

        _session.Lock(reason);

        Assert.False(AppVaultSession.AllowsQuickResume(reason));
        Assert.False(_session.CanResume(_fixture.Path_));
        Assert.Equal(UnlockOutcome.QuickUnlockRefused, _session.TryResume(_fixture.Path_));
        Assert.Equal(QuickUnlockOutcome.None, _session.LastQuickUnlock);
        Assert.Equal(1, _hello.Asked);
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public void Another_vault_opened_in_its_place_drops_its_copy()
    {
        using var other = new TempVault();
        UnlockAndKeep();
        var replaced = new List<VaultLockReason>();
        _session.Locked += (_, reason) => replaced.Add(reason);

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(other.Path_, master.Value));
        }

        Assert.Equal([VaultLockReason.Replaced], replaced);
        Assert.False(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public void Where_Windows_Hello_is_not_set_up_nothing_is_kept_and_nobody_is_asked()
    {
        _hello.Available = false;
        Unlock();

        Assert.Equal(QuickUnlockOutcome.Unavailable, _session.KeepForQuickUnlock());
        _session.Lock(VaultLockReason.Manual);

        Assert.Equal(0, _hello.Asked);
        Assert.False(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public void A_session_without_a_quick_unlock_keeps_nothing()
    {
        using var plain = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, plain.TryUnlock(_fixture.Path_, master.Value));
        }

        Assert.Null(plain.QuickUnlock);
        Assert.Equal(QuickUnlockOutcome.Unavailable, plain.KeepForQuickUnlock());
        plain.Lock(VaultLockReason.Manual);
        Assert.False(plain.CanResume(_fixture.Path_));
        Assert.Equal(UnlockOutcome.QuickUnlockRefused, plain.TryResume(_fixture.Path_));
    }

    [Fact]
    public void Cancelling_the_keep_keeps_nothing_and_the_vault_stays_open()
    {
        _hello.Answer = SignOutcome.Cancelled;
        Unlock();

        Assert.Equal(QuickUnlockOutcome.Cancelled, _session.KeepForQuickUnlock());

        Assert.True(_session.IsUnlocked);
        _session.Lock(VaultLockReason.Manual);
        Assert.False(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public void Cancelling_a_resume_opens_nothing_gives_the_vault_back_and_keeps_the_copy()
    {
        UnlockAndKeep();
        _session.Lock(VaultLockReason.Manual);
        _hello.Answer = SignOutcome.Cancelled;

        Assert.Equal(UnlockOutcome.QuickUnlockRefused, _session.TryResume(_fixture.Path_));

        Assert.Equal(QuickUnlockOutcome.Cancelled, _session.LastQuickUnlock);
        Assert.False(_session.IsUnlocked);
        Assert.True(_session.CanResume(_fixture.Path_));
        Assert.True(VaultClaim.TryAcquire(_fixture.Home, _fixture.Path_, OwnerKind.DesktopApp, out var claim, out _, out _));
        claim.Dispose();

        _hello.Answer = SignOutcome.Signed;
        Assert.Equal(UnlockOutcome.Opened, _session.TryResume(_fixture.Path_));
    }

    [Fact]
    public void A_vault_another_process_holds_is_refused_before_Windows_Hello_is_asked_and_keeps_its_copy()
    {
        UnlockAndKeep();
        _session.Lock(VaultLockReason.Manual);

        using (var holder = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home))
        {
            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, holder.TryUnlock(_fixture.Path_, master.Value));
            }

            Assert.Equal(UnlockOutcome.HeldElsewhere, _session.TryResume(_fixture.Path_));
            Assert.Equal(1, _hello.Asked);
            Assert.True(_session.CanResume(_fixture.Path_));
        }

        Assert.Equal(UnlockOutcome.Opened, _session.TryResume(_fixture.Path_));
    }

    [Fact]
    public void A_vault_whose_key_was_changed_elsewhere_drops_its_copy_and_opens_with_the_new_password()
    {
        UnlockAndKeep();
        _session.Lock(VaultLockReason.Manual);

        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), _next, _next);
        }

        Assert.Equal(UnlockOutcome.KeyChanged, _session.TryResume(_fixture.Path_));

        Assert.False(_session.CanResume(_fixture.Path_));
        Assert.False(_session.IsUnlocked);
        using var master = TempVault.Secret(_next);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    [Fact]
    public void A_file_another_program_saved_while_locked_is_read_as_it_is_now()
    {
        UnlockAndKeep();
        _session.Lock(VaultLockReason.Manual);

        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { Title = "saved-elsewhere", Password = "outside" });
            vault.Save();
        }

        Assert.Equal(UnlockOutcome.Opened, _session.TryResume(_fixture.Path_));
        Assert.Equal("outside", _session.Unlocked!.Find("saved-elsewhere")?.Password);
    }

    [Fact]
    public void A_copy_Windows_Hello_no_longer_opens_is_dropped()
    {
        UnlockAndKeep();
        _session.Lock(VaultLockReason.Manual);
        _hello.ReplaceKey();

        Assert.Equal(UnlockOutcome.QuickUnlockRefused, _session.TryResume(_fixture.Path_));

        Assert.Equal(QuickUnlockOutcome.Unreadable, _session.LastQuickUnlock);
        Assert.False(_session.CanResume(_fixture.Path_));
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public void A_lock_that_forces_the_full_unlock_while_Windows_Hello_asks_leaves_nothing_kept()
    {
        Unlock();
        _hello.WhileAsking = () => _session.Lock(VaultLockReason.Requested);

        Assert.Equal(QuickUnlockOutcome.None, _session.KeepForQuickUnlock());

        Assert.False(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public void An_idle_lock_while_Windows_Hello_asks_keeps_the_copy()
    {
        Unlock();
        _hello.WhileAsking = () => _session.Lock(VaultLockReason.Idle);

        Assert.Equal(QuickUnlockOutcome.Done, _session.KeepForQuickUnlock());

        _hello.WhileAsking = null;
        Assert.Equal(UnlockOutcome.Opened, _session.TryResume(_fixture.Path_));
    }

    [Fact]
    public void A_successful_access_change_drops_the_copy()
    {
        UnlockAndKeep();

        using (var current = TempVault.Secret(TempVault.Password))
        using (var next = TempVault.Secret(_next))
        {
            var result = _session.ChangeAccess(current.Value, new VaultAccessChange(true, AccessKeyfileChange.Keep), next.Value, next.Value);
            Assert.Equal(AccessChangeOutcome.Changed, result.Outcome);
        }

        _session.Lock(VaultLockReason.Manual);
        Assert.False(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public void A_refused_access_change_keeps_the_copy()
    {
        UnlockAndKeep();

        using (var wrong = TempVault.Secret("not-the-password"))
        using (var next = TempVault.Secret(_next))
        {
            var result = _session.ChangeAccess(wrong.Value, new VaultAccessChange(true, AccessKeyfileChange.Keep), next.Value, next.Value);
            Assert.Equal(AccessChangeOutcome.WrongCurrentSecret, result.Outcome);
        }

        _session.Lock(VaultLockReason.Manual);
        Assert.True(_session.CanResume(_fixture.Path_));
    }

    [Fact]
    public void The_copy_holds_neither_the_password_nor_its_SHA_256_and_opens_only_for_its_vault()
    {
        UnlockAndKeep();
        var copies = _session.QuickUnlock!;
        var vault = VaultIdentity.Of(_fixture.Home, _fixture.Path_);
        var kept = copies.SealedFor(vault)!;

        Assert.Equal(-1, kept.AsSpan().IndexOf(Encoding.UTF8.GetBytes(TempVault.Password)));
        Assert.Equal(-1, kept.AsSpan().IndexOf(SHA256.HashData(Encoding.UTF8.GetBytes(TempVault.Password))));

        using var other = new TempVault();
        var opened = copies.Open(VaultIdentity.Of(_fixture.Home, other.Path_), out var parts);
        parts?.Dispose();
        Assert.Equal(QuickUnlockOutcome.None, opened);
        Assert.Null(parts);
    }

    [Fact]
    public void Quitting_drops_every_copy()
    {
        UnlockAndKeep();
        var copies = _session.QuickUnlock!;

        _session.Dispose();

        Assert.Equal(0, copies.Count);
        Assert.False(copies.Has(VaultIdentity.Of(_fixture.Home, _fixture.Path_)));
    }

    [Fact]
    public void A_YubiKey_vault_is_touched_again_on_every_resume_and_a_missing_key_keeps_the_copy()
    {
        var secret = Convert.FromHexString("5a6b7c8d9e0f112233445566778899aabbccddee");
        var device = new SoftwareYubiKey(secret);
        var path = Path.Combine(_fixture.Home, "yubikey.kdbx");

        using (var key = new HardwareKey(new SoftwareYubiKey(secret), 2))
        using (var created = Vault.CreateWith(path, TempVault.Password, null, key))
        {
            created.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            created.Save();
        }

        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home, hardwareKeys: device, quickUnlock: _hello);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value, null, 2));
        }

        Assert.Equal(QuickUnlockOutcome.Done, session.KeepForQuickUnlock());
        session.Lock(VaultLockReason.Manual);

        var touched = device.Challenges.Count;
        Assert.Equal(UnlockOutcome.Opened, session.TryResume(path));
        Assert.Equal(touched + 1, device.Challenges.Count);
        Assert.Equal(2, session.HardwareKeySlot);
        session.Lock(VaultLockReason.Manual);

        device.Connected = false;
        Assert.Equal(UnlockOutcome.HardwareKeyFailed, session.TryResume(path));
        Assert.Equal(HardwareKeyFailure.NotFound, session.LastHardwareKeyFailure?.Failure);
        Assert.True(session.CanResume(path));

        device.Connected = true;
        Assert.Equal(UnlockOutcome.Opened, session.TryResume(path));
        Assert.Equal(touched + 2, device.Challenges.Count);
    }

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    private void UnlockAndKeep()
    {
        Unlock();
        Assert.Equal(QuickUnlockOutcome.Done, _session.KeepForQuickUnlock());
        Assert.Equal(1, _hello.Asked);
    }
}
