namespace Keypaste.Core.Approval;

/// <summary>
/// The timed grants a person gave repeated <c>keypaste run --session</c> requests: which request
/// may run again unasked, with which variable names, until when. It holds no value.
/// </summary>
/// <remarks>
/// <para>
/// Every run is a new connection, so a grant is keyed by the request the runner claims — project,
/// profile, directory and command — rather than by a connection, and any same-user process sending
/// that exact claim is served until it ends (THREATS.md T-34). That is why it lasts at most
/// <see cref="CeilingSeconds"/> and why the prompt offering it says whom it covers.
/// </para>
/// <para>
/// Names, not values: each reuse reads the vault again, so the latest saved values leave, and a set
/// whose names changed is asked about again rather than released under a grant that did not cover it.
/// Expiry is a <see cref="Deadline"/> on whichever clock ran furthest, with a one-shot timer as
/// <see cref="GrantCache"/> has, and a cache that belongs to a lifetime forgets everything when a lock
/// ends it (D-0313).
/// </para>
/// </remarks>
public sealed class EnvGrantCache : IDisposable
{
    /// <summary>The longest timed env grant, however high the approver's ceiling.</summary>
    public const int CeilingSeconds = 900;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Grant> _grants = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private bool _disposed;

    /// <summary>Builds an empty cache.</summary>
    /// <param name="clock">The clock grants expire on.</param>
    public EnvGrantCache(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    /// <summary>The timed env grant a prompt offers: the approver's ceiling, at most <see cref="CeilingSeconds"/>, because it is not bound to a connection (T-34).</summary>
    /// <param name="limits">The approver's limits.</param>
    /// <returns>The grant's length in seconds.</returns>
    public static int GrantSeconds(ApprovalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return Math.Min(limits.MaximumTtlSeconds, CeilingSeconds);
    }

    /// <summary>Whether a live grant answers this request with exactly these names.</summary>
    /// <param name="key">The request's identity.</param>
    /// <param name="keys">The variable names the set holds now.</param>
    /// <param name="remaining">How long the grant has left, when it answers.</param>
    /// <returns>True only for a live grant under <paramref name="key"/> whose names equal <paramref name="keys"/>, in order; a mismatch forgets it.</returns>
    public bool TryUse(string key, IReadOnlyList<string> keys, out TimeSpan remaining)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(keys);

        remaining = TimeSpan.Zero;

        lock (_gate)
        {
            if (_disposed || !_grants.TryGetValue(key, out var grant))
            {
                return false;
            }

            if (grant.Expires.HasExpired(_clock) || !grant.Keys.SequenceEqual(keys, StringComparer.Ordinal))
            {
                Forget(key, grant);
                return false;
            }

            remaining = grant.Expires.Remaining(_clock);
            return true;
        }
    }

    /// <summary>Records a timed grant a person just gave.</summary>
    /// <param name="key">The request's identity.</param>
    /// <param name="project">The project, as the prompt showed it.</param>
    /// <param name="profile">The profile, as the prompt showed it.</param>
    /// <param name="command">The command, as the prompt showed it.</param>
    /// <param name="keys">The variable names the person approved.</param>
    /// <param name="ttl">How long the grant lasts.</param>
    /// <remarks>A cache that has been disposed keeps nothing: its lifetime has ended.</remarks>
    public void Store(string key, string project, string profile, string command, IReadOnlyList<string> keys, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(keys);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_grants.TryGetValue(key, out var replaced))
            {
                Forget(key, replaced);
            }

            var grant = new Grant(project, profile, command, [.. keys], Deadline.Starting(_clock, ttl));
            _grants[key] = grant;
            grant.Expiry = _clock.CreateTimer(_ => Expire(key, grant), null, ttl, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Lists the grants in force, soonest to expire first.</summary>
    /// <returns>Each live grant's names and remaining time; nothing once disposed.</returns>
    public IReadOnlyList<EnvGrantInForce> InForce()
    {
        lock (_gate)
        {
            return _grants
                .Where(pair => !pair.Value.Expires.HasExpired(_clock))
                .Select(pair => new EnvGrantInForce(
                    pair.Key, pair.Value.Project, pair.Value.Profile, pair.Value.Command, pair.Value.Expires.Remaining(_clock)))
                .OrderBy(grant => grant.Remaining)
                .ToList();
        }
    }

    /// <summary>Ends one grant, so the next run it would have answered is asked again.</summary>
    /// <param name="key">The grant, as <see cref="InForce"/> listed it.</param>
    public void Revoke(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            if (_grants.TryGetValue(key, out var grant))
            {
                Forget(key, grant);
            }
        }
    }

    /// <summary>Ends every grant.</summary>
    public void RevokeAll()
    {
        lock (_gate)
        {
            foreach (var (key, grant) in _grants.ToList())
            {
                Forget(key, grant);
            }
        }
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
                grant.Expiry?.Dispose();
            }

            _grants.Clear();
        }
    }

    /// <summary>Forgets the grant a timer was armed for, if it is still the one under that key.</summary>
    private void Expire(string key, Grant armed)
    {
        lock (_gate)
        {
            if (_grants.TryGetValue(key, out var grant) && ReferenceEquals(grant, armed))
            {
                Forget(key, grant);
            }
        }
    }

    private void Forget(string key, Grant grant)
    {
        _grants.Remove(key);
        grant.Expiry?.Dispose();
    }

    private sealed class Grant(string project, string profile, string command, IReadOnlyList<string> keys, Deadline expires)
    {
        internal string Project { get; } = project;

        internal string Profile { get; } = profile;

        internal string Command { get; } = command;

        internal IReadOnlyList<string> Keys { get; } = keys;

        internal Deadline Expires { get; } = expires;

        internal ITimer? Expiry { get; set; }
    }
}
