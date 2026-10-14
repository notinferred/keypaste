using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;

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
/// <b>History is read from the audit file this app records every answered request in</b>, kept to
/// the records naming this session and rendered by <see cref="AuditText"/> as the activity log reads
/// the whole file (D-0331). A log that is missing is a session with no records yet, as the activity
/// log reads it; one that cannot be read is said to be unavailable, never shown as a session in which
/// nothing happened.
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
    private readonly string _clientsPath;
    private readonly TimeProvider _clock;
    private IReadOnlyList<AuditEntry> _auditEntries = [];
    private (long Length, DateTime Written)? _auditRead;
    private IReadOnlyList<ClientCardRow> _clients = [];
    private string _clientsSignature = string.Empty;
    private string _clientsProblem = string.Empty;
    private readonly Action<Action> _post;
    private readonly Action<string> _toast;
    private readonly ITimer _timer;
    private readonly CancellationTokenSource _closing = new();
    private bool _isConnectOpen;
    private bool _isResetting;
    private AgentSettingsViewModel? _settings;

    private string _status = string.Empty;
    private string _serving = string.Empty;
    private string _unavailable = string.Empty;
    private IReadOnlyList<ActivityRow> _waiting = [];
    private IReadOnlyList<ActivityRow> _grants = [];
    private string _historyHeading = string.Empty;
    private string _history = string.Empty;
    private string _historyMessage = string.Empty;
    private (string? Session, long Length, DateTime Written)? _historyRead;
    private bool _disposed;

    internal AgentActivityViewModel(
        AppAuthority? authority,
        string? home,
        TimeProvider? clock = null,
        Action<Action>? post = null,
        ClientConnector? connector = null,
        Action<string>? toast = null,
        Action? openHistory = null)
    {
        _authority = authority;
        OpenHistoryCommand = new RelayCommand(() => openHistory?.Invoke(), () => openHistory is not null);
        _toast = toast ?? (_ => { });
        Connect = authority is null ? null : new ConnectClientViewModel(authority.Session, connector ?? ClientConnector.ForThisProcess());
        ToggleConnectCommand = new RelayCommand(() => IsConnectOpen = !IsConnectOpen, () => Connect is not null);
        OffersHistory = openHistory is not null;
        _auditPath = KeypasteHome.AuditPath(home);
        _clientsPath = KeypasteHome.ClientsPath(home);
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (run => run());
        ToggleSettingsCommand = new RelayCommand<ClientCardRow>(ToggleSettings, card => card is { HasSettings: true } && _authority is not null);
        ResetClientsCommand = new RelayCommand(() => Resetting = ResetClientsAsync(), () => _clientsProblem.Length > 0 && _authority is not null && !_isResetting);

        RefreshCommand = new RelayCommand(Refresh);
        RevokeCommand = new RelayCommand<ActivityRow>(Revoke, row => row?.Id is not null);
        RevokeAllCommand = new RelayCommand(RevokeAll, () => _grants.Count > 0);

        DependsOn(nameof(HasClientsProblem), nameof(ClientsProblem));
        DependsOn(nameof(HasSettings), nameof(Settings));
        DependsOn(nameof(ConnectIsPrimary), nameof(HasWaiting), nameof(IsConnectOpen));
        DependsOn(nameof(FormsArePrimary), nameof(HasWaiting));
        DependsOn(nameof(IsServing), nameof(Serving));
        DependsOn(nameof(IsAvailable), nameof(Unavailable));
        DependsOn(nameof(IsUnavailable), nameof(IsAvailable));
        DependsOn(nameof(NothingWaiting), nameof(IsAvailable), nameof(Waiting));
        DependsOn(nameof(HasWaiting), nameof(Waiting));
        DependsOn(nameof(NoGrants), nameof(IsAvailable), nameof(Grants));
        DependsOn(nameof(HasGrants), nameof(Grants));
        DependsOn(nameof(HasHistory), nameof(History));
        DependsOn(nameof(HasHistoryMessage), nameof(HistoryMessage));
        DependsOn(ResetClientsCommand, nameof(ClientsProblem), nameof(IsResetting));
        DependsOn(RevokeAllCommand, nameof(Grants));

        Refresh();

        _timer = _clock.CreateTimer(_ => _post(Tick), null, _tick, _tick);
    }

    /// <summary>The MCP clients of this vault; the <c>*</c> card last.</summary>
    internal IReadOnlyList<ClientCardRow> Clients => _clients;

    /// <summary>Opens a card's settings below the cards, or closes them when they are open.</summary>
    internal RelayCommand<ClientCardRow> ToggleSettingsCommand { get; }

    /// <summary>The settings open below the cards, at most one agent's at a time, or null.</summary>
    internal AgentSettingsViewModel? Settings => _settings;

    internal bool HasSettings => _settings is not null;

    /// <summary>
    /// Sets a clients file keypaste cannot parse aside and starts an empty one, once the person confirms, in
    /// the window that confirms any change giving agents more, what each agent then gets.
    /// </summary>
    internal RelayCommand ResetClientsCommand { get; }

    /// <summary>The reset in progress, or a completed task.</summary>
    internal Task Resetting { get; private set; } = Task.CompletedTask;

    /// <summary>Whether a reset is waiting for the person's answer.</summary>
    internal bool IsResetting
    {
        get => _isResetting;
        private set => Set(ref _isResetting, value);
    }

    /// <summary>Why <c>clients.toml</c> cannot be used, or empty. While it is set, every agent request is refused.</summary>
    internal string ClientsProblem
    {
        get => _clientsProblem;
        private set => Set(ref _clientsProblem, value);
    }

    internal bool HasClientsProblem => _clientsProblem.Length > 0;

    /// <summary>Connecting a client to this vault, and checking the connection.</summary>
    internal ConnectClientViewModel? Connect { get; }

    /// <summary>Whether the Connect a client card is open.</summary>
    internal bool IsConnectOpen
    {
        get => _isConnectOpen;
        set => Set(ref _isConnectOpen, value && Connect is not null);
    }

    /// <summary>
    /// Whether Connect client is the screen's primary action: not while a request waits, whose
    /// countdown is the live signal, nor while the connect form shows its own primary (D-0375).
    /// </summary>
    internal bool ConnectIsPrimary => Connect is not null && !HasWaiting && !_isConnectOpen;

    /// <summary>Whether a form's own action may be primary: not while a request waits.</summary>
    internal bool FormsArePrimary => !HasWaiting;

    internal RelayCommand ToggleConnectCommand { get; }

    /// <summary>Agents › History: what agents and tokens asked for, and what came of it.</summary>
    internal RelayCommand OpenHistoryCommand { get; }

    internal bool OffersHistory { get; }

    /// <summary>One true sentence about what agents can do with this vault.</summary>
    internal string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Which process and session answer agents, on one line, or empty when none does.</summary>
    internal string Serving
    {
        get => _serving;
        private set => Set(ref _serving, value);
    }

    internal bool IsServing => _serving.Length > 0;

    /// <summary>Why waiting requests and grants cannot be read, or empty when they were.</summary>
    internal string Unavailable
    {
        get => _unavailable;
        private set => Set(ref _unavailable, value);
    }

    internal bool IsAvailable => _unavailable.Length == 0;

    internal bool IsUnavailable => !IsAvailable;

    /// <summary>The request a person is being asked about; at most one, because the gate asks one at a time.</summary>
    internal IReadOnlyList<ActivityRow> Waiting => _waiting;

    internal bool NothingWaiting => IsAvailable && _waiting.Count == 0;

    internal bool HasWaiting => _waiting.Count > 0;

    /// <summary>The grants in force, soonest to end first.</summary>
    internal IReadOnlyList<ActivityRow> Grants => _grants;

    internal bool NoGrants => IsAvailable && _grants.Count == 0;

    internal bool HasGrants => _grants.Count > 0;

    /// <summary>The audit log history is read from.</summary>
    internal string AuditPath => _auditPath;

    /// <summary>How many of the log's records are this session's, as <see cref="AuditText"/> states it.</summary>
    internal string HistoryHeading => _historyHeading;

    /// <summary>This session's records and their notes, exactly as <see cref="AuditText"/> rendered them.</summary>
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

    /// <summary>The status as the screen says it, with no agent Automatic.</summary>
    internal static string Describe(AuthorityStatus status) => Describe(status, []);

    /// <summary>The status as the screen says it.</summary>
    /// <param name="status">Who answers agents.</param>
    /// <param name="automatic">Each agent released to without asking now, with where, as <see cref="AutomaticAgents.Named"/> names it.</param>
    internal static string Describe(AuthorityStatus status, IReadOnlyList<string> automatic) => status switch
    {
        AuthorityStatus.Serving serving =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"This app (process {serving.Owner.ProcessId}) holds this vault for agents, in session {serving.Session}. ")
            + "MCP clients set up for this vault reach it here and can list the entry names they are allowed to see. "
            + "Each credential request is asked about in a prompt window, and nothing is released unless you allow it, "
            + (automatic.Count == 0
                ? "or you set that agent to Automatic for it in its settings."
                : $"except what these agents are Automatic for: {string.Join("; ", automatic)}."),
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
        Serving = status is AuthorityStatus.Serving answering
            ? string.Create(CultureInfo.InvariantCulture, $"Answering agents from this app · process {answering.Owner.ProcessId} · session {answering.Session}")
            : string.Empty;

        string? session = null;

        if (status is AuthorityStatus.Serving serving)
        {
            var activity = _authority!.Activity;
            session = serving.Session;
            Unavailable = string.Empty;
            _waiting =
            [
                .. activity.Waiting.Select((waiting, i) => ActivityRow.Waiting(i + 1, waiting)),
                .. activity.WaitingRuns.Select((waiting, i) => ActivityRow.WaitingRun(activity.Waiting.Count + i + 1, waiting)),
                .. activity.WaitingEnvs.Select((waiting, i) => ActivityRow.WaitingEnv(activity.Waiting.Count + activity.WaitingRuns.Count + i + 1, waiting)),
            ];
            _grants =
            [
                .. activity.Grants.Select((grant, i) => ActivityRow.Granted(i + 1, grant)),
                .. activity.EnvGrants.Select((grant, i) => ActivityRow.EnvGranted(activity.Grants.Count + i + 1, grant)),
            ];
        }
        else
        {
            Unavailable = "Waiting requests and grants can't be read, because this app is not answering agents for this vault.";
            _waiting = [];
            _grants = [];
        }

        Raise(nameof(Waiting));
        Raise(nameof(Grants));

        ReadHistory(session, forceHistory);
        var caps = ReadClients(forceHistory);

        // With clients.toml unreadable every agent request is refused, so nobody is answered without asking.
        Status = Describe(status, status is AuthorityStatus.Serving && caps is not null ? Automatic(caps) : []);
    }

    /// <summary>
    /// Each agent the vault the app holds releases to without asking now, beneath this computer's
    /// ceilings, with where; read from the open vault, so a change not yet saved is named as it will apply.
    /// </summary>
    private IReadOnlyList<string> Automatic(AccessCaps caps)
    {
        try
        {
            return _authority?.Session.Unlocked is { } vault
                ? AutomaticAgents.Named(new AgentAccessStore(vault).List(), caps, _clock.GetUtcNow())
                : [];
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Builds the client cards from who is attached, this vault's audit lines and <c>clients.toml</c>, keeping the open settings open.</summary>
    /// <returns>This computer's ceilings, or null when <c>clients.toml</c> cannot be read.</returns>
    private AccessCaps? ReadClients(bool force)
    {
        var file = new FileInfo(_auditPath);
        (long, DateTime)? stamp = file.Exists ? (file.Length, file.LastWriteTimeUtc) : null;

        if (force || stamp != _auditRead)
        {
            _auditRead = stamp;
            _auditEntries = stamp is not null && AuditReader.TryRead(_auditPath, out var entries, out _, out _) ? entries : [];
        }

        AccessCaps caps;

        if (AccessCaps.TryLoad(_clientsPath, out var loaded, out var problem))
        {
            caps = loaded;
            ClientsProblem = string.Empty;
        }
        else
        {
            caps = AccessCaps.Empty;
            ClientsProblem = $"{_clientsPath}: {problem}. Every agent request is refused until it is fixed or reset; Reset shows what each agent then gets before anything changes.";
        }

        var now = _clock.GetUtcNow();
        var cards = McpClientCards.Build(
            _authority?.Clients ?? [],
            _auditEntries,
            _authority?.Session.Identity?.Key ?? string.Empty,
            caps,
            now);
        var signature = string.Join('\n', cards.Select(card => $"{card.Key}|{card.Status}|{card.Connections}|{card.LastSeen}"))
            + "\n*|" + ClientsProblem;

        if (!force && string.Equals(signature, _clientsSignature, StringComparison.Ordinal))
        {
            return loaded;
        }

        _clientsSignature = signature;
        _clients =
        [
            .. cards.Select(card => new ClientCardRow(card, now)),
            new ClientCardRow(null, now),
        ];

        foreach (var card in _clients)
        {
            card.IsSettingsOpen = _settings is { } open && string.Equals(card.Label, open.Agent, StringComparison.Ordinal);
        }

        Raise(nameof(Clients));

        if (force)
        {
            _settings?.Read();
        }

        return loaded;
    }

    /// <summary>Opens the named agent's settings below the cards, closing any others, whether or not a card shows it yet.</summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    internal void OpenSettings(string agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (_settings is { } open && string.Equals(open.Agent, agent, StringComparison.Ordinal))
        {
            return;
        }

        if (AgentNames.IsValid(agent, out _))
        {
            ShowSettings(agent);
        }
    }

    /// <summary>Opens a card's settings, closing any others, or closes them when they are open.</summary>
    private void ToggleSettings(ClientCardRow? card)
    {
        if (card?.Label is not { } label || !card.HasSettings)
        {
            return;
        }

        ShowSettings(_settings is { } open && string.Equals(open.Agent, label, StringComparison.Ordinal) ? null : label);
    }

    /// <summary>Shows one agent's settings, or none.</summary>
    private void ShowSettings(string? agent)
    {
        if (_authority is null)
        {
            return;
        }

        _settings?.Dispose();
        _settings = agent is null ? null : new AgentSettingsViewModel(_authority, agent, () => Read(forceHistory: false));

        foreach (var row in _clients)
        {
            row.IsSettingsOpen = agent is not null && string.Equals(row.Label, agent, StringComparison.Ordinal);
        }

        Raise(nameof(Settings));
    }

    /// <summary>Asks the authority to reset the clients file, which the person confirms, then reads it again.</summary>
    private async Task ResetClientsAsync()
    {
        if (_authority is null || _disposed)
        {
            return;
        }

        IsResetting = true;
        string said;

        try
        {
            var reply = await _authority.ResetCapsAsync(_closing.Token).ConfigureAwait(true);
            said = reply.Decision == AuditDecision.Granted
                ? $"Reset: {_clientsPath} was set aside as {Path.GetFileName(reply.Kept ?? _clientsPath + AccessCaps.KeptSuffix)}, and every agent gets what the vault's settings say."
                : $"Not reset: {reply.Reason}.";
        }
        catch (OperationCanceledException)
        {
            said = "Not reset: the screen closed before the reset had an answer.";
        }
        finally
        {
            IsResetting = false;
        }

        if (!_disposed)
        {
            ReadClients(force: true);
            _toast(said);
        }
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
            ["this session"]);

        // No log yet is the first run before any agent asked, as the activity log reads it too.
        if (history.Kind == AuditReadKind.Missing || (history.Kind == AuditReadKind.Intact && history.Entries.Count == 0))
        {
            Show([], "The audit log has no records from this session yet.");
            return;
        }

        Show(history.Lines, history.Kind switch
        {
            AuditReadKind.Unreadable => $"History unavailable: the audit log couldn't be read: {history.Error}",
            AuditReadKind.Unchecked => "History unavailable: the audit log couldn't be checked, so nothing from it is shown here.",
            AuditReadKind.Broken => "This log has been edited since keypaste wrote it. The activity log in Settings › Advanced says where.",
            _ => string.Empty,
        });
    }

    private void Show(IReadOnlyList<string> lines, string message)
    {
        _historyHeading = lines.Count > 0 ? lines[0] : string.Empty;
        _history = string.Join(Environment.NewLine, lines.Skip(1));
        _historyMessage = message;
        Raise(nameof(HistoryHeading));
        Raise(nameof(History));
        Raise(nameof(HistoryMessage));
    }

    private void Revoke(ActivityRow? row)
    {
        if (row?.Id is { } id)
        {
            var ended = _authority?.Revoke(id) == true;
            Read(forceHistory: false);

            if (ended)
            {
                _toast($"Revoked {row.Client}'s grant");
            }
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
        _closing.Cancel();
        _closing.Dispose();
        _timer.Dispose();
        Connect?.Dispose();
        _settings?.Dispose();
    }
}
