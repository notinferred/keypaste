using Keypaste.Core.Approval;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>One unlocked lifetime, and the transition every lock ends it with (D-0313).</summary>
public sealed class SessionLifetimeTests
{
    private static readonly GrantKey _key = new("conn-1", "k1_aaaaaaaaaaaaaaaa", "password");

    [Fact]
    public void Ending_it_withdraws_refuses_commits_and_zeroes_what_it_owns()
    {
        using var lifetime = new SessionLifetime();
        using var grants = new GrantCache(new ManualClock());
        lifetime.Own(grants);
        Grant(grants);

        Assert.True(lifetime.TryCommit());

        lifetime.End();

        Assert.False(lifetime.IsLive);
        Assert.False(lifetime.TryCommit());
        Assert.True(lifetime.Ended.IsCancellationRequested);
        Assert.Equal(0, grants.Count);
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

    /// <summary>
    /// A reload's withdrawal first ends the owner's grants, through what the owner handed it, so an
    /// allowance answered across it is not kept; once the lifetime has ended, nothing handed to it runs.
    /// </summary>
    [Fact]
    public void Withdrawing_first_runs_what_it_was_handed_and_an_ended_lifetime_runs_nothing()
    {
        using var lifetime = new SessionLifetime();
        using var grants = new GrantCache(new ManualClock());
        var before = lifetime.Waiting;
        var withdrawnFirst = false;
        lifetime.OnWithdraw(() =>
        {
            withdrawnFirst = !before.IsCancellationRequested;
            grants.RevokeEntries(VaultEdit.Everything);
        });
        Grant(grants);
        var asked = grants.Generation;

        lifetime.Withdraw();

        Assert.True(withdrawnFirst);
        Assert.Equal(0, grants.Count);
        Assert.True(before.IsCancellationRequested);
        using (var released = new ReleasedField("password", "sk_live_x"))
        {
            Assert.False(grants.Store(_key, released, TimeSpan.FromMinutes(1), ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 60), asked));
        }

        var runs = 0;
        lifetime.OnWithdraw(() => runs++);
        lifetime.End();
        lifetime.OnWithdraw(() => runs++);
        lifetime.Withdraw();

        Assert.Equal(0, runs);
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

        using var grants = new GrantCache(new ManualClock());
        lifetime.Own(grants);
        Grant(grants);

        Assert.Equal(0, grants.Count);
    }

    [Fact]
    public void Each_lifetime_has_its_own_session()
    {
        using var first = new SessionLifetime();
        using var second = new SessionLifetime();

        Assert.Equal(32, first.Id.Length);
        Assert.NotEqual(first.Id, second.Id);
    }

    private static void Grant(GrantCache grants)
    {
        using var released = new ReleasedField("password", "sk_live_x");
        grants.Store(_key, released, TimeSpan.FromMinutes(1), ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 60), grants.Generation);
    }
}
