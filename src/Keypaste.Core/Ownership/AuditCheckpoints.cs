using Keypaste.Core.Audit;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Signs the owner's audit log with the vault's checkpoint key after every <see cref="AuditCheckpoint.Every"/> lines
/// it writes and when its session ends. A vault that holds no key is given one by its next save, never by a save of
/// its own, and until that save the lines stay unsigned.
/// </summary>
/// <remarks>
/// A checkpoint that cannot be written refuses nothing: the lines it would have signed stay unsigned, and the next
/// line the owner writes tries again.
/// </remarks>
internal sealed class AuditCheckpoints(Func<AuditLog?> audit, Func<Vault?> vault, VaultIdentity identity, string session)
{
    private readonly Lock _gate = new();
    private AuditLog? _watched;
    private AuditCheckpointKey? _key;
    private int _unsigned;
    private bool _sealed;

    /// <summary>The owner's log, watched for the lines written to it.</summary>
    internal AuditLog? Watched()
    {
        var log = audit();

        lock (_gate)
        {
            if (log is not null && !_sealed && !ReferenceEquals(log, _watched))
            {
                if (_watched is not null)
                {
                    _watched.AppendFinished -= Appended;
                }

                log.AppendFinished += Appended;
                _watched = log;
            }
        }

        return log;
    }

    /// <summary>Signs what the session wrote since its last checkpoint, then stops; the session's vault is no longer needed.</summary>
    internal void Seal()
    {
        bool fetch;

        lock (_gate)
        {
            fetch = !_sealed && _key is null && _unsigned > 0;
        }

        var key = fetch ? Key() : null;

        lock (_gate)
        {
            Keep(key);

            if (_sealed)
            {
                return;
            }

            if (_unsigned > 0)
            {
                TryWrite();
            }

            _sealed = true;

            if (_watched is not null)
            {
                _watched.AppendFinished -= Appended;
            }

            if (_key is not null)
            {
                CryptographicOperations.ZeroMemory(_key.Key);
            }
        }
    }

    private void Appended(string? failure)
    {
        if (failure is not null)
        {
            return;
        }

        bool fetch;

        lock (_gate)
        {
            if (_sealed)
            {
                return;
            }

            _unsigned++;
            fetch = _key is null;
        }

        // Read outside the gate, which the vault's owner may be waiting on while it holds its own.
        var key = fetch ? Key() : null;

        lock (_gate)
        {
            Keep(key);

            if (!_sealed && _unsigned >= AuditCheckpoint.Every)
            {
                TryWrite();
            }
        }
    }

    /// <summary>Takes a key read for this session, unless one is held already or the session has ended. Call under the gate.</summary>
    private void Keep(AuditCheckpointKey? key)
    {
        if (key is null)
        {
            return;
        }

        if (_sealed || _key is not null)
        {
            CryptographicOperations.ZeroMemory(key.Key);
            return;
        }

        _key = key;
    }

    /// <summary>The key the vault's file holds; when the open vault holds none, its next save is asked to write one.</summary>
    private AuditCheckpointKey? Key()
    {
        try
        {
            if (vault() is not { } open)
            {
                return null;
            }

            if (AuditCheckpoint.KeyOf(open) is null)
            {
                open.BeforeEachSave = AuditCheckpoint.WriteNewKey;
                return null;
            }

            return AuditCheckpoint.SavedKeyOf(open) is { } saved ? new AuditCheckpointKey(saved, identity.Key) : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private void TryWrite()
    {
        try
        {
            if (_key is { } key && _watched is { } log && log.TryCheckpoint(key, session, out _))
            {
                _unsigned = 0;
            }
        }
        catch (ObjectDisposedException)
        {
            // The owner closed its log as the session ended.
        }
    }
}
