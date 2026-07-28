using System.Reflection;
using Keypaste.App.Clipboard;
using Xunit;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// What the app promises about a secret it put on the clipboard, and what it does not.
/// </summary>
/// <remarks>
/// <para>
/// Every row of the promise table in docs/desktop.md and THREATS.md T-19 that CI can hold is held
/// here. The rows it cannot hold — a process killed with <c>kill -9</c>, a machine losing power, a
/// third-party clipboard manager — are stated in those documents as things nothing protects,
/// because a promise with no test behind it is a sentence (D-0043).
/// </para>
/// <para>
/// The pair that matters most is
/// <see cref="A_clipboard_the_user_changed_is_left_alone"/> against
/// <see cref="A_read_back_that_fails_clears_anyway"/>. One says do not touch what is not ours, the
/// other says clear when we cannot tell — and an implementation that got either backwards would
/// still pass a suite containing only the happy path.
/// </para>
/// </remarks>
public sealed class ClipboardCountdownTests
{
    internal const string Secret = "SENTINEL-PASSWORD-a17f3c";

    [Fact]
    public async Task A_copy_puts_the_secret_on_the_clipboard_and_starts_counting()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");

        Assert.Equal(Secret, clipboard.Content);
        Assert.True(clipboard.ContentWasSetAsASecret);
        Assert.True(countdown.IsCounting);
        Assert.True(countdown.IsVisible);
        Assert.Equal("Password", countdown.Label);
        Assert.Equal(20, countdown.SecondsLeft);
        Assert.Equal(1d, countdown.Fraction);
    }

    [Fact]
    public async Task After_the_deadline_the_clipboard_is_cleared()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");

        clock.Advance(TimeSpan.FromSeconds(19));
        Assert.Equal(Secret, clipboard.Content);
        Assert.Equal(1, countdown.SecondsLeft);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
        Assert.False(countdown.IsCounting);
        Assert.False(countdown.IsVisible);
    }

    [Fact]
    public async Task A_clipboard_the_user_changed_is_left_alone()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");
        clipboard.ReplaceExternally("a shopping list");

        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal("a shopping list", clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);
        Assert.False(countdown.IsCounting);
    }

    [Fact]
    public async Task A_read_back_that_fails_clears_anyway()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");
        clipboard.ReadFails = true;

        clock.Advance(TimeSpan.FromSeconds(20));

        // Not knowing whether the secret is still there is not a reason to leave it there.
        Assert.Equal(1, clipboard.ClearCount);
    }

    /// <summary>
    /// A machine that slept through the window clears on the first tick after it wakes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Timers are scheduled against the monotonic clock, which does not advance across suspend on
    /// every platform, so a laptop closed five seconds into the window fires no tick at all while it
    /// is shut. What must not happen is the countdown resuming where it left off and giving the
    /// secret another fifteen seconds on a machine that has been in a bag overnight.
    /// </para>
    /// <para>
    /// The honest limit, and the reason this test advances a second at the end: the clear happens on
    /// the first tick after the monotonic clock restarts, not at the moment of waking. That is
    /// within a second, and it is a second in which the machine is already unlocked and in front of
    /// its owner.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_machine_that_slept_through_the_window_clears_when_it_wakes()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");

        // Asleep: wall time passes, the monotonic clock does not, and no timer fires.
        clock.AdvanceWallOnly(TimeSpan.FromHours(9));
        Assert.Equal(Secret, clipboard.Content);

        // Awake. The first tick finds the deadline long past.
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
    }

    /// <summary>
    /// A clock moved backwards does not buy the secret more time on the clipboard.
    /// </summary>
    [Fact]
    public async Task A_backwards_clock_correction_does_not_extend_the_window()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");

        // What an NTP correction looks like: the monotonic clock advances, the wall clock does not.
        clock.AdvanceMonotonicOnly(TimeSpan.FromSeconds(20));

        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
    }

    [Fact]
    public async Task Clearing_now_does_not_wait_for_the_deadline()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");
        Assert.True(countdown.ClearNowCommand.CanExecute(null));

        await countdown.ClearNowAsync();

        Assert.Null(clipboard.Content);
        Assert.False(countdown.IsCounting);
        Assert.False(countdown.ClearNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clearing_twice_is_harmless()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");
        await countdown.ClearNowAsync();
        await countdown.ClearNowAsync();

        Assert.Equal(1, clipboard.ClearCount);
    }

    /// <summary>
    /// A second copy supersedes the first, and exactly one countdown is live.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is two timers running: the first one's deadline would arrive
    /// while the second secret is on the clipboard, find a hash it does not recognise, and — if the
    /// rule were "clear anyway" rather than "leave alone" — wipe a secret the user just asked for.
    /// </remarks>
    [Fact]
    public async Task A_second_copy_supersedes_the_first()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");
        clock.Advance(TimeSpan.FromSeconds(15));

        await countdown.CopyAsync("SENTINEL-OTHER-PASSWORD-f62c81", "STRIPE_KEY");

        Assert.Equal(20, countdown.SecondsLeft);
        Assert.Equal("STRIPE_KEY", countdown.Label);

        // The first countdown's deadline would have been here. Nothing must happen at it.
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("SENTINEL-OTHER-PASSWORD-f62c81", clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);

        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(clipboard.Content);
    }

    [Fact]
    public async Task Locking_clears_the_clipboard_rather_than_waiting()
    {
        var (countdown, clipboard, _) = New();

        await countdown.CopyAsync(Secret, "Password");

        // What a lock does: ShellViewModel disposes everything it built.
        countdown.Dispose();

        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
    }

    [Fact]
    public async Task Locking_leaves_alone_a_clipboard_the_user_changed()
    {
        var (countdown, clipboard, _) = New();

        await countdown.CopyAsync(Secret, "Password");
        clipboard.ReplaceExternally("a shopping list");

        countdown.Dispose();

        Assert.Equal("a shopping list", clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);
    }

    [Fact]
    public void Disposing_without_a_copy_touches_nothing()
    {
        var (countdown, clipboard, _) = New();

        countdown.Dispose();

        Assert.Equal(0, clipboard.ClearCount);
        Assert.Equal(0, clipboard.SetCount);
    }

    /// <summary>
    /// A run command is copied plainly: no countdown, no clear, no exclusion formats.
    /// </summary>
    [Fact]
    public async Task A_run_command_is_copied_without_a_countdown()
    {
        var (countdown, clipboard, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyPlainAsync("keypaste run billing -- ", "Run command");

        Assert.False(countdown.IsCounting);
        Assert.False(clipboard.ContentWasSetAsASecret);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("keypaste run billing -- ", clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);
    }

    [Fact]
    public async Task A_clipboard_that_cannot_be_reached_says_so_and_does_not_pretend_to_count()
    {
        var (countdown, clipboard, _) = New();
        using var _disposable = countdown;

        clipboard.SetFails = true;
        await countdown.CopyAsync(Secret, "Password");

        Assert.False(countdown.IsCounting);
        Assert.True(countdown.IsVisible);
        Assert.NotNull(countdown.Failure);

        // The failure names what to do instead, and names no value.
        Assert.DoesNotContain(Secret, countdown.Failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The countdown holds a hash and never the value.
    /// </summary>
    /// <remarks>
    /// KeePassXC keeps the copied secret in a plain string for the whole timeout window purely to
    /// power the same equality guard (O-0008). A hash answers the only question being asked, and
    /// this reflects over every field to prove the obvious implementation was not the one taken.
    /// </remarks>
    [Fact]
    public async Task The_countdown_holds_a_hash_and_never_the_value()
    {
        var (countdown, _, clock) = New();
        using var _disposable = countdown;

        await countdown.CopyAsync(Secret, "Password");
        clock.Advance(TimeSpan.FromSeconds(5));

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var field in countdown.GetType().GetFields(Flags))
        {
            switch (field.GetValue(countdown))
            {
                case string text:
                    Assert.NotEqual(Secret, text);
                    break;

                case char[] characters:
                    Assert.NotEqual(Secret, new string(characters));
                    break;

                default:
                    break;
            }
        }

        // And the properties a binding can reach, which is the other half of the same claim.
        foreach (var property in countdown.GetType().GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length == 0
                && property.GetValue(countdown)?.ToString() is { } text)
            {
                Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
            }
        }
    }

    private static (ClipboardCountdown Countdown, FakeClipboard Clipboard, ManualClock Clock) New()
    {
        var clipboard = new FakeClipboard();
        var clock = new ManualClock();

        // No post function: the tick runs inline, which is what a UI thread would do with it.
        return (new ClipboardCountdown(clipboard, clock), clipboard, clock);
    }
}
