using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>An hourly limit on Automatic: a true sliding hour per agent and row, spent all or nothing.</summary>
public sealed class HourlyLimitTests
{
    private readonly ManualClock _clock = new();

    [Fact]
    public void ALimit_AllowsItsCount_ThenRefuses()
    {
        var limits = new HourlyLimit(_clock);

        Assert.True(limits.TryUse("a", [new RowLimit(string.Empty, 2)]));
        Assert.True(limits.TryUse("a", [new RowLimit(string.Empty, 2)]));
        Assert.False(limits.TryUse("a", [new RowLimit(string.Empty, 2)]));
        Assert.Equal(2, limits.Spent("a", string.Empty));
    }

    [Fact]
    public void NoLimit_AlwaysHasRoom()
    {
        var limits = new HourlyLimit(_clock);

        for (var i = 0; i < 2000; i++)
        {
            Assert.True(limits.TryUse("a", []));
        }
    }

    [Fact]
    public void EachRelease_ComesBackAnHourAfterItWasSpent()
    {
        var limits = new HourlyLimit(_clock);
        RowLimit[] one = [new RowLimit("env:billing", 2)];

        Assert.True(limits.TryUse("a", one));
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(limits.TryUse("a", one));
        Assert.False(limits.TryUse("a", one));

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(limits.TryUse("a", one));
        Assert.False(limits.TryUse("a", one));

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, limits.Spent("a", "env:billing"));
    }

    [Fact]
    public void TheWallClock_DoesNotMoveTheHour()
    {
        var limits = new HourlyLimit(_clock);
        RowLimit[] one = [new RowLimit(string.Empty, 1)];

        Assert.True(limits.TryUse("a", one));
        _clock.AdvanceWallOnly(TimeSpan.FromHours(2));

        Assert.False(limits.TryUse("a", one));
    }

    [Fact]
    public void AgentsAndRows_AreCountedApart()
    {
        var limits = new HourlyLimit(_clock);

        Assert.True(limits.TryUse("a", [new RowLimit("env:billing", 1)]));
        Assert.True(limits.TryUse("b", [new RowLimit("env:billing", 1)]));
        Assert.True(limits.TryUse("a", [new RowLimit("env:shop", 1)]));
        Assert.False(limits.TryUse("a", [new RowLimit("env:billing", 1)]));
    }

    [Fact]
    public void SeveralRows_AreSpentAllOrNothing()
    {
        var limits = new HourlyLimit(_clock);

        Assert.True(limits.TryUse("a", [new RowLimit("env:shop", 1)]));
        Assert.False(limits.TryUse("a", [new RowLimit("env:billing", 5), new RowLimit("env:shop", 1)]));
        Assert.Equal(0, limits.Spent("a", "env:billing"));
    }

    [Fact]
    public void ALoweredLimit_AppliesToWhatWasAlreadySpent()
    {
        var limits = new HourlyLimit(_clock);

        Assert.True(limits.TryUse("a", [new RowLimit(string.Empty, 5)]));
        Assert.True(limits.TryUse("a", [new RowLimit(string.Empty, 5)]));
        Assert.False(limits.TryUse("a", [new RowLimit(string.Empty, 2)]));
    }
}
