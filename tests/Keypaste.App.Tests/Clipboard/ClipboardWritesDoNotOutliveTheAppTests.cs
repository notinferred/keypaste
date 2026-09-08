using Keypaste.App.Clipboard;
using Xunit;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// What happens to a clipboard write the platform has not finished when the vault locks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClipboardCountdownTests"/> holds the promise for writes that have already landed, and
/// it holds it with a fake that completes synchronously — which is the whole reason these cases
/// were not covered. A copy suspended inside the windowing system's write is not a hypothetical:
/// the idle timeout, <c>Ctrl/Cmd+L</c>, a minimize and a quit can all arrive while it is
/// outstanding, and the continuation used to run afterwards on an object nothing owned any more.
/// </para>
/// <para>
/// The pair that matters most is
/// <see cref="A_write_still_in_flight_when_the_vault_locks_never_reaches_the_user"/> against
/// <see cref="A_run_command_in_flight_at_a_lock_is_left_where_it_landed"/>. Taking back everything
/// would be easy and wrong; taking back nothing is the defect.
/// </para>
/// </remarks>
public sealed class ClipboardWritesDoNotOutliveTheAppTests
{
    internal const string Secret = "SENTINEL-PASSWORD-4b90de";
    internal const string RunCommand = "keypaste run billing -- ";

    [Fact]
    public async Task A_write_still_in_flight_when_the_vault_locks_never_reaches_the_user()
    {
        var (countdown, clipboard, _) = New();
        clipboard.HoldWrites = true;

        var copy = countdown.CopyAsync(Secret, "Password");
        Assert.Equal(1, clipboard.WritesInFlight);

        countdown.Dispose();
        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await copy;
        await Settle(countdown);

        Assert.Null(clipboard.Content);
        Assert.False(countdown.IsCounting);
    }

    [Fact]
    public async Task A_write_released_after_disposal_cannot_revive_the_timer()
    {
        var (countdown, clipboard, clock) = New();
        clipboard.HoldWrites = true;

        var copy = countdown.CopyAsync(Secret, "Password");
        countdown.Dispose();
        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await copy;
        await Settle(countdown);

        var clears = clipboard.ClearCount;
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.False(countdown.IsCounting);
        Assert.Equal(0, countdown.SecondsLeft);
        Assert.Equal(clears, clipboard.ClearCount);
        Assert.Null(clipboard.Content);
    }

    /// <summary>
    /// A lock has to be immediate. Waiting for the windowing system to hand a write back before the
    /// unlock screen appears would put the lock behind whichever process currently owns the
    /// clipboard, which is the one thing a lock cannot afford to wait on.
    /// </summary>
    [Fact]
    public async Task Disposing_does_not_wait_for_the_platform_to_finish_a_write()
    {
        var (countdown, clipboard, _) = New();
        clipboard.HoldWrites = true;

        var copy = countdown.CopyAsync(Secret, "Password");

        countdown.Dispose();

        Assert.False(copy.IsCompleted);
        Assert.False(countdown.IsCounting);

        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await copy;
        await Settle(countdown);
    }

    /// <summary>
    /// Quitting is the one caller that can afford to wait, and has to: after this the process is
    /// gone and nothing is left to take the secret back.
    /// </summary>
    [Fact]
    public async Task An_orderly_quit_waits_for_the_write_it_started()
    {
        var (countdown, clipboard, _) = New();
        clipboard.HoldWrites = true;

        var copy = countdown.CopyAsync(Secret, "Password");
        var quit = countdown.CloseAsync();

        Assert.False(quit.IsCompleted);

        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await copy;
        await quit;

        Assert.Null(clipboard.Content);
        Assert.False(countdown.IsCounting);
    }

    [Fact]
    public async Task A_quit_after_a_lock_finds_nothing_left_to_do()
    {
        var (countdown, clipboard, _) = New();

        await countdown.CopyAsync(Secret, "Password");

        countdown.Dispose();
        await countdown.CloseAsync();

        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
    }

    [Fact]
    public async Task A_run_command_in_flight_at_a_lock_is_left_where_it_landed()
    {
        var (countdown, clipboard, _) = New();
        clipboard.HoldWrites = true;

        var copy = countdown.CopyPlainAsync(RunCommand, "Run command");
        countdown.Dispose();
        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await copy;
        await Settle(countdown);

        Assert.Equal(RunCommand, clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);
    }

    [Fact]
    public async Task Clearing_now_during_a_write_takes_the_secret_back_when_it_lands()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;
        clipboard.HoldWrites = true;

        var copy = countdown.CopyAsync(Secret, "Password");
        var clear = countdown.ClearNowAsync();

        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await copy;
        await clear;

