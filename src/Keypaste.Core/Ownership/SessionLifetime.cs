namespace Keypaste.Core.Ownership;

/// <summary>
/// One unlocked lifetime of a vault's owner, ended by the one transition every kind of lock takes
/// (D-0313).
/// </summary>
/// <remarks>
/// <para>
/// Ending it withdraws everything still waiting on it through <see cref="Ended"/> and <see cref="Waiting"/>,
/// zeroes what it owns, and makes <see cref="TryCommit()"/> false for good. A release commits under the
/// same lock that ends the lifetime, so a release either committed before the lock or is refused by it.
/// </para>
/// <para>
/// <see cref="Withdraw"/> withdraws what is waiting without ending it, as a reload does (D-0412): a request
/// that read <see cref="Waiting"/> before it then never commits, and one that begins after it waits anew.
/// </para>
/// <para>
/// A lock waits for at most one commit check, never for a peer or a person.
/// </para>
/// </remarks>
public sealed class SessionLifetime : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _ended = new();
    private readonly List<IDisposable> _owned = [];
    private CancellationTokenSource _waiting = new();
    private CancellationToken _waitingToken;
    private bool _live = true;

    /// <summary>Starts a lifetime under a fresh session identifier.</summary>
    public SessionLifetime()
        : this(VaultOwner.NewSession())
    {
    }

    /// <summary>Starts a lifetime under a given session identifier.</summary>
    /// <param name="id">The session identifier the endpoint names.</param>
    public SessionLifetime(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        Id = id;
        Ended = _ended.Token;
        _waitingToken = _waiting.Token;
    }

    /// <summary>The session identifier requests name.</summary>
    public string Id { get; }

    /// <summary>Cancelled when the lifetime ends.</summary>
    public CancellationToken Ended { get; }

    /// <summary>
    /// What withdraws a request that begins waiting now: cancelled when the lifetime ends or when
    /// <see cref="Withdraw"/> withdraws everything waiting on it.
    /// </summary>
    /// <remarks>Read once as a request begins, and given back to <see cref="TryCommit(CancellationToken)"/> to release it.</remarks>
    public CancellationToken Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waitingToken;
            }
        }
    }

    /// <summary>Whether the lifetime has not ended.</summary>
    public bool IsLive
    {
        get
        {
            lock (_gate)
            {
                return _live;
            }
        }
    }

    /// <summary>Hands something to the lifetime, which disposes it when it ends.</summary>
    /// <param name="owned">What the lifetime now owns.</param>
    /// <returns><paramref name="owned"/>.</returns>
    /// <remarks>Given to a lifetime that has already ended, it is disposed at once.</remarks>
    public T Own<T>(T owned)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(owned);

        lock (_gate)
        {
            if (_live)
            {
                _owned.Add(owned);
                return owned;
            }
        }

        owned.Dispose();
        return owned;
    }

    /// <summary>Commits a release to this lifetime, if it is still live.</summary>
    /// <returns>False once the lifetime has ended, and then nothing may be released under it.</returns>
    public bool TryCommit()
    {
        lock (_gate)
        {
            return _live;
        }
    }

    /// <summary>Commits a release to this lifetime, if it is still live and nothing withdrew what the request waited on.</summary>
    /// <param name="waiting">The <see cref="Waiting"/> the request read as it began.</param>
    /// <returns>False once the lifetime has ended or <see cref="Withdraw"/> has withdrawn that wait, and then nothing may be released for it.</returns>
    public bool TryCommit(CancellationToken waiting)
    {
        lock (_gate)
        {
            return _live && waiting == _waitingToken;
        }
    }

    /// <summary>
    /// Withdraws every request waiting on the lifetime without ending it, so none asked before now
    /// commits; the session stays, and a request that begins after this waits anew. Doing it on an
    /// ended lifetime does nothing.
    /// </summary>
    public void Withdraw()
    {
        CancellationTokenSource withdrawn;

        lock (_gate)
        {
            if (!_live)
            {
                return;
            }

            withdrawn = _waiting;
            _waiting = new CancellationTokenSource();
            _waitingToken = _waiting.Token;
        }

        // A request still holding the token reads it as cancelled, and linking to it cancels at once.
        using (withdrawn)
        {
            withdrawn.Cancel();
        }
    }

    /// <summary>Ends the lifetime. Doing it twice is not an error.</summary>
    public void End()
    {
        List<IDisposable> owned;
        CancellationTokenSource waiting;

        lock (_gate)
        {
            if (!_live)
            {
                return;
            }

            _live = false;
            owned = [.. _owned];
            _owned.Clear();
            waiting = _waiting;
        }

        waiting.Cancel();
        _ended.Cancel();

        foreach (var item in owned)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        End();
        _ended.Dispose();
        _waiting.Dispose();
    }
}
