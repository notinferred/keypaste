using Keypaste.Core.Agents;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Signs the owner's audit log with the vault's checkpoint key after every <see cref="AuditCheckpoint.Every"/> lines
/// it writes and when its session ends, making the key on the session's first line when the vault holds none.
/// </summary>
/// <remarks>
/// A checkpoint that cannot be written refuses nothing: the lines it would have signed stay unsigned, and the next
/// line the owner writes tries again.
/// </remarks>
internal sealed class AuditCheckpoints(Func<AuditLog?> audit, Func<Vault?> vault, Func<Func<Vault, bool>, AgentSave>? save, VaultIdentity identity, string session)
{
    private readonly Lock _gate = new();
    private AuditLog? _watched;
    private AuditCheckpointKey? _key;
    private int _unsigned;
    private bool _making;
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
        lock (_gate)
        {
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
            fetch = _key is null && !_making && (_unsigned == 1 || _unsigned >= AuditCheckpoint.Every);
            _making |= fetch;
        }

        if (fetch)
        {
            // Outside the gate: a save can merge another program's save, whose line comes back here.
            var key = Key();

            lock (_gate)
            {
                _making = false;

                if (_sealed && key is not null)
                {
                    CryptographicOperations.ZeroMemory(key.Key);
                }
                else
                {
                    _key ??= key;
                }
            }
        }

        lock (_gate)
        {
            if (!_sealed && _unsigned >= AuditCheckpoint.Every)
            {
                TryWrite();
            }
        }
    }

    private AuditCheckpointKey? Key()
    {
        try
        {
            if (AuditCheckpointKey.Read(vault(), identity) is { } held)
            {
                return held;
            }

            return save is not null && save(AuditCheckpoint.WriteNewKey).Holds ? AuditCheckpointKey.Read(vault(), identity) : null;
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
