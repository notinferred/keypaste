using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Approval;

/// <summary>
/// Which connection was granted which field of which entry, and until when.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the client's asserted name.</b> THREATS.md T-3 says that name is unauthenticated — any
/// process that can spawn the bridge can call itself <c>claude-code</c> — so it may be an audit
/// field and never an authorization input. A connection id is the strongest honest scoping
/// available: it means <em>the process the human approved for</em>, and when that process restarts,
/// its connection dies and its grants die with it.
/// </para>
/// <para>
/// <b>The field is part of the key.</b> "Repeat requests for the same entry" is not enough: without
/// the field, an approval for a user name would satisfy a request for a password.
/// </para>
/// </remarks>
/// <param name="ConnectionId">Which connection was granted this, minted by the approver per connection.</param>
/// <param name="Handle">The <see cref="EntryHandle"/> of the entry, so a path and its handle share one grant.</param>
/// <param name="Field">Which field, so an approval for one is not an approval for another.</param>
public readonly record struct GrantKey(string ConnectionId, string Handle, string Field);

/// <summary>
/// Holds released field values until their TTL expires, so a repeat request inside the window does
/// not put the same question in front of a human again.
/// </summary>
/// <remarks>
/// <para>
/// <b>It stores the value, not a capability.</b> A token meaning "you may re-open the vault and
/// read this field" would be worse in three ways: it keeps a capability alive rather than a datum,
/// it re-enters the vault on every hit, and the vault may have changed underneath so the second
/// answer differs from the one the human approved. Storing the value makes a grant exactly what was
/// approved, makes expiry an overwrite, and lets the approver drop its unlocked vault on idle
/// without invalidating grants a human already gave.
/// </para>
/// <para>
/// <b>A hit hands out a copy.</b> The caller disposes what it is given; the cache keeps its own
/// until expiry. Anything else would let one caller zero another's grant, or leave the cache
/// holding a disposed buffer.
/// </para>
/// <para>
/// <b>Expiry zeroes, it does not merely forget.</b> Each grant carries a one-shot timer for its own
/// TTL, so an unused grant is cleared at the moment it expires rather than lingering in the heap
/// until something happens to look for it. The honest limit is the one
/// <see cref="SecretBuffer"/> already states: the value existed as an unclearable
/// <see cref="string"/> before it arrived here, and SECURITY.md says so.
/// </para>
/// </remarks>
public sealed class GrantCache : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<GrantKey, Grant> _grants = [];
    private readonly TimeProvider _clock;
    private bool _disposed;

    /// <summary>Builds an empty cache.</summary>
    /// <param name="clock">The clock TTLs are measured on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public GrantCache(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    /// <summary>How many grants are live. A test hook and a status line, not a decision input.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _grants.Count;
            }
        }
    }

    /// <summary>Takes a copy of a live grant, if there is one.</summary>
    /// <param name="key">Which connection, entry and field.</param>
    /// <param name="value">A copy of the released field, which the caller owns and disposes.</param>
    /// <param name="remaining">How much of the TTL is left, for the audit line and the status line.</param>
    /// <returns><see langword="true"/> when a live grant existed.</returns>
    /// <remarks>
    /// Expiry is checked here as well as by the timer, so a grant can never be used after its TTL
    /// even if a timer has not run yet. A hit is still an agent access and still has to be logged
    /// by the caller (CORE.md law 3.3).
    /// </remarks>
    public bool TryUse(GrantKey key, [NotNullWhen(true)] out ReleasedField? value, out TimeSpan remaining)
    {
        value = null;
        remaining = TimeSpan.Zero;

        lock (_gate)
        {
            if (_disposed || !_grants.TryGetValue(key, out var grant))
            {
                return false;
            }

            var now = _clock.GetUtcNow();

            if (now >= grant.ExpiresAt)
            {
                Forget(key, grant);
                return false;
            }

            value = new ReleasedField(grant.Value.Field, grant.Value.Value);
            remaining = grant.ExpiresAt - now;
            return true;
        }
    }

    /// <summary>Records a grant a human just gave.</summary>
    /// <param name="key">Which connection, entry and field.</param>
    /// <param name="value">The released field. The cache takes a copy; the caller keeps ownership of its own.</param>
    /// <param name="ttl">How long the grant lives.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    public void Store(GrantKey key, ReleasedField value, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(value);

        // The copy's ownership transfers to the dictionary, and every route out of it — Forget,
        // Revoke, Expire, Dispose — zeroes it. CA2000 cannot see a lifetime that leaves the method.
#pragma warning disable CA2000
        var grant = new Grant(new ReleasedField(value.Field, value.Value), _clock.GetUtcNow() + ttl);
#pragma warning restore CA2000

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_grants.TryGetValue(key, out var replaced))
            {
                Forget(key, replaced);
            }

            _grants[key] = grant;

            // Inside the lock, so a grant can never be replaced between being stored and being
            // given its timer — that window would leak a timer and leave a grant that never zeroes.
            grant.Expiry = _clock.CreateTimer(_ => Expire(key), null, ttl, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Zeroes every grant belonging to one connection.</summary>
    /// <param name="connectionId">The connection that went away.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionId"/> is null.</exception>
    /// <remarks>
    /// Called when a client disconnects. A grant outliving the process it was given to would be a
    /// standing authorization nobody asked for.
    /// </remarks>
    public void Revoke(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        lock (_gate)
        {
            foreach (var key in _grants.Keys.Where(k => string.Equals(k.ConnectionId, connectionId, StringComparison.Ordinal)).ToList())
            {
                Forget(key, _grants[key]);
            }
        }
    }

    private void Expire(GrantKey key)
    {
        lock (_gate)
        {
            if (_grants.TryGetValue(key, out var grant) && _clock.GetUtcNow() >= grant.ExpiresAt)
            {
                Forget(key, grant);
            }
        }
    }

    private void Forget(GrantKey key, Grant grant)
    {
        _grants.Remove(key);
        grant.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var grant in _grants.Values)
            {
                grant.Dispose();
            }

            _grants.Clear();
        }
    }

    private sealed class Grant(ReleasedField value, DateTimeOffset expiresAt) : IDisposable
    {
        internal ReleasedField Value { get; } = value;

        internal DateTimeOffset ExpiresAt { get; } = expiresAt;

        internal ITimer? Expiry { get; set; }

        public void Dispose()
        {
            Expiry?.Dispose();
            Value.Dispose();
        }
    }
}
