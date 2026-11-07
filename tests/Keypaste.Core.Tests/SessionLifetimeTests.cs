using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>One unlocked lifetime, and the transition every lock ends it with (D-0313).</summary>
public sealed class SessionLifetimeTests
{
    [Fact]
    public void Ending_it_withdraws_refuses_commits_and_zeroes_what_it_owns()
    {
        using var lifetime = new SessionLifetime();
        using var owned = new Owned();
        lifetime.Own(owned);

        Assert.True(lifetime.TryCommit());

        lifetime.End();

        Assert.False(lifetime.IsLive);
        Assert.False(lifetime.TryCommit());
        Assert.True(lifetime.Ended.IsCancellationRequested);
        Assert.True(owned.IsDisposed);
    }

    [Fact]
    public void Withdrawing_refuses_what_was_waiting_and_keeps_the_lifetime()
    {
        using var lifetime = new SessionLifetime();
        var before = lifetime.Waiting;

        lifetime.Withdraw();

        Assert.True(lifetime.IsLive);
        Assert.False(lifetime.Ended.IsCancellationRequested);
        Assert.True(before.IsCancellationRequested);
        Assert.False(lifetime.TryCommitAfter(before));

        var after = lifetime.Waiting;
        Assert.True(lifetime.TryCommitAfter(after));

        lifetime.End();

        Assert.True(after.IsCancellationRequested);
        Assert.False(lifetime.TryCommitAfter(after));
    }

    [Fact]
    public void Ending_it_twice_is_not_an_error()
    {
        using var lifetime = new SessionLifetime();

        lifetime.End();
        lifetime.End();

        Assert.False(lifetime.IsLive);
    }

    [Fact]
    public void What_an_ended_lifetime_is_given_keeps_nothing()
    {
        using var lifetime = new SessionLifetime();
        lifetime.End();

        using var owned = new Owned();
        lifetime.Own(owned);

        Assert.True(owned.IsDisposed);
    }

    [Fact]
    public void Each_lifetime_has_its_own_session()
    {
        using var first = new SessionLifetime();
        using var second = new SessionLifetime();

        Assert.Equal(32, first.Id.Length);
        Assert.NotEqual(first.Id, second.Id);
    }

    private sealed class Owned : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
