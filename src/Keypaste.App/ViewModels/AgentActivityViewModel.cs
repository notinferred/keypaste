using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>
/// What agents meet at this app's unlocked vault: the request waiting for a person, the grants in
/// force and what this session answered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Waiting requests and grants are read from the session authority</b> that answers agents
/// (D-0330), never from the prompt window's events, so the list says what the next request would
/// meet. A revoke is made there too.
/// </para>
/// <para>
/// <b>History is read from the audit file <c>keypaste-mcp</c> wrote</b>, kept to the records naming
/// this session and rendered by <see cref="AuditText"/> as the Log screen renders the whole file
/// (D-0331). A log that is missing or unreadable is said to be unavailable, never shown as a
/// session in which nothing happened.
/// </para>
/// <para>
/// It re-reads the authority every second so the lifetimes count down, and re-reads the file only
/// when its length or write time has moved.
/// </para>
/// </remarks>
internal sealed class AgentActivityViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly AppAuthority? _authority;
    private readonly string _auditPath;
    private readonly Action<Action> _post;
    private readonly ITimer _timer;

    private string _status = string.Empty;
    private string _unavailable = string.Empty;
    private IReadOnlyList<ActivityRow> _waiting = [];
    private IReadOnlyList<ActivityRow> _grants = [];
    private string _history = string.Empty;
    private string _historyMessage = string.Empty;
    private (string? Session, long Length, DateTime Written)? _historyRead;
    private bool _disposed;

    internal AgentActivityViewModel(AppAuthority? authority, string? home, TimeProvider? clock = null, Action<Action>? post = null)
    {
        _authority = authority;
        _auditPath = KeypasteHome.AuditPath(home);
        _post = post ?? (run => run());

        RefreshCommand = new RelayCommand(Refresh);
        RevokeCommand = new RelayCommand<ActivityRow>(Revoke, row => row?.Grant is not null);
        RevokeAllCommand = new RelayCommand(RevokeAll, () => _grants.Count > 0);

        Refresh();

        _timer = (clock ?? TimeProvider.System).CreateTimer(_ => _post(Tick), null, _tick, _tick);
    }

    /// <summary>One true sentence about what agents can do with this vault.</summary>
    internal string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Why waiting requests and grants cannot be read, or empty when they were.</summary>
    internal string Unavailable
    {
        get => _unavailable;
        private set
        {
            if (Set(ref _unavailable, value))
            {
                Raise(nameof(IsAvailable));
                Raise(nameof(IsUnavailable));
            }
        }
    }

    internal bool IsAvailable => _unavailable.Length == 0;

    internal bool IsUnavailable => !IsAvailable;

    /// <summary>The request a person is being asked about; at most one, because the gate asks one at a time.</summary>
    internal IReadOnlyList<ActivityRow> Waiting => _waiting;

    internal bool NothingWaiting => IsAvailable && _waiting.Count == 0;

    /// <summary>The grants in force, soonest to end first.</summary>
    internal IReadOnlyList<ActivityRow> Grants => _grants;

    internal bool NoGrants => IsAvailable && _grants.Count == 0;

    /// <summary>The audit log history is read from.</summary>
    internal string AuditPath => _auditPath;

    /// <summary>This session's records, exactly as <see cref="AuditText"/> rendered them.</summary>
    internal string History => _history;

    internal bool HasHistory => _history.Length > 0;

    /// <summary>What is true of the log as a whole: that it is unavailable, or that its chain is broken.</summary>
    internal string HistoryMessage => _historyMessage;

    internal bool HasHistoryMessage => _historyMessage.Length > 0;

    /// <summary>Reads everything again, the log included.</summary>
    internal RelayCommand RefreshCommand { get; }

    /// <summary>Ends the grant a row lists.</summary>
    internal RelayCommand<ActivityRow> RevokeCommand { get; }

    /// <summary>Ends every grant this session has given.</summary>
    internal RelayCommand RevokeAllCommand { get; }

    /// <summary>Reads the authority and the log again.</summary>
    internal void Refresh() => Read(forceHistory: true);

    /// <summary>The status as the screen says it.</summary>
    internal static string Describe(AuthorityStatus status) => status switch
    {
        AuthorityStatus.Serving serving =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"This app (process {serving.Owner.ProcessId}) holds this vault for agents, in session {serving.Session}. ")
            + "MCP clients set up for this vault reach it here and can list the entry names they are allowed to see. "
            + "Each credential request is asked about in a prompt window, and nothing is released unless you press Approve.",
        AuthorityStatus.NotServing notServing => $"Agents cannot reach this vault: {notServing.Reason}",
        AuthorityStatus.HeldBy held => held.Sentence,
        _ => "Agents cannot reach this vault right now.",
    };

    private void Tick()
    {
        if (!_disposed)
        {
            Read(forceHistory: false);
        }
    }

    private void Read(bool forceHistory)
    {
        var status = _authority?.Status ?? new AuthorityStatus.Locked();
        Status = Describe(status);

        string? session = null;

        if (status is AuthorityStatus.Serving serving)
        {
            var activity = _authority!.Activity;
            session = serving.Session;
            Unavailable = string.Empty;
            _waiting = [.. activity.Waiting.Select((waiting, i) => ActivityRow.Waiting(i + 1, waiting))];
            _grants = [.. activity.Grants.Select((grant, i) => ActivityRow.Granted(i + 1, grant))];
        }
        else
        {
            Unavailable = "Waiting requests and grants can't be read, because this app is not answering agents for this vault.";
            _waiting = [];
            _grants = [];
        }

        Raise(nameof(Waiting));
        Raise(nameof(NothingWaiting));
        Raise(nameof(Grants));
        Raise(nameof(NoGrants));
        RevokeAllCommand.RaiseCanExecuteChanged();

        ReadHistory(session, forceHistory);
    }

    private void ReadHistory(string? session, bool force)
    {
        var file = new FileInfo(_auditPath);
        (string?, long, DateTime) stamp = file.Exists ? (session, file.Length, file.LastWriteTimeUtc) : (session, -1, default);

        if (!force && _historyRead == stamp)
        {
            return;
        }

        _historyRead = stamp;

        if (session is null)
        {
            Show([], "History can't be shown, because this app is not answering agents for this vault.");
            return;
        }

        var history = AuditHistory.Read(
            _auditPath,
            entry => string.Equals(entry.Session, session, StringComparison.Ordinal),
            [$"session {session}"]);

        Show(history.Lines, history.Kind switch
        {
            AuditReadKind.Missing => $"History unavailable: there is no audit log at {_auditPath}.",
            AuditReadKind.Unreadable => $"History unavailable: the audit log couldn't be read: {history.Error}",
            AuditReadKind.Unchecked => "History unavailable: the audit log couldn't be checked, so nothing from it is shown here.",
            AuditReadKind.Broken => "This log has been edited since keypaste wrote it. Verify chain on the Log screen says where.",
            _ => string.Empty,
        });
    }

    private void Show(IReadOnlyList<string> lines, string message)
    {
        _history = string.Join(Environment.NewLine, lines);
        _historyMessage = message;
        Raise(nameof(History));
        Raise(nameof(HasHistory));
        Raise(nameof(HistoryMessage));
        Raise(nameof(HasHistoryMessage));
    }

    private void Revoke(ActivityRow? row)
    {
        if (row?.Grant is { } grant)
        {
            _authority?.Revoke(grant);
            Read(forceHistory: false);
        }
    }

    private void RevokeAll()
    {
        _authority?.RevokeAll();
        Read(forceHistory: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }
}
