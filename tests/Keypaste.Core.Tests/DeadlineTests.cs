using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// One deadline, two clocks, and two opposite readings of them — because a permission and a
/// refusal need the clock's errors to point in opposite directions.
/// </summary>
/// <remarks>
/// Held here as well as at the two call sites, because these are the four lines that decide whether
/// changing a computer's clock is a way to extend an agent's access or to take back somebody's no.
/// </remarks>
public sealed class DeadlineTests
{
    private static readonly TimeSpan _minute = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A machine asleep through the TTL: the monotonic clock did not move, and only the wall clock
    /// can say what happened.
    /// </summary>
    [Fact]
    public void APermissionExpires_WhenOnlyTheWallClockSaysSo()
    {
        var clock = new ManualClock();
        var deadline = Deadline.Starting(clock, _minute);

        clock.AdvanceWallOnly(TimeSpan.FromHours(1));

        Assert.True(deadline.HasExpired(clock));
    }

    /// <summary>
    /// The other half: an hour taken off the wall clock, which is the correction that used to make
    /// an expired grant usable again.
    /// </summary>
    [Fact]
    public void APermissionExpires_WhenOnlyTheMonotonicClockSaysSo()
    {
        var clock = new ManualClock();
        var deadline = Deadline.Starting(clock, _minute);

        clock.AdvanceMonotonicOnly(TimeSpan.FromMinutes(2));

        Assert.True(deadline.HasExpired(clock));
    }

    /// <summary>
    /// A refusal reads the same two clocks the opposite way round. Either one alone saying the
    /// minute is up is not enough, because ending a person's "no" early is the fail-open direction.
    /// </summary>
    [Fact]
    public void ARefusalStands_UntilBothClocksAgreeItIsOver()
    {
        var wallJumped = new ManualClock();
        var refusal = Deadline.Starting(wallJumped, _minute);
        wallJumped.AdvanceWallOnly(TimeSpan.FromHours(1));

        Assert.True(refusal.StillInForce(wallJumped));

        var wallRolledBack = new ManualClock();
        var other = Deadline.Starting(wallRolledBack, _minute);
        wallRolledBack.AdvanceMonotonicOnly(TimeSpan.FromMinutes(2));

        Assert.True(other.StillInForce(wallRolledBack));

        var ordinary = new ManualClock();
        var spent = Deadline.Starting(ordinary, _minute);
        ordinary.Advance(TimeSpan.FromMinutes(2));

        Assert.False(spent.StillInForce(ordinary));
    }

    /// <summary>
    /// Remaining lifetime is reported to an agent and written to the audit log, so it has to be a
    /// duration rather than arithmetic: never longer than what was approved, and never a negative
    /// number of seconds.
    /// </summary>
    [Fact]
    public void RemainingIsNeverMoreThanTheDuration_AndNeverLessThanNothing()
    {
        var clock = new ManualClock();
        var deadline = Deadline.Starting(clock, _minute);

        Assert.Equal(_minute, deadline.Remaining(clock));

        clock.RewindWallOnly(TimeSpan.FromHours(1));
        Assert.InRange(deadline.Remaining(clock), TimeSpan.Zero, _minute);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.Zero, deadline.Remaining(clock));
    }
}
