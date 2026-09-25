using Keypaste.Core.Approval;
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
        Assert.Equal(new EnvGrantInForce("short", "billing", "dev", "npm run deploy", TimeSpan.FromMinutes(5)), listed[0]);
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
}
