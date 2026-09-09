namespace Keypaste.Core.Approval;

/// <summary>A moment in the future, held on both clocks so that moving either one cannot move it.</summary>
/// <remarks>
/// Neither clock is enough alone: the wall clock can be moved backwards by an NTP correction, which
/// makes an expired approval usable again, and the monotonic clock does not advance across suspend on
/// every platform, so a machine asleep for an hour wakes with a five-minute grant still live.
/// The direction is not the same for a permission and a refusal. <see cref="HasExpired"/> takes
/// whichever clock says <em>more</em> time has passed, so both kinds of clock error end a grant early
/// and the worst that costs is one more prompt. <see cref="StillInForce"/> takes whichever says
/// <em>less</em>, because a cooldown is a person's "no" and ending it early is the fail-open
/// direction there. One "elapsed" would have to be wrong for one of them.
/// </remarks>
internal readonly record struct Deadline(DateTimeOffset Wall, long Stamp, TimeSpan Duration)
{
    /// <summary>Starts a deadline now, reading both clocks.</summary>
    internal static Deadline Starting(TimeProvider clock, TimeSpan duration) =>
        new(clock.GetUtcNow(), clock.GetTimestamp(), duration);

    /// <summary>Whether a permission this long has run out, on whichever clock ran furthest.</summary>
    internal bool HasExpired(TimeProvider clock) => LongerElapsed(clock) >= Duration;

    /// <summary>How much of the duration is left, never more than it and never less than
    /// nothing.</summary>
    /// <remarks>
    /// The clamp is here rather than at each call site because this number reaches an agent and the
    /// audit log, and a lifetime longer than the one a person approved would contradict the ceiling.
    /// </remarks>
    internal TimeSpan Remaining(TimeProvider clock)
    {
        var left = Duration - LongerElapsed(clock);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Whether a refusal this long still stands, on whichever clock ran least.</summary>
    internal bool StillInForce(TimeProvider clock) => ShorterElapsed(clock) < Duration;

    private TimeSpan LongerElapsed(TimeProvider clock)
    {
        var (wall, monotonic) = Elapsed(clock);
        return wall > monotonic ? wall : monotonic;
    }

    private TimeSpan ShorterElapsed(TimeProvider clock)
    {
        var (wall, monotonic) = Elapsed(clock);
        return wall < monotonic ? wall : monotonic;
    }

    /// <summary>
    /// Both readings. The wall one can be negative and is left that way rather than floored, because
    /// taking the longer of the two is already the floor and a second would be a line no test can
    /// turn red.
    /// </summary>
    private (TimeSpan Wall, TimeSpan Monotonic) Elapsed(TimeProvider clock) =>
        (clock.GetUtcNow() - Wall, clock.GetElapsedTime(Stamp));
}
