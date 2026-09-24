namespace Keypaste.Core.Ownership;

/// <summary>
/// One unlocked lifetime of a vault's owner, ended by the one transition every kind of lock takes
/// (D-0313).
/// </summary>
/// <remarks>
/// <para>
/// Ending it withdraws everything still waiting on it through <see cref="Ended"/>, zeroes what it
/// owns, and makes <see cref="TryCommit"/> false for good. A release commits under the same lock
/// that ends the lifetime, so a release either committed before the lock or is refused by it.
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
    }

    /// <summary>The session identifier requests name.</summary>
    public string Id { get; }

    /// <summary>Cancelled when the lifetime ends.</summary>
    public CancellationToken Ended { get; }

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

    /// <summary>Ends the lifetime. Doing it twice is not an error.</summary>
    public void End()
    {
        List<IDisposable> owned;

        lock (_gate)
        {
            if (!_live)
            {
                return;
            }

            _live = false;
            owned = [.. _owned];
            _owned.Clear();
        }

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
    }
}
