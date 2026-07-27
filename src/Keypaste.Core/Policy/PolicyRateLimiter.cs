namespace Keypaste.Core.Policy;

/// <summary>Enforces each rule's <c>max_per_hour</c>, if it set one.</summary>
/// <remarks>
/// <para>
/// A true sliding window — "no more than N in any sixty minutes ending now" — rather than sixty
/// per-minute buckets. Buckets cost constant memory whatever N is, and let twice the allowance
/// through across a boundary; on a cap whose whole job is to bound how much a silent rule can
/// release, being exactly right is worth an array of N longs. <see cref="PolicyRule.MaximumAllowance"/>
/// is what keeps that array small, and a rule over it is a parse error.
/// </para>
/// <para>
/// <b>Counted per rule, process-wide.</b> Not per connection: a client that wanted more than its
/// allowance would only have to spawn a fresh bridge per call, and a quota that can be reset by the
/// party it constrains is not a quota. Restarting the approver does reset it, which is honest —
/// the person restarting it is the person the rule belongs to.
/// </para>
/// <para>
/// <b>Nothing here is asynchronous, and that is the thread-safety argument.</b> There is no
/// <c>async</c>, <c>Task</c>- or <c>ValueTask</c>-returning member on this type or on
/// <see cref="PolicyGate"/>, so "never hold a lock across an await" is a property of the shape of
/// the type rather than of anyone remembering it.
/// </para>
/// </remarks>
public sealed class PolicyRateLimiter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    /// <summary>Creates a limiter.</summary>
    /// <param name="clock">The clock, injected so an hour can pass in a test without waiting one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public PolicyRateLimiter(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Spends one of a rule's hourly allowance, if it has any left.</summary>
    /// <param name="rule">The rule about to release something.</param>
    /// <returns>
    /// <see langword="true"/> if the release may go ahead. Always <see langword="true"/> for a rule
    /// that set no allowance.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    /// <remarks>
    /// Called once the gate has already decided to grant, and <b>not refunded</b> if the read that
    /// follows fails. Refunding needs a second trip through the lock and a failure path that can
    /// itself fail, and over-counting a cap is the fail-closed direction.
    /// </remarks>
    public bool TryUse(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.MaximumPerHour is not { } capacity)
        {
            return true;
        }

        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            if (!_windows.TryGetValue(rule.Id, out var window))
            {
                window = new Window(capacity);
                _windows[rule.Id] = window;
            }

            return window.TryUse(now);
        }
    }

    /// <summary>How much of a rule's allowance is spent right now.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The number of releases inside the last hour.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    public int Spent(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            return _windows.TryGetValue(rule.Id, out var window) ? window.Spent(now) : 0;
        }
    }

    /// <summary>The timestamps of one rule's last N releases, as a ring.</summary>
    private sealed class Window(int capacity)
    {
        private readonly long[] _ticks = new long[capacity];
        private int _oldest;
        private int _count;

        internal bool TryUse(DateTimeOffset now)
        {
            Forget(now);

            if (_count == _ticks.Length)
            {
                return false;
            }

            _ticks[(_oldest + _count) % _ticks.Length] = now.UtcTicks;
            _count++;
            return true;
        }

        internal int Spent(DateTimeOffset now)
        {
            Forget(now);
            return _count;
        }

        private void Forget(DateTimeOffset now)
        {
            var horizon = now.UtcTicks - TimeSpan.TicksPerHour;

            while (_count > 0 && _ticks[_oldest] <= horizon)
            {
                _oldest = (_oldest + 1) % _ticks.Length;
                _count--;
            }
        }
    }
}
