namespace Keypaste.Core.Tests;

/// <summary>
/// A clock the test moves by hand, so a forty-five second window can be tested in no time at all.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than <c>Microsoft.Extensions.TimeProvider.Testing</c>'s
/// <c>FakeTimeProvider</c>, and the reason is supply chain rather than taste: a test-only package
/// still enters <c>packages.lock.json</c>, still restores under <c>--locked-mode</c>, and still
/// turns CI red the day it gets a low-severity advisory under <c>NuGetAudit</c>. That is a real
/// cost for a class with two overrides (CORE.md law 3.9).
/// </para>
/// <para>
/// <b>Callbacks fire on the thread that calls <see cref="Advance"/>.</b> Anything they complete
/// continues inline unless it was created with
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>, so a test that advances the
/// clock is running production continuations on its own thread. That is deliberate — it makes the
/// ordering deterministic — but it is why nothing under test may block waiting for the test thread.
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now;

    internal ManualClock(DateTimeOffset? start = null) =>
        _now = start ?? new DateTimeOffset(2026, 7, 26, 14, 3, 11, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires everything that became due.</summary>
    internal void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        FakeTimer[] due;

        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.IsDueAt(_now))];
        }

        foreach (var timer in due)
        {
            timer.Fire(GetUtcNow());
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

    private sealed class FakeTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private readonly Lock _gate = new();
        private DateTimeOffset? _dueAt;
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
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
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

        internal bool IsDueAt(DateTimeOffset now)
        {
            lock (_gate)
            {
                return !_disposed && _dueAt is { } due && now >= due;
            }
        }

        internal void Fire(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_disposed || _dueAt is null || now < _dueAt)
                {
                    return;
                }

                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
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
