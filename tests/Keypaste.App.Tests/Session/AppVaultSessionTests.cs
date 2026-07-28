using Keypaste.App.Session;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// What the app's vault session promises: it opens, it locks when nobody is there, and the master
/// password is gone whichever way it went.
/// </summary>
/// <remarks>
/// Not one of these starts an Avalonia application. <see cref="AppVaultSession"/> names no Avalonia
/// type precisely so that the assertions CORE.md law 4.5 makes mandatory run in milliseconds and
/// without a display.
/// </remarks>
public sealed class AppVaultSessionTests
{
    [Fact]
    public void The_right_password_opens_the_vault()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        var outcome = Unlock(session, fixture.Path_, TempVault.Password);

        Assert.Equal(UnlockOutcome.Opened, outcome);
        Assert.True(session.IsUnlocked);
        Assert.NotNull(session.Unlocked);
        Assert.Equal(fixture.Path_, session.VaultPath);
    }

    [Fact]
    public void The_wrong_password_leaves_it_locked()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        var outcome = Unlock(session, fixture.Path_, "not-the-password");

        Assert.Equal(UnlockOutcome.WrongPassword, outcome);
        Assert.False(session.IsUnlocked);
        Assert.Null(session.Unlocked);
        Assert.Null(session.VaultPath);
    }

    [Fact]
    public void A_missing_file_is_reported_as_missing()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        Assert.Equal(UnlockOutcome.NotFound, Unlock(session, fixture.MissingPath, TempVault.Password));
    }

    /// <summary>
    /// Answered by the header before the password is used, so somebody who dropped the wrong file
    /// is told that rather than being told their password was wrong.
    /// </summary>
    [Fact]
    public void A_file_that_was_never_a_vault_is_reported_as_that()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        Assert.Equal(UnlockOutcome.NotAKdbx, Unlock(session, fixture.ImposterPath, TempVault.Password));
    }

    // ---- the secret path (CORE.md law 4.5) --------------------------------------------------

    /// <summary>
    /// The caller owns the buffer, so what has to be true is that disposing it zeroes the password
    /// <em>whatever</em> the session did with it — including the paths that never reached Argon2,
    /// and including the wrong-password path, which is the one that happens most.
    /// </summary>
    /// <remarks>
    /// That the unlock screen actually disposes on every one of those paths is asserted against the
    /// view model, which is where the buffer lives and where forgetting would be possible.
    /// </remarks>
    [Theory]
    [InlineData("opened")]
    [InlineData("wrong-password")]
    [InlineData("missing")]
    [InlineData("imposter")]
    public void The_master_password_zeroes_on_dispose_whatever_the_outcome(string scenario)
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        var (path, password, expected) = scenario switch
        {
            "opened" => (fixture.Path_, TempVault.Password, UnlockOutcome.Opened),
            "wrong-password" => (fixture.Path_, "not-the-password", UnlockOutcome.WrongPassword),
            "missing" => (fixture.MissingPath, TempVault.Password, UnlockOutcome.NotFound),
            _ => (fixture.ImposterPath, TempVault.Password, UnlockOutcome.NotAKdbx),
        };

        var master = TempVault.Secret(password);
        UnlockOutcome outcome;

        try
        {
            outcome = session.TryUnlock(path, master.Value);
        }
        finally
        {
            master.Dispose();
        }

        Assert.Equal(expected, outcome);
        Assert.True(master.IsZeroed);
    }

    // ---- idle locking -----------------------------------------------------------------------

    [Fact]
    public void It_stays_unlocked_just_short_of_the_timeout()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));

        Assert.True(session.IsUnlocked);
    }

    [Fact]
    public void It_locks_when_the_timeout_passes()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));

        VaultLockReason? reason = null;
        session.Locked += (_, r) => reason = r;
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(session.IsUnlocked);
        Assert.Null(session.Unlocked);
        Assert.Equal(VaultLockReason.Idle, reason);
    }

    [Fact]
    public void Touching_it_moves_the_deadline()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.Advance(TimeSpan.FromMinutes(4));
        session.Touch();
        clock.Advance(TimeSpan.FromMinutes(4));

        Assert.True(session.IsUnlocked);

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(session.IsUnlocked);
    }

    /// <summary>
    /// A laptop that slept through the timeout must wake locked, even though the timer never fired
    /// because the monotonic clock slept with it. Only the wall clock noticed, and only because the
    /// window asked on activation.
    /// </summary>
    [Fact]
    public void A_suspended_machine_wakes_locked_even_though_the_timer_never_fired()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.AdvanceWallOnly(TimeSpan.FromHours(8));

        // Nothing has fired: timers run on the monotonic clock, which slept too.
        Assert.True(session.IsUnlocked);

        session.Reevaluate();

        Assert.False(session.IsUnlocked);
    }

    /// <summary>Waking inside the timeout must not lock somebody out mid-sentence.</summary>
    [Fact]
    public void A_short_sleep_does_not_lock_on_wake()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.AdvanceWallOnly(TimeSpan.FromMinutes(1));
        session.Reevaluate();

        Assert.True(session.IsUnlocked);
    }

    /// <summary>
    /// The other direction: a clock correction that moves wall time backwards must not extend the
    /// deadline, because the monotonic clock still says the app has been idle.
    /// </summary>
    [Fact]
    public void A_backwards_clock_correction_does_not_buy_more_time()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.AdvanceMonotonicOnly(TimeSpan.FromMinutes(6));

        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void It_warns_once_before_locking()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));

        var warnings = 0;
        session.LockingSoon += (_, _) => warnings++;
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow);
        Assert.Equal(1, warnings);
        Assert.True(session.IsUnlocked);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, warnings);
    }

    [Fact]
    public void Activity_after_a_warning_earns_another_one()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5));

        var warnings = 0;
        session.LockingSoon += (_, _) => warnings++;
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow);
        session.Touch();
        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow);

        Assert.Equal(2, warnings);
        Assert.True(session.IsUnlocked);
    }

    // ---- locking and lifetime ---------------------------------------------------------------

    [Fact]
    public void Locking_twice_raises_the_event_once()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        var locks = 0;
        session.Locked += (_, _) => locks++;
        Unlock(session, fixture.Path_, TempVault.Password);

        session.Lock(VaultLockReason.Manual);
        session.Lock(VaultLockReason.Manual);

        Assert.Equal(1, locks);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void Locking_a_locked_session_raises_nothing()
    {
        using var session = new AppVaultSession(new ManualClock());

        var locks = 0;
        session.Locked += (_, _) => locks++;

        session.Lock(VaultLockReason.Manual);

        Assert.Equal(0, locks);
    }

    [Fact]
    public void Opening_a_second_vault_closes_the_first_and_says_so()
    {
        using var first = new TempVault();
        using var second = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        var reasons = new List<VaultLockReason>();
        session.Locked += (_, r) => reasons.Add(r);

        Unlock(session, first.Path_, TempVault.Password);
        Unlock(session, second.Path_, TempVault.Password);

        Assert.Equal([VaultLockReason.Replaced], reasons);
        Assert.Equal(second.Path_, session.VaultPath);
    }

    [Fact]
    public void Disposing_locks_with_the_shutdown_reason()
    {
        using var fixture = new TempVault();

        // Disposed inside the test on purpose; the using declaration is what keeps CA2000 provable,
        // and Dispose is idempotent so the second call at the end of scope costs nothing.
        using var session = new AppVaultSession(new ManualClock());

        VaultLockReason? reason = null;
        session.Locked += (_, r) => reason = r;
        Unlock(session, fixture.Path_, TempVault.Password);

        session.Dispose();

        Assert.Equal(VaultLockReason.Shutdown, reason);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void Touching_a_locked_session_does_nothing()
    {
        using var session = new AppVaultSession(new ManualClock());

        session.Touch();

        Assert.False(session.IsUnlocked);
    }

    // ---- the timeout setting ----------------------------------------------------------------

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 60)]
    [InlineData(59, 60)]
    [InlineData(300, 300)]
    [InlineData(28_800, 28_800)]
    [InlineData(100_000, 28_800)]
    public void The_timeout_is_clamped_into_a_range_that_always_locks(int given, int expected) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expected),
            AppVaultSession.Clamp(TimeSpan.FromSeconds(given)));

    /// <summary>
    /// There is no "never". A setting that turns the feature off would be the one value everybody
    /// picked the first time the countdown interrupted them.
    /// </summary>
    [Fact]
    public void There_is_no_never()
    {
        Assert.Equal(AppVaultSession.MaximumIdleTimeout, AppVaultSession.Clamp(TimeSpan.MaxValue));
        Assert.Equal(AppVaultSession.MinimumIdleTimeout, AppVaultSession.Clamp(Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void Shortening_the_timeout_takes_effect_at_once()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, TimeSpan.FromHours(4));
        Unlock(session, fixture.Path_, TempVault.Password);

        clock.Advance(TimeSpan.FromMinutes(3));
        session.IdleTimeout = TimeSpan.FromMinutes(1);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void The_default_is_five_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(5), AppVaultSession.DefaultIdleTimeout);

    /// <summary>Opens a vault the way a caller should: the buffer is owned, and it is disposed.</summary>
    private static UnlockOutcome Unlock(AppVaultSession session, string path, string password)
    {
        using var master = TempVault.Secret(password);
        return session.TryUnlock(path, master.Value);
    }
}
