using Keypaste.Core.Audit;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Compacts the owner's audit log under the vault's checkpoint key when the vault is unlocked and then once a day
/// while it stays unlocked. A vault that holds no key yet compacts nothing until a save gives it one.
/// </summary>
/// <remarks>
/// A compaction that cannot run refuses nothing: the log stays as it is, and the next one tries again. It reads the log by its path
/// rather than opening it as a request does, so an unlock alone creates no log.
/// </remarks>
internal sealed class AuditCompactions(string? auditPath, Func<Vault?> vault, VaultIdentity identity, string session, Func<int> keepDays, TimeProvider clock) : IDisposable
{
    internal static readonly TimeSpan Every = TimeSpan.FromDays(1);

    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _disposed;
    private int _running;

    /// <summary>Compacts now, then once a day.</summary>
    internal void Start()
    {
        lock (_gate)
        {
            if (!Volatile.Read(ref _disposed) && _timer is null)
            {
                _timer = clock.CreateTimer(_ => Run(), null, TimeSpan.Zero, Every);
            }
        }
    }

    /// <summary>Compacts the log once, if the vault holds its key.</summary>
    /// <returns>What was done, or null when there was no log's path, no unlocked vault, no key, or a compaction already running.</returns>
    /// <remarks>Never under the gate: the vault is read here, and the owner may hold its own lock while it disposes this.</remarks>
    internal AuditCompactionResult? Run()
    {
        if (Volatile.Read(ref _disposed) || Interlocked.Exchange(ref _running, 1) == 1)
        {
            return null;
        }

        AuditCheckpointKey? key = null;

        try
        {
            if (auditPath is null || vault() is not { } open || AuditCheckpoint.SavedKeyOf(open) is not { } saved)
            {
                return null;
            }

            key = new AuditCheckpointKey(saved, identity.Key);
            return AuditCompaction.Compact(auditPath, key, keepDays(), clock.GetUtcNow(), session);
        }
        catch (ObjectDisposedException)
        {
            // The session ended while it was compacting.
            return null;
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key.Key);
            }

            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        ITimer? timer;

        lock (_gate)
        {
            Volatile.Write(ref _disposed, true);
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }
}
