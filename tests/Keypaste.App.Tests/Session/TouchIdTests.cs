using System.Runtime.Versioning;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// 4.10b's Touch ID as far as a machine with no person reaches it: what each keychain status comes to, one keychain
/// service per home, and a test host the keychain does not admit, which reads as no Touch ID, so a password unlock keeps
/// nothing and the screen offers only the full unlock.
/// </summary>
/// <remarks>
/// A test host carries no provisioning profile, so the data protection keychain refuses it as an ad-hoc build is refused
/// (D-0681). Touch ID itself needs a person at a Mac with a fingerprint enrolled and the Developer ID build, which 4.10b's
/// record holds.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class TouchIdTests : IDisposable
{
    private readonly TempVault _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(TouchId.Succeeded, nameof(KeyOutcome.Released))]
    [InlineData(TouchId.UserCanceled, nameof(KeyOutcome.Cancelled))]
    [InlineData(TouchId.ItemNotFound, nameof(KeyOutcome.Gone))]
    [InlineData(TouchId.MissingEntitlement, nameof(KeyOutcome.Unavailable))]
    [InlineData(TouchId.InteractionNotAllowed, nameof(KeyOutcome.Unavailable))]
    [InlineData(TouchId.NotAvailable, nameof(KeyOutcome.Unavailable))]
    public void Each_keychain_status_comes_to_its_outcome(int status, string outcome)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Touch ID is macOS's.");

        Assert.Equal(Enum.Parse<KeyOutcome>(outcome), TouchId.Outcome(status));
    }

    [Theory]
    [InlineData(-25293)]
    [InlineData(TouchId.DuplicateItem)]
    [InlineData(-50)]
    public void A_status_no_outcome_names_is_a_failure_to_report(int status)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Touch ID is macOS's.");

        Assert.Null(TouchId.Outcome(status));
    }

    [Fact]
    public void Each_home_keeps_its_keys_under_its_own_service()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Touch ID is macOS's.");
        var other = Path.Combine(_fixture.Home, "other");

        Assert.Equal(TouchId.ServiceFor(_fixture.Home), TouchId.ServiceFor(_fixture.Home + Path.DirectorySeparatorChar));
        Assert.NotEqual(TouchId.ServiceFor(_fixture.Home), TouchId.ServiceFor(other));
        Assert.StartsWith("com.keypaste.app.quick-unlock.", TouchId.ServiceFor(_fixture.Home), StringComparison.Ordinal);
    }

    [Fact]
    public void The_app_on_macOS_asks_Touch_ID()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Touch ID is macOS's.");

        var keys = MachineQuickUnlock.Keys(_fixture.Home);

        Assert.IsType<TouchId>(keys);
        Assert.Equal("Touch ID", keys.Name);
    }

    [Fact]
    public async Task A_build_the_keychain_refuses_has_no_Touch_ID_and_the_screen_offers_only_the_full_unlock()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Touch ID is macOS's.");
        var reported = new List<string>();
        var touchId = new TouchId(_fixture.Home, (_, site) => reported.Add(site));

        Assert.Equal(TouchId.MissingEntitlement, touchId.Probe());
        Assert.False(touchId.IsAvailable());
        Assert.Equal(KeyOutcome.Unavailable, touchId.Make(new byte[32], out var key));
        Assert.Null(key);
        Assert.Empty(reported);

        _fixture.RememberSelf();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home, quickUnlock: touchId);
        var landed = 0;

        using (var unlock = new UnlockViewModel(session, _fixture.Home, new FakeVaultFilePicker(), () => landed++))
        {
            foreach (var c in TempVault.Password)
            {
                unlock.Type(c);
            }

            await unlock.UnlockAsync();
        }

        Assert.Equal(1, landed);
        Assert.Equal(QuickUnlockOutcome.Unavailable, session.KeepForQuickUnlock());
        session.Lock(VaultLockReason.Manual);

        Assert.False(session.CanResume(_fixture.Path_));
        using var locked = new UnlockViewModel(session, _fixture.Home, new FakeVaultFilePicker(), () => landed++, lockedBy: VaultLockReason.Manual);
        Assert.False(locked.OffersQuickUnlock);
        Assert.True(locked.ShowsPasswordForm);
    }
}
