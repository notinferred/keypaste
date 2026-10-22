using Keypaste.Core;
using Keypaste.Core.Activity;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;

namespace Keypaste.App.Session;

/// <summary>
/// The picture of what agents did with this vault's entries, read again every few seconds: this
/// vault's granted audit lines, the session's release ledger, and what the authority holds now
/// (D-0361). Never a value.
/// </summary>
/// <remarks>
/// The audit log is read from where the last read stopped, and lines older than
/// <see cref="Horizon"/> are dropped, so a refresh never reads the whole file again.
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
        _post = post ?? (run => run());

        Refresh();
        _timer = clock.CreateTimer(_ => _post(Refresh), null, Interval, Interval);
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

            if (AuditReader.TryReadFrom(_auditPath, _position, out var appended, out var next, out _, out _))
            {
                // A replaced file is read from its start again, which numbers its lines from one again.
                if (appended.Count > 0 && appended[0].Line <= _position.Line)
                {
                    _read.Clear();
                }

                _position = next;
                _read.AddRange(appended);
            }

            _read.RemoveAll(entry => entry.At is not { } at || now - at > Horizon);
            picture = EntryActivity.Build(_read, _vaultKey, _authority.Activity, _authority.Released, now, Reach());
            Current = picture;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What agents can name here: for each attached bridge, named as a screen names its client, the least of its
    /// announced exposure and what its agent's settings let it see, so a bridge registered wide shows only what the owner allows (D-0378).
    /// </summary>
    private List<AgentReach> Reach()
    {
        var reach = new List<AgentReach>();

        foreach (var client in _authority.Clients)
        {
            if (client.Exposure.Count > 0
                && EntryExposure.TryCreate(client.Exposure, out var exposure, out _)
                && _authority.Access(client.Label) is { } access)
            {
                reach.Add(new AgentReach(ConnectedClient.Named(client.Label, client.Name), (name, tags) => access.Reach(exposure, name, tags)));
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
            Current = null;
        }

        _timer.Dispose();
    }
}
