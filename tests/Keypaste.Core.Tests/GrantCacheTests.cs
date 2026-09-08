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

    /// <summary>
    /// V-F.3a's own case. Expiry was measured on the wall clock alone, so an hour's rollback made
    /// an already-expired grant usable again — and the one-shot timer that should have zeroed it
    /// had already been spent. Both clocks are consulted now, and for a permission whichever says
    /// more time has passed is the one that decides.
    /// </summary>
    [Fact]
    public void AWallClockRollback_DoesNotResurrectAnExpiredGrant()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);
        Store(cache, Key(), ttl: TimeSpan.FromMilliseconds(250));

        clock.AdvanceMonotonicOnly(TimeSpan.FromMilliseconds(600));
        clock.RewindWallOnly(TimeSpan.FromHours(1));

        AssertNoGrant(cache, Key());
    }

    /// <summary>
    /// The same case with the timer taken away, which is where the lookup rule is actually pinned.
    /// The class remark promises that a grant can never be used after its TTL "even if a timer has
    /// not run yet", and a wall-clock lookup breaks that promise the moment the wall clock moves
    /// backwards: it would report this grant live with its full 250 ms still to run.
    /// </summary>
    [Fact]
    public void TheLookupAlone_RefusesAnExpiredGrant_WithoutWaitingForTheTimer()
    {
        var clock = new CapturingClock();
        using var cache = new GrantCache(clock);
        Store(cache, Key(), ttl: TimeSpan.FromMilliseconds(250));

        clock.AdvanceMonotonicOnly(TimeSpan.FromMilliseconds(600));
        clock.RewindWallOnly(TimeSpan.FromHours(1));

        AssertNoGrant(cache, Key());
    }

    /// <summary>
    /// The other direction, and why the monotonic clock alone is not the answer either: it does not
    /// advance across suspend on every platform, so a machine asleep for an hour would wake with a
    /// five-minute grant still live and nobody in the room.
    /// </summary>
    [Fact]
    public void AMachineThatSleptThroughTheTtl_WakesWithNoGrant()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);
        Store(cache, Key(), ttl: TimeSpan.FromSeconds(300));

        clock.AdvanceWallOnly(TimeSpan.FromHours(1));

        AssertNoGrant(cache, Key());
    }

    /// <summary>
    /// The half an agent and the audit log actually see: <c>ApproverHandler</c> puts this number in
    /// <c>CredentialReply.TtlSeconds</c>, so a wall clock moved backwards made keypaste report a
    /// remaining lifetime longer than the one a person approved — the bound THREATS.md T-12 claims.
    /// </summary>
    [Fact]
    public void TheRemainingLifetime_NeverExceedsTheApprovedTtl()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);
        var ttl = TimeSpan.FromSeconds(60);
        Store(cache, Key(), ttl: ttl);

        clock.RewindWallOnly(TimeSpan.FromHours(1));

        Assert.True(cache.TryUse(Key(), out var value, out var remaining));

        using (value)
        {
            Assert.InRange(remaining, TimeSpan.Zero, ttl);
        }
    }

    /// <summary>
    /// The timer half of the same defect, and the worse half. Expiry re-checked the wall clock
    /// before forgetting anything, so a rollback turned the one-shot timer into a no-op — nothing
    /// would ever clear that grant again, and the plaintext stayed in the cache for as long as the
    /// process lived.
    /// </summary>
    [Fact]
    public void AnUnusedGrant_IsStillClearedByItsTimer_WhenTheWallClockRunsBackwards()
    {
        var clock = new ManualClock();
        using var cache = new GrantCache(clock);
        Store(cache, Key(), ttl: TimeSpan.FromSeconds(60));

        clock.RewindWallOnly(TimeSpan.FromHours(1));

        // Nothing looks the grant up. The timer alone has to clear it.
        clock.AdvanceMonotonicOnly(TimeSpan.FromSeconds(61));

        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    /// Not a defect this file found, but the one the fix could have introduced. Once expiry stops
    /// re-checking a deadline before forgetting, a callback that had already passed its timer's
    /// disposal check and is waiting on the cache's lock would remove whatever it found under the
    /// key — which, after a re-approval, is somebody else's live grant.
    /// </summary>
    [Fact]
    public void AnExpiryAlreadyInFlight_DoesNotZeroTheGrantThatReplacedIt()
    {
        var clock = new CapturingClock();
        using var cache = new GrantCache(clock);

        Store(cache, Key(), "first", TimeSpan.FromSeconds(60));
        var inFlight = clock.LastCallback!;

        Store(cache, Key(), "second", TimeSpan.FromSeconds(60));

        // What the blocked callback does when it finally takes the lock.
        inFlight(null);

        Assert.True(cache.TryUse(Key(), out var value, out _));

        using (value)
        {
            Assert.Equal("second", value.Value.ToString(), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// A clock whose timers never fire, and whose two clocks move apart.
    /// </summary>
    /// <remarks>
    /// Two things <see cref="ManualClock"/> cannot do, both needed here. It fires due timers inline
    /// as it is moved, so a test cannot hold an expiry callback and run it at a moment of its
    /// choosing; and it cannot show what the <em>lookup</em> decides on its own, because the timer
    /// gets there first and clears the grant either way. Expiry has to hold without the timer, or
    /// the guarantee is only as good as a callback having run.
    /// </remarks>
    private sealed class CapturingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 26, 14, 3, 11, TimeSpan.Zero);
        private long _stamp;

        internal TimerCallback? LastCallback { get; private set; }

        public override long TimestampFrequency => 1_000_000_000;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _stamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);

            LastCallback = callback;
            return new IdleTimer();
        }

        internal void AdvanceMonotonicOnly(TimeSpan by) =>
            _stamp += (long)(by.TotalSeconds * TimestampFrequency);

        internal void RewindWallOnly(TimeSpan by) => _now -= by;

        private sealed class IdleTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
