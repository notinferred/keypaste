namespace Keypaste.Core.Approval;

/// <summary>
/// A moment in the future, held on both clocks so that moving either one cannot move it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neither clock is enough on its own.</b> The wall clock can be moved backwards by an NTP
/// correction or by hand, which pushes a wall-clock deadline away and makes an expired approval
/// usable again. The monotonic clock cannot be moved, but it does not advance across suspend on
/// every platform, so a machine asleep for an hour can wake with a five-minute grant still live and
/// nobody in the room. Capturing both at the start costs one <see cref="long"/>, and then each
/// caller says which way it needs to be wrong.
/// </para>
/// <para>
/// <b>And the direction is not the same for a permission and for a refusal.</b>
/// <see cref="HasExpired"/> takes whichever clock says <em>more</em> time has passed, so both kinds
/// of clock error end a grant early; the worst that costs is one more prompt.
/// <see cref="StillInForce"/> takes whichever says <em>less</em>, because a cooldown is a person's
/// "no" and the fail-open direction there is ending it early — a wall clock nudged forward must not
/// be a way to ask again. Two methods rather than one, because a single "elapsed" would have to be
/// wrong for one of them (docs/PRODUCT.md law 3.7).
/// </para>
/// <para>
/// This is the rule <see cref="TimeSpan"/>-based idle locking and the clipboard countdown already
/// apply in the desktop app; it lives here so the approval path cannot drift away from it.
/// </para>
/// </remarks>
/// <param name="Wall">The wall clock when the deadline started.</param>
/// <param name="Stamp">The monotonic timestamp when the deadline started.</param>
/// <param name="Duration">How long it lasts.</param>
internal readonly record struct Deadline(DateTimeOffset Wall, long Stamp, TimeSpan Duration)
{
    /// <summary>Starts a deadline now.</summary>
    /// <param name="clock">The clock both readings come from.</param>
    /// <param name="duration">How long it lasts.</param>
    /// <returns>The deadline.</returns>
    internal static Deadline Starting(TimeProvider clock, TimeSpan duration) =>
        new(clock.GetUtcNow(), clock.GetTimestamp(), duration);

    /// <summary>Whether a permission this long has run out, on whichever clock ran furthest.</summary>
    /// <param name="clock">The clock to ask.</param>
    /// <returns><see langword="true"/> once it has expired.</returns>
    internal bool HasExpired(TimeProvider clock) => LongerElapsed(clock) >= Duration;

    /// <summary>
    /// How much of the duration is left, never more than the duration and never less than nothing.
    /// </summary>
    /// <param name="clock">The clock to ask.</param>
    /// <returns>The remaining lifetime.</returns>
    /// <remarks>
    /// The clamp is here rather than at each call site because this number is reported to an agent
    /// and written to the audit log, and a lifetime longer than the one a person approved would be
    /// keypaste contradicting its own ceiling.
    /// </remarks>
    internal TimeSpan Remaining(TimeProvider clock)
    {
        var left = Duration - LongerElapsed(clock);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Whether a refusal this long still stands, on whichever clock ran least.</summary>
    /// <param name="clock">The clock to ask.</param>
    /// <returns><see langword="true"/> while it is still in force.</returns>
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
    /// Both readings. The wall one can be negative — that is the whole problem — and it is left
    /// that way rather than floored, because the monotonic one never is: taking the longer of the
    /// two is already the floor, and a second one would be a line no test could turn red.
    /// </summary>
    private (TimeSpan Wall, TimeSpan Monotonic) Elapsed(TimeProvider clock) =>
        (clock.GetUtcNow() - Wall, clock.GetElapsedTime(Stamp));
}
