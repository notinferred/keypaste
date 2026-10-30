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
/// <see cref="Withdraw(SessionWithdrawal)"/> withdraws what is waiting without ending it, as a reload does (D-0412) and a
/// merge with another program's save (D-0542): a request that read <see cref="Waiting"/> before it then never commits,
/// and one that begins after it waits anew. It first runs what <see cref="OnWithdraw"/> was given, so the owner's grants
/// keep no allowance from a request asked before it.
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
    private readonly List<Action> _onWithdraw = [];
    private CancellationTokenSource _waiting = new();
    private CancellationToken _waitingToken;
    private SessionWithdrawal _withdrawal;
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
    /// <see cref="Withdraw(SessionWithdrawal)"/> withdraws everything waiting on it.
    /// </summary>
    /// <remarks>Read once as a request begins, and given back to <see cref="TryCommitAfter(CancellationToken)"/> to release it.</remarks>
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

    /// <summary>How a withdrawn request's refusal names the latest withdrawal: the reload or the merge that replaced the copy it waited on.</summary>
    /// <remarks>Read once a request finds itself withdrawn, which follows the withdrawal at once, while an owner makes one at a time.</remarks>
    internal string WithdrawnBecause
    {
        get
        {
            lock (_gate)
            {
                return _withdrawal == SessionWithdrawal.Merge
                    ? "the vault was merged with another program's save"
                    : "the vault was reloaded from its file";
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

    /// <summary>Runs <paramref name="withdrawing"/> each time <see cref="Withdraw(SessionWithdrawal)"/> withdraws what is waiting, before it does, until the lifetime ends.</summary>
    /// <param name="withdrawing">What a withdrawal also does, such as ending every grant.</param>
    /// <remarks>Given to a lifetime that has already ended, it is never run.</remarks>
    public void OnWithdraw(Action withdrawing)
    {
        ArgumentNullException.ThrowIfNull(withdrawing);

        lock (_gate)
        {
            if (_live)
            {
                _onWithdraw.Add(withdrawing);
            }
        }
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
    /// <returns>False once the lifetime has ended or <see cref="Withdraw(SessionWithdrawal)"/> has withdrawn that wait, and then nothing may be released for it.</returns>
    public bool TryCommitAfter(CancellationToken waiting)
    {
        lock (_gate)
        {
            return _live && waiting == _waitingToken;
        }
    }

    /// <summary>Withdraws every request waiting on the lifetime as a reload does.</summary>
    public void Withdraw() => Withdraw(SessionWithdrawal.Reload);

    /// <summary>
    /// Withdraws every request waiting on the lifetime without ending it, so none asked before now
    /// commits; the session stays, and a request that begins after this waits anew. Doing it on an
    /// ended lifetime does nothing.
    /// </summary>
    /// <param name="cause">What replaces the copy those requests waited on, which their refusals name.</param>
    public void Withdraw(SessionWithdrawal cause)
    {
        CancellationTokenSource withdrawn;
        List<Action> withdrawing;

        lock (_gate)
        {
            if (!_live)
            {
                return;
            }

            withdrawing = [.. _onWithdraw];
        }

        // Before the wait is replaced, so an answer that lands meanwhile finds its grant already refused.
        foreach (var action in withdrawing)
        {
            action();
        }

        lock (_gate)
        {
            if (!_live)
            {
                return;
            }

            withdrawn = _waiting;
            _waiting = new CancellationTokenSource();
            _waitingToken = _waiting.Token;
            _withdrawal = cause;
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
            _onWithdraw.Clear();
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

/// <summary>What withdraws the requests waiting on a lifetime that goes on.</summary>
public enum SessionWithdrawal
{
    /// <summary>A reload put what the file holds in place of the open copy (D-0412).</summary>
    Reload = 0,

    /// <summary>A merge with another program's save is putting the merged copy in place of the open copy (D-0542).</summary>
    Merge = 1,
}
