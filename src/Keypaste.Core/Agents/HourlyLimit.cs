namespace Keypaste.Core.Agents;

/// <summary>Counts Automatic releases against each agent's hourly limits, per row.</summary>
/// <remarks>
/// <para>
/// A true sliding window: no more than N in any sixty minutes ending now, measured on the monotonic
/// clock, so a limit cannot be doubled across a boundary or by moving the wall clock.
/// </para>
/// <para>
/// One per owner process, counted per agent and row, not per connection: an agent that wanted more
/// would otherwise only have to start a fresh bridge. It is kept across locks and lost on restart,
/// which is the person's own act.
/// </para>
/// <para>
/// Nothing here is asynchronous, so no lock is ever held across an await.
/// </para>
/// </remarks>
/// <param name="clock">The clock, injected so an hour can pass in a test without waiting one.</param>
public sealed class HourlyLimit(TimeProvider clock)
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Agent, string Row), Queue<long>> _windows = [];

    /// <summary>Spends one release from each of an item's limits, only if every one has a release left.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="limits">The rows' limits, from <see cref="ItemAccess.Limits"/>; none always has room.</param>
    /// <returns>Whether the release may go ahead; nothing is spent when it may not.</returns>
    /// <remarks>Not refunded if the read that follows fails: over-counting a limit is the side that asks more.</remarks>
    public bool TryUse(string agent, IReadOnlyList<RowLimit> limits)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(limits);

        var now = _clock.GetTimestamp();
        var horizon = HourBefore(now);

        lock (_gate)
        {
            List<Queue<long>> windows = [];

            foreach (var limit in limits.GroupBy(entry => entry.Row, StringComparer.Ordinal).Select(row => new RowLimit(row.Key, row.Min(entry => entry.PerHour))))
            {
                if (!_windows.TryGetValue((agent, limit.Row), out var window))
                {
                    window = new Queue<long>();
                    _windows[(agent, limit.Row)] = window;
                }

                Forget(window, horizon);

                if (window.Count >= limit.PerHour)
                {
                    return false;
                }

                windows.Add(window);
            }

            foreach (var window in windows)
            {
                window.Enqueue(now);
            }

            return true;
        }
    }

    /// <summary>How many releases a row's limit has counted in the last hour.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="row">The row, as <see cref="RowLimit.Row"/> names it.</param>
    /// <returns>The count.</returns>
    public int Spent(string agent, string row)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(row);

        var horizon = HourBefore(_clock.GetTimestamp());

        lock (_gate)
        {
            if (!_windows.TryGetValue((agent, row), out var window))
            {
                return 0;
            }

            Forget(window, horizon);
            return window.Count;
        }
    }

    private static void Forget(Queue<long> window, long horizon)
    {
        while (window.TryPeek(out var oldest) && oldest <= horizon)
        {
            window.Dequeue();
        }
    }

    private long HourBefore(long timestamp) => timestamp - (_clock.TimestampFrequency * 3600);
}
