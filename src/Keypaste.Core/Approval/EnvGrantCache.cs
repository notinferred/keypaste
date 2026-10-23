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
    private long _generation;
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

    /// <summary>
    /// How many times grants were ended for a change, by Revoke all, ending an agent's, an edit or a reload:
    /// read before a person is asked and given to <see cref="Store"/>, which keeps no grant asked for before one.
    /// </summary>
    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
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
    /// <param name="generation">The <see cref="Generation"/> read before the person was asked.</param>
    /// <param name="client">The agent client it is given to, as the prompt showed it, or null for <c>keypaste run --session</c>.</param>
    /// <param name="label">The bridge's raw <c>--client-label</c>, or null.</param>
    /// <param name="entries">The entries it releases, or null for each key of <paramref name="project"/>'s <paramref name="profile"/>.</param>
    /// <returns>Whether the grant is kept.</returns>
    /// <remarks>
    /// A cache that has been disposed keeps nothing: its lifetime has ended. Nor does one whose grants
    /// were ended while the person was asked, so an answer given before Revoke all, ending an agent's
    /// grants, an edit or a reload leaves no grant after it.
    /// </remarks>
    public bool Store(
        string key,
        string project,
        string profile,
        string command,
        IReadOnlyList<string> keys,
        TimeSpan ttl,
        long generation,
        string? client = null,
        string? label = null,
        IReadOnlyList<EntryName>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(keys);

        IReadOnlyList<EntryName> released = entries
            ?? [.. keys.Select(name => new EntryName(EnvProfileNames.SetName(project, profile), name))];

        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return false;
            }

            if (_grants.TryGetValue(key, out var replaced))
            {
                Forget(key, replaced);
            }

            var grant = new Grant(project, profile, command, [.. keys], Deadline.Starting(_clock, ttl))
            {
                Client = client,
                Label = label,
                Entries = [.. released],
            };
            _grants[key] = grant;
            grant.Expiry = _clock.CreateTimer(_ => Expire(key, grant), null, ttl, Timeout.InfiniteTimeSpan);
            return true;
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
                .Select(pair => Listed(pair.Key, pair.Value))
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

    /// <summary>Ends every grant whose key starts with <paramref name="prefix"/>, such as every run grant of one connection.</summary>
    /// <param name="prefix">The start of the keys to end.</param>
    public void RevokePrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        RevokeWhere((key, _) => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Ends every grant given to one agent, and keeps none a person is being asked about now.</summary>
    /// <param name="agent">Whether a grant, as <see cref="InForce"/> lists it, was given to the agent.</param>
    /// <returns>How many live grants ended.</returns>
    /// <remarks>
    /// It advances <see cref="Generation"/> under the same lock, so no grant stored before it outlives it and
    /// no timed allow asked about before it is kept after it, for this agent or, since the count is the
    /// cache's, any other (D-0516).
    /// </remarks>
    public int RevokeAgent(Func<EnvGrantInForce, bool> agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        lock (_gate)
        {
            _generation++;

            var ended = _grants
                .Where(pair => !pair.Value.Expires.HasExpired(_clock) && agent(Listed(pair.Key, pair.Value)))
                .ToList();

            foreach (var (key, grant) in ended)
            {
                Forget(key, grant);
            }

            return ended.Count;
        }
    }

    /// <summary>Ends every grant releasing an entry an edit touched, so no value the vault no longer holds there is served again (D-0318).</summary>
    /// <param name="edit">What the edit touched.</param>
    /// <remarks>It advances <see cref="Generation"/>, so no grant a person is being asked about now is kept either.</remarks>
    public void RevokeEntries(VaultEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var touched = edit.Entries.ToHashSet();

        lock (_gate)
        {
            _generation++;

            foreach (var (key, grant) in _grants.Where(pair => edit.IsEverything || pair.Value.Entries.Any(touched.Contains)).ToList())
            {
                Forget(key, grant);
            }
        }
    }

    /// <summary>Ends every grant, and keeps none a person is being asked about now.</summary>
    public void RevokeAll()
    {
        lock (_gate)
        {
            _generation++;

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

    private void RevokeWhere(Func<string, Grant, bool> ends)
    {
        lock (_gate)
        {
            foreach (var (key, grant) in _grants.Where(pair => ends(pair.Key, pair.Value)).ToList())
            {
                Forget(key, grant);
            }
        }
    }

    private EnvGrantInForce Listed(string key, Grant grant) =>
        new(key, grant.Project, grant.Profile, grant.Command, grant.Expires.Remaining(_clock))
        {
            Client = grant.Client,
            Label = grant.Label,
            Entries = [.. grant.Entries.Select(ApprovalPrompt.Shown)],
        };

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

        internal string? Client { get; init; }

        internal string? Label { get; init; }

        internal IReadOnlyList<EntryName> Entries { get; init; } = [];
    }
}
