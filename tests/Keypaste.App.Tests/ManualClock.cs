namespace Keypaste.App.Tests;

/// <summary>
/// A clock the test moves by hand, with the wall clock and the monotonic clock movable apart.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>ManualClock</c> in <c>Keypaste.Core.Tests</c>, and hand-rolled for the same
/// reason it gives: a test-only package still enters <c>packages.lock.json</c>, still restores
/// under <c>--locked-mode</c>, and still turns CI red the day it gets a low-severity advisory
/// (docs/PRODUCT.md law 3.9).
/// </para>
/// <para>
/// <b>One thing here that the core's version does not do, and the whole idle policy depends on
/// it:</b> <see cref="GetTimestamp"/> is overridden. <see cref="TimeProvider"/>'s base
/// implementation returns <c>Stopwatch.GetTimestamp()</c>, which no amount of
/// <see cref="Advance"/> can move — so a session that consults the monotonic clock would be
/// half-untested against the core's clock, and the half that was untested is the half that
/// decides whether a sleeping laptop wakes locked. <see cref="Advance"/> moves both;
/// <see cref="AdvanceWallOnly"/> and <see cref="AdvanceMonotonicOnly"/> move one, which is how
/// suspend and an NTP correction are told apart.
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now;
    private long _stamp;

    internal ManualClock(DateTimeOffset? start = null) =>
        _now = start ?? new DateTimeOffset(2026, 7, 28, 9, 12, 44, TimeSpan.Zero);

    /// <summary>One tick is one nanosecond, so a timestamp difference is easy to reason about.</summary>
    public override long TimestampFrequency => 1_000_000_000;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _stamp;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves both clocks forward and fires everything that became due.</summary>
    internal void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Move(by, by);
    }

    /// <summary>
    /// Moves the wall clock only — what a suspended machine looks like on a platform whose
    /// monotonic clock stops while it sleeps.
    /// </summary>
    internal void AdvanceWallOnly(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Move(by, TimeSpan.Zero);
    }

    /// <summary>
    /// Moves the monotonic clock only, and the wall clock backwards — what an NTP correction looks
    /// like, and the case a wall-clock-only design would get wrong.
    /// </summary>
    internal void AdvanceMonotonicOnly(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Move(-by, by);
    }

    private void Move(TimeSpan wall, TimeSpan monotonic)
    {
        FakeTimer[] due;

        lock (_gate)
        {
            _now += wall;
            _stamp += (long)(monotonic.TotalSeconds * TimestampFrequency);
            due = [.. _timers.Where(timer => timer.IsDueAt(_stamp))];
        }

        foreach (var timer in due)
        {
            timer.Fire(GetTimestamp());
        }
    }

    private void Register(FakeTimer timer)
    {
        lock (_gate)
        {
            if (!_timers.Contains(timer))
            {
                _timers.Add(timer);
            }
        }
    }

    private void Forget(FakeTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    /// <summary>
    /// Timers are scheduled on the monotonic clock, which is what a real
    /// <see cref="TimeProvider"/> does — a wall-clock jump must not fire a timer early.
    /// </summary>
    private sealed class FakeTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private readonly Lock _gate = new();
        private long? _dueAt;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : clock.GetTimestamp() + (long)(dueTime.TotalSeconds * clock.TimestampFrequency);
            }

            if (_dueAt is null)
            {
                clock.Forget(this);
            }
            else
            {
                clock.Register(this);
            }

            return true;
        }

        internal bool IsDueAt(long stamp)
        {
            lock (_gate)
            {
                return !_disposed && _dueAt is { } due && stamp >= due;
            }
        }

        internal void Fire(long stamp)
        {
            lock (_gate)
            {
                if (_disposed || _dueAt is null || stamp < _dueAt)
                {
                    return;
                }

                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? null
                    : stamp + (long)(_period.TotalSeconds * clock.TimestampFrequency);
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _dueAt = null;
            }

            clock.Forget(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
