using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The cache is what stops a repeat request re-asking a human, and therefore the one place where a
/// secret is handed out without anybody being asked. What it must not do outnumbers what it must.
/// </summary>
public sealed class GrantCacheTests
{
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(300);

    private static GrantKey Key(string connection = "conn-1", string handle = "k1_0123456789abcdef", string field = "password") =>
        new(connection, handle, field);

    private static void Store(GrantCache cache, GrantKey key, string value = "sk_live_x", TimeSpan? ttl = null)
    {
        using var released = new ReleasedField(key.Field, value);
        cache.Store(key, released, ttl ?? _ttl);
    }

    /// <summary>
    /// Asserts the cache has nothing for a key. The <c>using</c> is what satisfies CA2000, and it
    /// is the honest shape too: the claim is that nothing came back, and if that ever fails then
    /// whatever did come back still has to be zeroed rather than dropped on the floor.
    /// </summary>
    private static void AssertNoGrant(GrantCache cache, GrantKey key)
    {
        var found = cache.TryUse(key, out var value, out var remaining);

        using (value)
        {
            Assert.False(found);
            Assert.Null(value);
            Assert.Equal(TimeSpan.Zero, remaining);
        }
    }

    [Fact]
    public void ALiveGrantComesBack()
    {
        using var cache = new GrantCache(new ManualClock());
        Store(cache, Key());

        Assert.True(cache.TryUse(Key(), out var value, out var remaining));

        using (value)
        {
            Assert.Equal("sk_live_x", value.Value.ToString(), StringComparer.Ordinal);
            Assert.Equal("password", value.Field, StringComparer.Ordinal);
            Assert.True(remaining > TimeSpan.Zero);
        }
    }

    [Fact]
    public void AnEmptyCacheGrantsNothing()
    {
        using var cache = new GrantCache(new ManualClock());

        AssertNoGrant(cache, Key());
    }

    /// <summary>
    /// The rule the specification gets wrong. "Repeat requests for the same entry" is not enough:
    /// with the field out of the key, an approval a human gave for a user name would silently
    /// satisfy a request for the password.
    /// </summary>
    [Fact]
    public void AUsernameGrant_DoesNotSatisfyAPasswordRequest()
    {
        using var cache = new GrantCache(new ManualClock());
        Store(cache, Key(field: "username"), "alice");

        AssertNoGrant(cache, Key(field: "password"));
    }

    /// <summary>
    /// The grant belongs to the process the human approved for, not to whoever claims the same
    /// name. THREATS.md T-3: a client's asserted identity is an audit field and never an
    /// authorization input, so a different connection starts with nothing.
    /// </summary>
    [Fact]
    public void AnotherConnection_InheritsNothing()
    {
        using var cache = new GrantCache(new ManualClock());
        Store(cache, Key(connection: "conn-1"));

        AssertNoGrant(cache, Key(connection: "conn-2"));
    }

    [Fact]
    public void ADifferentEntry_IsADifferentGrant()
    {
        using var cache = new GrantCache(new ManualClock());
        Store(cache, Key(handle: "k1_aaaaaaaaaaaaaaaa"));

        AssertNoGrant(cache, Key(handle: "k1_bbbbbbbbbbbbbbbb"));
    }

    [Fact]
    public void OnceTheTtlHasPassed_TheGrantIsGone()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);
        Store(cache, Key(), ttl: TimeSpan.FromSeconds(60));

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True(cache.TryUse(Key(), out var live, out _));
        live.Dispose();

        clock.Advance(TimeSpan.FromSeconds(2));
        AssertNoGrant(cache, Key());
    }

    /// <summary>
    /// A grant nobody looks at again is still cleared, by its own timer, at the moment it expires.
    /// Without this a TTL would only mean "stops being handed out" while the plaintext sat in the
    /// heap for as long as the process lived.
    /// </summary>
    /// <remarks>
    /// What this proves is that the grant leaves the cache unprompted. That the characters are then
    /// zeroed rather than merely dropped is <see cref="ReleasedFieldTests.DisposingZeroesTheReleasedCharacters"/>,
    /// because the cache's own copy is unreachable from here by design — said out loud rather than
    /// letting the name of this test imply it checked both halves.
    /// </remarks>
    [Fact]
    public void AnUnusedGrant_IsClearedByItsOwnTimer()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);
        Store(cache, Key(), ttl: TimeSpan.FromSeconds(60));

        Assert.Equal(1, cache.Count);

        // Nothing looks the grant up. The timer alone has to clear it, or an unused grant lingers
        // until something happens to ask for it.
        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    /// A hit hands out a copy. Anything else would let one caller zero another caller's grant, or
    /// leave the cache holding a buffer somebody else already disposed.
    /// </summary>
    [Fact]
    public void UsingAGrant_DoesNotConsumeIt()
    {
        using var cache = new GrantCache(new ManualClock());
        Store(cache, Key());

        Assert.True(cache.TryUse(Key(), out var first, out _));
        first.Dispose();

        Assert.True(cache.TryUse(Key(), out var second, out _));

        using (second)
        {
            Assert.Equal("sk_live_x", second.Value.ToString(), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// When a client goes away its grants go with it. A grant outliving the process it was given to
    /// would be a standing authorization nobody asked for, and one the human could not see.
    /// </summary>
    [Fact]
    public void WhenAConnectionGoesAway_ItsGrantsGoWithIt()
    {
        using var cache = new GrantCache(new ManualClock());
        Store(cache, Key(connection: "going", field: "password"));
        Store(cache, Key(connection: "going", field: "username"), "alice");
        Store(cache, Key(connection: "staying"));

        cache.Revoke("going");

        AssertNoGrant(cache, Key(connection: "going", field: "password"));
        AssertNoGrant(cache, Key(connection: "going", field: "username"));

        Assert.True(cache.TryUse(Key(connection: "staying"), out var survivor, out _));
        survivor.Dispose();
    }

    [Fact]
    public void ReApprovingReplacesTheGrantRatherThanKeepingBoth()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);

        Store(cache, Key(), "first", TimeSpan.FromSeconds(60));
        Store(cache, Key(), "second", TimeSpan.FromSeconds(60));

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryUse(Key(), out var value, out _));

        using (value)
        {
            Assert.Equal("second", value.Value.ToString(), StringComparer.Ordinal);
        }
    }

    [Fact]
    public void DisposingTheCacheZeroesEverythingInIt()
    {
        var cache = new GrantCache(new ManualClock());
        Store(cache, Key());

        cache.Dispose();

        AssertNoGrant(cache, Key());
    }

    [Fact]
    public void TheCacheRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new GrantCache(null!));

        using var cache = new GrantCache(new ManualClock());

        Assert.Throws<ArgumentNullException>(() => cache.Store(Key(), null!, _ttl));
        Assert.Throws<ArgumentNullException>(() => cache.Revoke(null!));
    }
}
