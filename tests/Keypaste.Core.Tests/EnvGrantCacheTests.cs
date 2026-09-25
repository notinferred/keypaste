using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The timed grants a person gives a repeated <c>keypaste run --session</c>: names only, one exact
/// request each, ended by expiry on either clock, by revoking and by the lifetime's end (T-34).
/// </summary>
public sealed class EnvGrantCacheTests
{
    private const string _key = "env\0billing\0/work\0npm\0run\0deploy";
    private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(15);
    private static readonly string[] _names = ["DATABASE_URL", "STRIPE_KEY"];

    private static void Store(EnvGrantCache cache, string key = _key, TimeSpan? ttl = null) =>
        cache.Store(key, "billing", "dev", "npm run deploy", _names, ttl ?? _ttl);

    [Fact]
    public void AGrant_AnswersOnlyTheSameKeyAndNames()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache);

        Assert.True(cache.TryUse(_key, _names, out var remaining));
        Assert.Equal(_ttl, remaining);
        Assert.False(cache.TryUse(_key + "\0--force", _names, out _));
        Assert.True(cache.TryUse(_key, _names, out _));
    }

    [Fact]
    public void AChangedNameList_ForgetsTheGrant()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache);

        Assert.False(cache.TryUse(_key, ["DATABASE_URL", "STRIPE_KEY", "NEW_KEY"], out _));

        // Forgotten, not merely declined: the approved list does not answer again either.
        Assert.False(cache.TryUse(_key, _names, out _));
        Assert.Empty(cache.InForce());
    }

    [Fact]
    public void AReorderedNameList_IsAChangedOne()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache);

        Assert.False(cache.TryUse(_key, ["STRIPE_KEY", "DATABASE_URL"], out _));
    }

    [Theory]
    [InlineData("both")]
    [InlineData("wall")]
    [InlineData("monotonic")]
    public void ExpiryOnEitherClock_EndsIt(string which)
    {
        var clock = new ManualClock();
        using var cache = new EnvGrantCache(clock);
        Store(cache);

        switch (which)
        {
            case "both":
                clock.Advance(_ttl);
                break;
            case "wall":
                clock.AdvanceWallOnly(_ttl);
                break;
            default:
                clock.AdvanceMonotonicOnly(_ttl);
                break;
        }

        Assert.False(cache.TryUse(_key, _names, out _));
        Assert.Empty(cache.InForce());
    }

    [Fact]
    public void JustBeforeItsEnd_ItStillAnswers()
    {
        var clock = new ManualClock();
        using var cache = new EnvGrantCache(clock);
        Store(cache);

        clock.Advance(_ttl - TimeSpan.FromSeconds(1));

        Assert.True(cache.TryUse(_key, _names, out var remaining));
        Assert.Equal(TimeSpan.FromSeconds(1), remaining);
    }

    [Fact]
    public void RevokeAndRevokeAll()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache, "one");
        Store(cache, "two");
        Store(cache, "three");

        cache.Revoke("one");

        Assert.False(cache.TryUse("one", _names, out _));
        Assert.True(cache.TryUse("two", _names, out _));

        cache.RevokeAll();

        Assert.False(cache.TryUse("two", _names, out _));
        Assert.False(cache.TryUse("three", _names, out _));
        Assert.Empty(cache.InForce());
    }

    [Fact]
    public void AfterDispose_NothingIsStoredOrUsed()
    {
        var cache = new EnvGrantCache(new ManualClock());
        Store(cache, "before");
        cache.Dispose();

        Store(cache, "after");

        Assert.False(cache.TryUse("before", _names, out _));
        Assert.False(cache.TryUse("after", _names, out _));
        Assert.Empty(cache.InForce());
    }

    [Fact]
    public void InForce_IsSoonestFirst_AndCarriesNamesOnly()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache, "long", TimeSpan.FromMinutes(15));
        Store(cache, "short", TimeSpan.FromMinutes(5));

        var listed = cache.InForce();

        Assert.Equal(["short", "long"], listed.Select(grant => grant.Key));
        Assert.Equal(
            new EnvGrantInForce("short", "billing", "dev", "npm run deploy", TimeSpan.FromMinutes(5)) { Entries = ["env/billing/DATABASE_URL", "env/billing/STRIPE_KEY"] },
            listed[0]);
    }

    [Fact]
    public void TheTimerForgetsAnUnusedGrant()
    {
        var clock = new ManualClock();
        using var cache = new EnvGrantCache(clock);
        Store(cache);

        clock.Advance(_ttl);

        Assert.Empty(cache.InForce());
    }

    [Fact]
    public void TheOfferedLength_IsTheCeilingAtMostFifteenMinutes()
    {
        Assert.Equal(900, EnvGrantCache.GrantSeconds(ApprovalLimits.Default));
        Assert.Equal(300, EnvGrantCache.GrantSeconds(ApprovalLimits.Default with { MaximumTtlSeconds = 300 }));
    }

    [Fact]
    public void RevokePrefix_EndsOnlyThoseGrants()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache, "run\0conn-1\0a");
        Store(cache, "run\0conn-1\0b");
        Store(cache, "run\0conn-10\0a");

        cache.RevokePrefix("run\0conn-1\0");

        Assert.Equal(["run\0conn-10\0a"], cache.InForce().Select(grant => grant.Key));
    }

    [Fact]
    public void RevokeLabel_EndsOnlyThatLabelsGrants()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        cache.Store("a", "p", "dev", "npm test", _names, _ttl, "Claude Code", "claude-code");
        cache.Store("b", "p", "dev", "npm test", _names, _ttl, "Cursor", "cursor");
        Store(cache, "c");

        cache.RevokeLabel("claude-code");

        Assert.Equal(["b", "c"], cache.InForce().Select(grant => grant.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InForce_CarriesClientLabelAndEntries()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        cache.Store("a", "p", "dev", "npm test", ["GH"], _ttl, "Claude Code", "claude-code", [new EntryName("personal", "github")]);
        Store(cache, "b");

        var run = cache.InForce().Single(grant => grant.Key == "a");
        var env = cache.InForce().Single(grant => grant.Key == "b");

        Assert.Equal(("Claude Code", "claude-code"), (run.Client, run.Label));
        Assert.Equal(["personal/github"], run.Entries);
        Assert.Equal(["env/billing/DATABASE_URL", "env/billing/STRIPE_KEY"], env.Entries);
        Assert.Null(env.Client);
    }

    [Fact]
    public void RevokeEntries_EndsEveryGrantReleasingAnEditedEntry()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        Store(cache, "env-set");
        cache.Store("run", "p", "dev", "npm test", ["GH"], _ttl, "Claude Code", "claude-code", [new EntryName("personal", "github")]);

        cache.RevokeEntries(VaultEdit.Of(new EntryName("env/billing", "STRIPE_KEY")));
        Assert.Equal(["run"], cache.InForce().Select(grant => grant.Key));

        cache.RevokeEntries(VaultEdit.Everything);
        Assert.Empty(cache.InForce());
    }

    [Fact]
    public void ARunGrant_IsListedAsRun_UnderItsClient()
    {
        using var cache = new EnvGrantCache(new ManualClock());
        cache.Store("run", "p", "dev", "npm test", ["GH"], _ttl, "Claude Code", "claude-code", [new EntryName("personal", "github")]);
        Store(cache, "env");

        var rows = GrantSummary.Of(ApproverActivity.None with { EnvGrants = cache.InForce() });

        Assert.Contains(rows, row => row.Kind == "run" && row.Client == "Claude Code");
        Assert.Contains(rows, row => row.Kind == "env" && row.Client == GrantSummary.EnvClient);
    }
}