        Assert.Null(clipboard.Content);
        Assert.False(countdown.IsCounting);
    }

    [Fact]
    public async Task A_clipboard_the_user_changed_before_the_drain_is_left_alone()
    {
        var (countdown, clipboard, _) = New();

        await countdown.CopyAsync(Secret, "Password");

        clipboard.HoldReads = true;
        countdown.Dispose();
        clipboard.ReplaceExternally("a shopping list");
        clipboard.HoldReads = false;
        clipboard.ReleaseReads();
        await Settle(countdown);

        Assert.Equal("a shopping list", clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);
    }

    /// <summary>
    /// The read-back is what tells the countdown whether the clipboard is still its own. When it
    /// fails at the copy there is no baseline, and the deadline used to arrive and do nothing —
    /// leaving the secret there for good, which is the opposite of <c>ClipboardClear.Should</c>'s
    /// fail-closed rule.
    /// </summary>
    [Fact]
    public async Task A_read_back_that_fails_at_the_copy_still_clears_at_the_deadline()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;
        clipboard.ReadFails = true;

        await countdown.CopyAsync(Secret, "Password");
        Assert.True(countdown.IsCounting);

        clock.Advance(TimeSpan.FromSeconds(20));
        await Settle(countdown);

        Assert.Equal(1, clipboard.ClearCount);
        Assert.Null(clipboard.Content);
    }

    [Fact]
    public async Task A_write_that_cannot_reach_the_clipboard_takes_back_the_secret_it_replaced()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");

        clipboard.SetFails = true;
        await countdown.CopyAsync("SENTINEL-OTHER-PASSWORD-1d7cc2", "STRIPE_KEY");

        Assert.Null(clipboard.Content);
        Assert.False(countdown.IsCounting);
        Assert.NotNull(countdown.Failure);
        Assert.DoesNotContain(Secret, countdown.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_copy_does_not_stop_the_next_one()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;

        clipboard.SetFails = true;
        await countdown.CopyAsync(Secret, "Password");

        clipboard.SetFails = false;
        await countdown.CopyAsync(Secret, "Password");

        Assert.Equal(Secret, clipboard.Content);
        Assert.True(countdown.IsCounting);
        Assert.Null(countdown.Failure);
    }

    [Fact]
    public async Task A_secret_in_flight_when_a_run_command_arrives_leaves_the_run_command_alone()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;
        clipboard.HoldWrites = true;

        var secret = countdown.CopyAsync(Secret, "Password");
        var plain = countdown.CopyPlainAsync(RunCommand, "Run command");

        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await secret;
        await plain;

        Assert.Equal(RunCommand, clipboard.Content);
        Assert.False(countdown.IsCounting);

        clock.Advance(TimeSpan.FromSeconds(30));
        await Settle(countdown);

        Assert.Equal(RunCommand, clipboard.Content);
    }

    [Fact]
    public async Task A_run_command_in_flight_when_a_secret_arrives_still_arms_the_countdown()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;
        clipboard.HoldWrites = true;

        var plain = countdown.CopyPlainAsync(RunCommand, "Run command");
        var secret = countdown.CopyAsync(Secret, "Password");

        clipboard.HoldWrites = false;
        clipboard.ReleaseWrites();
        await plain;
        await secret;

        Assert.Equal(Secret, clipboard.Content);
        Assert.True(countdown.IsCounting);

        clock.Advance(TimeSpan.FromSeconds(20));
        await Settle(countdown);

        Assert.Null(clipboard.Content);
    }

    [Fact]
    public async Task An_overlapping_clear_and_copy_never_orphans_the_newer_secret()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync("SENTINEL-FIRST-PASSWORD-90ab3e", "Password");

        clipboard.HoldReads = true;
        var clear = countdown.ClearNowAsync();
        var copy = countdown.CopyAsync(Secret, "STRIPE_KEY");

        clipboard.HoldReads = false;
        clipboard.ReleaseReads();
        await clear;
        await copy;

        Assert.Equal(Secret, clipboard.Content);
        Assert.True(countdown.IsCounting);

        clock.Advance(TimeSpan.FromSeconds(20));
        await Settle(countdown);

        Assert.Null(clipboard.Content);
    }

    // The fire-and-forget paths — the deadline tick and disposal — hand their work to the same
    // queue every other operation goes through, so a test that asserted on the next line would be
    // asserting on whichever half had run. Ask the object when it has finished instead.
    private static Task Settle(ClipboardCountdown countdown) => countdown.SettledAsync();

    private static (ClipboardCountdown Countdown, HeldClipboard Clipboard, ManualClock Clock) New()
    {
        var clipboard = new HeldClipboard();
        var clock = new ManualClock();

        return (new ClipboardCountdown(clipboard, clock), clipboard, clock);
    }
}
