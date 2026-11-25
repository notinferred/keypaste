using System.Globalization;
using Keypaste.Cli.Clipboard;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The CLI's real clipboard clear, run against the fake clipboard with a clock whose deadline passes at once.
/// </summary>
public sealed class BlockingClearStrategyTests
{
    private static readonly TimeSpan _delay = TimeSpan.FromSeconds(20);

    /// <summary>Whatever the person copied while keypaste waited is theirs, and the deadline leaves it alone.</summary>
    [Fact]
    public void A_clipboard_the_person_replaced_during_the_wait_is_left_alone_at_the_deadline()
    {
        var clipboard = new FakeClipboard();
        Assert.Equal(ClipboardStatus.Ok, clipboard.TrySet("the-copied-secret", out _));
        var clock = new DeadlineClock { BeforeDeadline = () => clipboard.ReplaceExternally("what-the-person-copied") };
        using var status = new StringWriter(CultureInfo.InvariantCulture);

        var baseline = ClipboardBaseline.Take(clipboard);
        new BlockingClearStrategy(clock).ClearAfter(clipboard, baseline, _delay, status);

        Assert.Equal(_delay, clock.DueTime);
        Assert.Equal("what-the-person-copied", clipboard.Content);
        Assert.Equal(0, clipboard.ClearCount);
        Assert.Contains("Clipboard changed since the copy; leaving it alone.", status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_clipboard_still_holding_the_secret_is_cleared_at_the_deadline()
    {
        var clipboard = new FakeClipboard();
        Assert.Equal(ClipboardStatus.Ok, clipboard.TrySet("the-copied-secret", out _));
        var clock = new DeadlineClock();
        using var status = new StringWriter(CultureInfo.InvariantCulture);

        var baseline = ClipboardBaseline.Take(clipboard);
        new BlockingClearStrategy(clock).ClearAfter(clipboard, baseline, _delay, status);

        Assert.NotNull(baseline);
        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
        Assert.Contains("Clipboard cleared.", status.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A read-back that failed straight after the copy leaves no baseline, and the deadline's check clears anyway
    /// rather than leaving the secret on the clipboard.
    /// </summary>
    [Fact]
    public void A_copy_whose_first_read_back_failed_is_cleared_at_the_deadline()
    {
        var clipboard = new FakeClipboard();
        Assert.Equal(ClipboardStatus.Ok, clipboard.TrySet("the-copied-secret", out _));
        clipboard.FailingReads = 1;
        var counted = new ReadCountingClipboard(clipboard);
        using var status = new StringWriter(CultureInfo.InvariantCulture);

        var baseline = ClipboardBaseline.Take(counted);
        new BlockingClearStrategy(new DeadlineClock()).ClearAfter(counted, baseline, _delay, status);

        Assert.Null(baseline);
        Assert.Equal(2, counted.Reads);
        Assert.Null(clipboard.Content);
        Assert.Equal(1, clipboard.ClearCount);
        Assert.Contains("Clipboard cleared.", status.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A clock whose timers fire as soon as they are made, so the wait ends at its deadline and never on an interrupt.</summary>
    private sealed class DeadlineClock : TimeProvider
    {
        /// <summary>Runs during the wait, before the deadline fires, so a test can change the clipboard underneath.</summary>
        internal Action? BeforeDeadline { get; init; }

        /// <summary>The delay the last timer was made with.</summary>
        internal TimeSpan? DueTime { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);

            DueTime = dueTime;
            BeforeDeadline?.Invoke();
            callback(state);
            return new FiredTimer();
        }

        private sealed class FiredTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Counts read-backs, so a test can tell the deadline's check ran rather than an unconditional clear.</summary>
    private sealed class ReadCountingClipboard(FakeClipboard inner) : IClipboard
    {
        internal int Reads { get; private set; }

        public ClipboardStatus TrySet(string text, out string error) => inner.TrySet(text, out error);

        public ClipboardStatus TryReadHash(out byte[] sha256, out string error)
        {
            Reads++;
            return inner.TryReadHash(out sha256, out error);
        }

        public ClipboardStatus TryClear(out string error) => inner.TryClear(out error);
    }
}
