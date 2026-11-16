using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The hand-moved clock the suites share stays safe to use from more than one thread at once.</summary>
public sealed class ManualClockTests
{
    /// <summary>A timer changed on another thread while the clock moves stops neither, as the shell's folder watch changes its settle timer while a test moves the clock.</summary>
    [Fact]
    public async Task ATimerChangedWhileTheClockMoves_StopsNeither()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new ManualClock();
        // Not disposed: a deadlocked timer would hold its dispose too, and the clock goes with the test.
        var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        var moving = Task.Run(
            () =>
            {
                for (var i = 0; i < 100_000; i++)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(1));
                }
            },
            token);
        var changing = Task.Run(
            () =>
            {
                for (var i = 0; i < 100_000; i++)
                {
                    timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
                }
            },
            token);

        var both = Task.WhenAll(moving, changing);

        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(30), token)));
    }
}
