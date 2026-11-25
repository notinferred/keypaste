using Keypaste.Core;
using Keypaste.Core.Activity;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;

namespace Keypaste.App.Session;

/// <summary>
/// The picture of what agents did with this vault's entries, read again every few seconds: this
/// vault's granted audit lines, the session's release ledger, and what the authority holds now.
/// Never a value.
/// </summary>
/// <remarks>
/// The audit log is read from where the last read stopped, and lines older than
/// <see cref="Horizon"/> are dropped, so a refresh never reads the whole file again. The first read
/// does read the whole file, so with a <c>post</c> it runs on a pool thread and only its picture is posted.
/// </remarks>
internal sealed class EntryActivitySource : IDisposable
{
    /// <summary>How often the picture is rebuilt.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>How far back audit lines are kept.</summary>
    internal static readonly TimeSpan Horizon = TimeSpan.FromDays(30);

    private readonly AppAuthority _authority;
    private readonly string _auditPath;
    private readonly string _vaultKey;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private readonly List<AuditEntry> _read = [];
    private AuditPosition _position = AuditPosition.Start;
    private AuditReleases? _released;
    private bool _disposed;

    /// <param name="authority">What answers agents for this vault.</param>
    /// <param name="auditPath">The audit log.</param>
    /// <param name="vaultKey">The vault's identity key; other vaults' lines are ignored.</param>
    /// <param name="clock">What the interval and the windows are measured on.</param>
    /// <param name="post">Runs an action on the UI thread; null runs it where it is.</param>
    internal EntryActivitySource(
        AppAuthority authority, string auditPath, string vaultKey, TimeProvider clock, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(auditPath);
        ArgumentNullException.ThrowIfNull(vaultKey);
        ArgumentNullException.ThrowIfNull(clock);

        _authority = authority;
        _auditPath = auditPath;
        _vaultKey = vaultKey;
        _clock = clock;

        if (post is null)
        {
            _post = run => run();
            Refresh();
            _timer = clock.CreateTimer(_ => _post(Refresh), null, Interval, Interval);
        }
        else
        {
            // The first read parses the whole log, every vault's lines and refusals, so it must not hold the thread post runs on.
            _post = post;
            _timer = clock.CreateTimer(_ => _post(Refresh), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _ = Task.Run(ReadFirst);
        }
    }

    /// <summary>Raised after the picture was rebuilt, on the thread <c>post</c> runs on.</summary>
    internal event EventHandler? Changed;

    /// <summary>The latest picture, or null before the first read.</summary>
    internal EntryActivity? Current { get; private set; }

    /// <summary>Reads what was appended and rebuilds the picture now.</summary>
    internal void Refresh()
    {
        EntryActivity picture;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = _clock.GetUtcNow();
            var changed = false;

            if (AuditReader.TryReadFrom(_auditPath, _position, out var appended, out var next, out _, out _))
            {
                // A replaced file is read from its start again, which numbers its lines from one again.
                if (appended.Count > 0 && appended[0].Line <= _position.Line)
                {
                    _read.Clear();
                    changed = true;
                }

                _position = next;
                var before = _read.Count;
                _read.AddRange(appended.Where(Usable));
                changed |= _read.Count != before;
            }

            changed |= _read.RemoveAll(entry => entry.At is not { } at || now - at > Horizon) > 0;

            // Cleared before folding again, so a fold that throws is retried next tick instead of a stale one standing in.
            if (changed)
            {
                _released = null;
            }

            _released ??= EntryActivity.Fold(_read, _vaultKey);

            picture = EntryActivity.Build(_released, _authority.Activity, _authority.Released, now, Reach());
            Current = picture;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the whole log off the thread <c>post</c> runs on, then posts the first picture.</summary>
    private void ReadFirst()
    {
        try
        {
            if (AuditReader.TryReadFrom(_auditPath, AuditPosition.Start, out var appended, out var next, out _, out _))
            {
                var usable = appended.Where(Usable).ToList();

                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _position = next;
                        _read.AddRange(usable);
                        _released = null;
                    }
                }
            }
        }
        finally
        {
            // Posted even when the read threw, so the failure surfaces where a refresh's would.
            _post(Start);
        }
    }

    /// <summary>Builds the first picture where <c>post</c> runs it, then starts the interval.</summary>
    private void Start()
    {
        Refresh();

        lock (_gate)
        {
            if (!_disposed)
            {
                _timer.Change(Interval, Interval);
            }
        }
    }

    /// <summary>
    /// Whether the picture can use the line: only this vault's granted releases are held, so other vaults' lines, refusals,
    /// a terminal's edit, an agent's item change and a merge are not kept and walked for a month.
    /// </summary>
    private bool Usable(AuditEntry entry) =>
        entry.Granted
        && (entry.Vault.Length == 0 || string.Equals(entry.Vault, _vaultKey, StringComparison.Ordinal))
        && !string.Equals(entry.Tool, AuditTools.Edit, StringComparison.Ordinal)
        && !string.Equals(entry.Tool, AuditTools.ItemChange, StringComparison.Ordinal)
        && !string.Equals(entry.Tool, AuditTools.Merge, StringComparison.Ordinal);

    /// <summary>
    /// What agents can name here: for each attached bridge, named as a screen names its client, its announced exposure.
    /// </summary>
    private List<AgentReach> Reach()
    {
        var reach = new List<AgentReach>();

        foreach (var client in _authority.Clients)
        {
            if (client.Exposure.Count > 0 && EntryExposure.TryCreate(client.Exposure, out var exposure, out _))
            {
                reach.Add(new AgentReach(ConnectedClient.Named(client.Label, client.Name), exposure.Reach));
            }
        }

        return reach;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _read.Clear();
            _released = null;
            Current = null;
        }

        _timer.Dispose();
    }
}
