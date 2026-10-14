using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Agents: the request waiting for the person, the allowances in force, the AI tools on this computer
/// and the agents that asked, each with its settings, and what was recorded since the app was unlocked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Waiting requests and allowances are read from the session authority</b> that answers agents
/// (D-0330), never from the prompt window's events, so the list says what the next request would
/// meet. Nothing here answers a request: Show the prompt brings its window forward (D-0456), and End
/// ends an allowance there.
/// </para>
/// <para>
/// <b>The AI tools are looked for in the background</b> each time the screen opens, as
/// <c>keypaste setup</c> finds them, and again once a Connect or Disconnect has run (D-0455). Each
/// row is kept by its key, updated in place and left where it is listed, so an open Settings disclosure,
/// its unsaved edits, an open pane and the focus or open drop-down in them survive the refresh; so do a
/// waiting request's and an allowance's buttons. One Connect pane is open at a time, and while its plan
/// or check runs no other Connect, Connect again or Disconnect can start.
/// </para>
/// <para>
/// <b>History is read from the audit file this app records every answered request in</b>, kept to
/// the records since this unlock and rendered by <see cref="AuditText"/> as the activity log reads
/// the whole file (D-0331). A log that is missing has no records yet, as the activity log reads it; one
/// that cannot be read is said to be unavailable, never shown as an unlock in which nothing happened.
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
    private readonly ClientConnector? _connector;
    private readonly string _auditPath;
    private readonly string _clientsPath;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, AgentRow> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opened = new(StringComparer.Ordinal);
    private IReadOnlyList<AuditEntry> _auditEntries = [];
    private (long Length, DateTime Written)? _auditRead;
    private readonly ObservableCollection<AgentRow> _rows = [];
    private IReadOnlyList<McpToolFinding> _findings = [];
    private string _clientsSignature = string.Empty;
    private string _clientsProblem = string.Empty;
    private readonly Action<Action> _post;
    private readonly Action<string> _toast;
    private readonly ITimer _timer;
    private readonly CancellationTokenSource _closing = new();
    private bool _isResetting;
    private bool _isLooking;
    private bool _looked;
    private int _looks;
    private AgentRow? _connecting;

    private string _status = string.Empty;
    private string _serving = string.Empty;
    private string _unavailable = string.Empty;
    private readonly ObservableCollection<ActivityRow> _waiting = [];
    private readonly ObservableCollection<ActivityRow> _grants = [];
    private string _historyHeading = string.Empty;
    private string _history = string.Empty;
    private string _historyMessage = string.Empty;
    private (string? Session, long Length, DateTime Written)? _historyRead;
    private bool _disposed;

    /// <param name="authority">The session authority that answers agents, or null when nothing here does.</param>
    /// <param name="home">The keypaste home whose audit log and <c>clients.toml</c> are read.</param>
    /// <param name="clock">What lifetimes count down on.</param>
    /// <param name="post">Runs a tick or a finished look on the UI thread.</param>
    /// <param name="connector">What finding and connecting AI tools reaches, or null to look for none.</param>
    /// <param name="toast">Says what a press did.</param>
    /// <param name="openHistory">Opens Agents › History.</param>
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
        _connector = authority is null ? null : connector;
        OpenHistoryCommand = new RelayCommand(() => openHistory?.Invoke(), () => openHistory is not null);
        _toast = toast ?? (_ => { });
        OffersHistory = openHistory is not null;
        _auditPath = KeypasteHome.AuditPath(home);
        _clientsPath = KeypasteHome.ClientsPath(home);
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (run => run());
        ResetClientsCommand = new RelayCommand(() => Resetting = ResetClientsAsync(), () => _clientsProblem.Length > 0 && _authority is not null && !_isResetting);
        ConnectCommand = new RelayCommand<AgentRow>(row => Open(row, removes: false), row => row is { OffersConnect: true } && CanConnect);
        ConnectAgainCommand = new RelayCommand<AgentRow>(row => Open(row, removes: false), row => row is { OffersConnectAgain: true } && CanConnect);
        DisconnectCommand = new RelayCommand<AgentRow>(row => Open(row, removes: true), row => row is { OffersDisconnect: true } && CanConnect);
        CloseConnectionCommand = new RelayCommand(CloseConnection, () => Connection is { IsBusy: false });

        RefreshCommand = new RelayCommand(Refresh);
        ShowPromptCommand = new RelayCommand(ShowPrompt, () => HasWaiting);
        EndCommand = new RelayCommand<ActivityRow>(End, row => row?.Id is not null);
        EndAllCommand = new RelayCommand(EndAll, () => _grants.Count > 0);

        DependsOn(nameof(HasClientsProblem), nameof(ClientsProblem));
        DependsOn(nameof(FormsArePrimary), nameof(HasWaiting));
        DependsOn(nameof(IsServing), nameof(Serving));
        DependsOn(nameof(IsAvailable), nameof(Unavailable));
        DependsOn(nameof(IsUnavailable), nameof(IsAvailable));
        DependsOn(nameof(NothingWaiting), nameof(IsAvailable), nameof(HasWaiting));
        DependsOn(nameof(NoGrants), nameof(IsAvailable), nameof(HasGrants));
        DependsOn(nameof(HasHistory), nameof(History));
        DependsOn(nameof(HasHistoryMessage), nameof(HistoryMessage));
        DependsOn(nameof(HasNoTools), nameof(IsLooking));
        DependsOn(ResetClientsCommand, nameof(ClientsProblem), nameof(IsResetting));
        DependsOn(ShowPromptCommand, nameof(HasWaiting));
        DependsOn(EndAllCommand, nameof(HasGrants));
        DependsOn(CloseConnectionCommand, nameof(Connection));

        Refresh();
        Look();

        _timer = _clock.CreateTimer(_ => _post(Tick), null, _tick, _tick);
    }

    /// <summary>The AI tools found and the agents of this vault, Every other agent last; a row stays where it was first listed while the screen is open.</summary>
    internal IReadOnlyList<AgentRow> Rows => _rows;

    /// <summary>Whether the tools on this computer are being looked for.</summary>
    internal bool IsLooking
    {
        get => _isLooking;
        private set => Set(ref _isLooking, value);
    }

    /// <summary>The look in progress or the last one, finished once what it found is posted.</summary>
    internal Task Looking { get; private set; } = Task.CompletedTask;

    /// <summary>Whether the last look found none of the tools keypaste connects.</summary>
    internal bool HasNoTools => _connector is not null && _looked && !_isLooking && _findings.Count == 0;

    /// <summary>The tools keypaste looks for on this computer, named when none was found.</summary>
    internal string NoTools => _connector is null
        ? string.Empty
        : $"None of the AI tools keypaste connects was found on this computer: {string.Join(", ", McpClientCatalog.For(_connector.Machine.Platform).Select(tool => tool.DisplayName))}.";

    /// <summary>Opens a found tool's Connect pane under its row.</summary>
    internal RelayCommand<AgentRow> ConnectCommand { get; }

    /// <summary>Opens a connected tool's Connect pane again, for a moved vault or app.</summary>
    internal RelayCommand<AgentRow> ConnectAgainCommand { get; }

    /// <summary>Opens the pane that disconnects a tool.</summary>
    internal RelayCommand<AgentRow> DisconnectCommand { get; }

    /// <summary>Closes the open pane, unless its plan or check is running.</summary>
    internal RelayCommand CloseConnectionCommand { get; }

    /// <summary>The Connect or Disconnect pane open under a row, or null.</summary>
    internal ConnectClientViewModel? Connection => _connecting?.Connection;

    /// <summary>The open pane composing its plan, or a completed task.</summary>
    internal Task Opening { get; private set; } = Task.CompletedTask;

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

    /// <summary>Whether a pane's own action may be primary: not while a request waits, whose countdown is the live signal (D-0375).</summary>
    internal bool FormsArePrimary => !HasWaiting;

    /// <summary>Agents › History: what agents and tokens asked for, and what came of it.</summary>
    internal RelayCommand OpenHistoryCommand { get; }

    internal bool OffersHistory { get; }

    /// <summary>One true sentence about what agents can do with this vault.</summary>
    internal string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Which process and session answer agents, as the line after "Answering agents from this app" names them, or empty when none does.</summary>
    internal string Serving
    {
        get => _serving;
        private set => Set(ref _serving, value);
    }

    internal bool IsServing => _serving.Length > 0;

    /// <summary>Why waiting requests and allowances cannot be read, or empty when they were.</summary>
    internal string Unavailable
    {
        get => _unavailable;
        private set => Set(ref _unavailable, value);
    }

    internal bool IsAvailable => _unavailable.Length == 0;

    internal bool IsUnavailable => !IsAvailable;

    /// <summary>What the person is being asked about; at most one, because the gate asks one at a time.</summary>
    internal IReadOnlyList<ActivityRow> Waiting => _waiting;

    internal bool NothingWaiting => IsAvailable && _waiting.Count == 0;

    internal bool HasWaiting => _waiting.Count > 0;

    /// <summary>The allowances in force, soonest to end first.</summary>
    internal IReadOnlyList<ActivityRow> Grants => _grants;

    internal bool NoGrants => IsAvailable && _grants.Count == 0;

    internal bool HasGrants => _grants.Count > 0;

    /// <summary>The audit log history is read from.</summary>
    internal string AuditPath => _auditPath;

    /// <summary>How many of the log's records are since this unlock, as <see cref="AuditText"/> states it.</summary>
    internal string HistoryHeading => _historyHeading;

    /// <summary>The records since this unlock and their notes, exactly as <see cref="AuditText"/> rendered them.</summary>
    internal string History => _history;

    internal bool HasHistory => _history.Length > 0;

    /// <summary>What is true of the log as a whole: that it is unavailable, or that its chain is broken.</summary>
    internal string HistoryMessage => _historyMessage;

    internal bool HasHistoryMessage => _historyMessage.Length > 0;

    /// <summary>Reads everything again, the log included.</summary>
    internal RelayCommand RefreshCommand { get; }

    /// <summary>Brings the waiting prompt's window forward with Deny focused and its allows disarmed for a second.</summary>
    internal RelayCommand ShowPromptCommand { get; }

    /// <summary>Ends the allowance a row lists, so the next matching request asks again.</summary>
    internal RelayCommand<ActivityRow> EndCommand { get; }

    /// <summary>Ends every allowance given since this unlock.</summary>
    internal RelayCommand EndAllCommand { get; }

    private bool CanConnect => _connector is not null && _authority is not null && Connection is not { IsBusy: true };

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
                $"This app (process {serving.Owner.ProcessId}, unlock {serving.Session}) holds this vault for agents while it is unlocked. ")
            + "AI tools connected to this vault reach it here and can list the item names they are allowed to see. "
            + "Each credential request is asked about in a prompt window, and nothing is released unless you allow it, "
            + (automatic.Count == 0
                ? "or you set that agent to Automatic for it in its settings."
                : $"except what these agents are Automatic for: {string.Join("; ", automatic)}."),
        AuthorityStatus.NotServing notServing => $"Agents cannot reach this vault: {notServing.Reason}",
        AuthorityStatus.HeldBy held => held.Sentence,
        _ => "Agents cannot reach this vault right now.",
    };

    /// <summary>Opens the named agent's settings on its row, listing it first when no row shows it yet.</summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    internal void OpenSettings(string agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!AgentNames.IsValid(agent, out _))
        {
            return;
        }

        if (!_byKey.ContainsKey(agent) && _opened.Add(agent))
        {
            ReadClients(force: false);
        }

        if (_byKey.TryGetValue(agent, out var row))
        {
            row.IsOpen = true;
        }
    }

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
            ? string.Create(CultureInfo.InvariantCulture, $"process {answering.Owner.ProcessId} · unlock {answering.Session}")
            : string.Empty;

        string? session = null;
        List<ActivityRow> waiting = [];
        List<ActivityRow> grants = [];

        if (status is AuthorityStatus.Serving serving)
        {
            var activity = _authority!.Activity;
            session = serving.Session;
            Unavailable = string.Empty;
            var asked = activity.Waiting.Count;
            var run = asked + activity.WaitingRuns.Count;
            var env = run + activity.WaitingEnvs.Count;
            waiting =
            [
                .. activity.Waiting.Select((request, i) => ActivityRow.Waiting(i + 1, request)),
                .. activity.WaitingRuns.Select((request, i) => ActivityRow.WaitingRun(asked + i + 1, request)),
                .. activity.WaitingEnvs.Select((request, i) => ActivityRow.WaitingEnv(run + i + 1, request)),
                .. activity.WaitingChanges.Select((request, i) => ActivityRow.WaitingChange(env + i + 1, request)),
            ];
            grants =
            [
                .. activity.Grants.Select((grant, i) => ActivityRow.Granted(i + 1, grant)),
                .. activity.EnvGrants.Select((grant, i) => ActivityRow.EnvGranted(activity.Grants.Count + i + 1, grant)),
            ];
        }
        else
        {
            Unavailable = "Waiting requests and allowances can't be read, because this app is not answering agents for this vault.";
        }

        Relist(_waiting, waiting);
        Relist(_grants, grants);
        Raise(nameof(HasWaiting));
        Raise(nameof(HasGrants));

        ReadHistory(session, forceHistory);
        var (caps, records) = ReadClients(forceHistory);

        // With clients.toml unreadable every agent request is refused, so nobody is answered without asking.
        Status = Describe(status, status is AuthorityStatus.Serving && caps is not null ? AutomaticAgents.Named(records, caps, _clock.GetUtcNow()) : []);
    }

    /// <summary>The records of the vault the app holds, read from the open vault, so a change not yet saved is named as it will apply.</summary>
    private IReadOnlyList<AgentRecord> Records()
    {
        try
        {
            return _authority?.Session.Unlocked is { } vault ? new AgentAccessStore(vault).List() : [];
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Builds the rows from the tools found, who is attached, this vault's audit lines, its records and
    /// <c>clients.toml</c>, keeping each row that was already listed.
    /// </summary>
    /// <returns>This computer's ceilings, or null when <c>clients.toml</c> cannot be read; and the vault's records.</returns>
    private (AccessCaps? Caps, IReadOnlyList<AgentRecord> Records) ReadClients(bool force)
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

        var records = Records();
        var now = _clock.GetUtcNow();

        // A row whose settings or pane are open is built from its name, so it stays listed, and current, until they close.
        var open = _byKey.Values.Where(row => row.IsOpen || row.HasConnection).Select(row => row.Label).OfType<string>();
        var cards = McpClientCards.Build(
            _authority?.Clients ?? [],
            _auditEntries,
            _authority?.Session.Identity?.Key ?? string.Empty,
            caps,
            _findings,
            now,
            [.. records.Select(record => record.Name), .. _opened, .. open]);
        List<AgentRow> listed = [];

        foreach (var card in cards)
        {
            if (_byKey.TryGetValue(card.Key, out var row))
            {
                row.Update(card, now);
            }
            else
            {
                row = new AgentRow(card, now, OpenAgentSettings);
                _byKey[card.Key] = row;
            }

            listed.Add(row);
        }

        Place(listed);

        foreach (var (key, row) in _byKey.Where(pair => !listed.Contains(pair.Value)).ToList())
        {
            _byKey.Remove(key);
            row.Dispose();
        }

        // What each summary is read from: the records' content, not only their number, and the two files beneath them.
        var signature = string.Join(
            '\n',
            cards.Select(card => $"{card.Key}|{card.Presence}")
                .Concat(records.Select(record => $"{record.Name}|{record.Record.Understood}|{AcafRecord.Write(record.Record.Access)}"))
                .Append($"*|{ClientsProblem}|{Stamp(_clientsPath)}|{Stamp(_authority?.Session.Unlocked?.Path)}"));

        if (force || !string.Equals(signature, _clientsSignature, StringComparison.Ordinal))
        {
            _clientsSignature = signature;
            Summarize();
        }

        if (force)
        {
            foreach (var row in _rows)
            {
                row.ReadSettings();
            }
        }

        return (ClientsProblem.Length == 0 ? loaded : null, records);
    }

    /// <summary>
    /// Lists the rows built, leaving each row already listed where it is, so its controls, and the focus or open
    /// drop-down in them, are never made again; a new row goes after the one built before it.
    /// </summary>
    private void Place(List<AgentRow> listed)
    {
        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            if (!listed.Contains(_rows[i]))
            {
                _rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < listed.Count; i++)
        {
            if (!_rows.Contains(listed[i]))
            {
                _rows.Insert(i == 0 ? 0 : _rows.IndexOf(listed[i - 1]) + 1, listed[i]);
            }
        }
    }

    /// <summary>Lists the rows read in their order, updating each one already listed in place, so its buttons are not made again.</summary>
    private static void Relist(ObservableCollection<ActivityRow> shown, List<ActivityRow> read)
    {
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            var key = shown[i].Key;

            if (!read.Exists(row => string.Equals(row.Key, key, StringComparison.Ordinal)))
            {
                shown.RemoveAt(i);
            }
        }

        for (var i = 0; i < read.Count; i++)
        {
            var at = i;

            while (at < shown.Count && !string.Equals(shown[at].Key, read[i].Key, StringComparison.Ordinal))
            {
                at++;
            }

            if (at == shown.Count)
            {
                shown.Insert(i, read[i]);
                continue;
            }

            if (at != i)
            {
                shown.Move(at, i);
            }

            shown[i].Update(read[i]);
        }
    }

    private static string Stamp(string? path) =>
        path is not null && new FileInfo(path) is { Exists: true } file
            ? string.Create(CultureInfo.InvariantCulture, $"{file.Length}@{file.LastWriteTimeUtc.Ticks}")
            : "-";

    /// <summary>Says on each row that has settings what they give it, as its panel reads them.</summary>
    private void Summarize()
    {
        foreach (var row in _rows)
        {
            row.Summary = row.Label is { } label && _authority is not null && _authority.TrySettings(label, out var state, out _)
                ? Summary(state)
                : string.Empty;
        }
    }

    /// <summary>An agent's settings in a line: its mode, each project row, what it sees, values and commands, and what holds them back.</summary>
    private static string Summary(AgentSettingsState state)
    {
        var access = state.Vault;
        List<string> parts = [Words(access.Mode), .. access.Projects.Select(row => $"{Words(row.Mode)} for {row.Scope.Name}")];
        parts.Add(access.See.Globs switch
        {
            var globs when globs.Contains(McpServerRegistration.WideExposure, StringComparer.Ordinal) => "sees every item",
            var globs when globs.Contains(EntryExposure.DefaultGlob, StringComparer.Ordinal) => "sees all projects",
            _ => "sees only what you picked",
        });

        if (!access.Values)
        {
            parts.Add("values only into commands it runs");
        }

        if (access.Run)
        {
            parts.Add("may run commands");
        }

        if (state.BeneathDefaults)
        {
            parts.Add("the defaults apply until its saved settings can be read");
        }
        else if (state.HeldBack.Count > 0)
        {
            parts.Add("less on this computer");
        }

        return string.Join(" · ", parts);
    }

    private static string Words(AgentMode mode) => mode switch
    {
        AgentMode.Ask => "Ask every time",
        AgentMode.Automatic => "Automatic",
        _ => "Ask, allow for a while",
    };

    /// <summary>The panel a row's Settings opens, which re-reads the screen after each change.</summary>
    private AgentSettingsViewModel? OpenAgentSettings(string agent) =>
        _authority is null ? null : new AgentSettingsViewModel(_authority, agent, () =>
        {
            Read(forceHistory: false);
            Summarize();
        });

    /// <summary>Starts looking for the AI tools on this computer, dropping what an earlier look finds later.</summary>
    private void Look()
    {
        if (_connector is not { } connector || _disposed)
        {
            return;
        }

        IsLooking = true;
        Looking = LookAsync(connector, ++_looks, _closing.Token);
    }

    private async Task LookAsync(ClientConnector connector, int look, CancellationToken cancellationToken)
    {
        IReadOnlyList<McpToolFinding> found;

        try
        {
            found = await McpToolSurvey.LookAsync(connector.Machine, connector.Runner, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _post(() =>
        {
            if (_disposed || look != _looks)
            {
                return;
            }

            _findings = found;
            _looked = true;
            IsLooking = false;
            ReadClients(force: false);
            Raise(nameof(HasNoTools));
        });
    }

    /// <summary>Opens a tool's pane under its row, closing the one open, and composes its plan.</summary>
    private void Open(AgentRow? row, bool removes)
    {
        if (row?.Tool is not { } tool || _authority is null || _connector is null || !CanConnect)
        {
            return;
        }

        CloseConnection();

        var pane = new ConnectClientViewModel(_authority.Session, _connector, tool, removes, ran: Look, cancelled: Cancelled);
        pane.PropertyChanged += OnConnectionChanged;
        _connecting = row;
        row.Connection = pane;
        Raise(nameof(Connection));
        RaiseConnectCommands();
        Opening = pane.PreviewAsync();
    }

    private void Cancelled()
    {
        CloseConnection();
        _toast("Cancelled. Nothing was changed.");
    }

    private void CloseConnection()
    {
        if (_connecting is not { } row)
        {
            return;
        }

        var pane = row.Connection;
        row.Connection = null;
        _connecting = null;

        if (pane is not null)
        {
            pane.PropertyChanged -= OnConnectionChanged;
            pane.Dispose();
        }

        Raise(nameof(Connection));
        RaiseConnectCommands();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectClientViewModel.IsBusy))
        {
            RaiseConnectCommands();
        }
    }

    private void RaiseConnectCommands()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        ConnectAgainCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        CloseConnectionCommand.RaiseCanExecuteChanged();
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
            ["since you unlocked"]);

        // No log yet is the first run before any agent asked, as the activity log reads it too.
        if (history.Kind == AuditReadKind.Missing || (history.Kind == AuditReadKind.Intact && history.Entries.Count == 0))
        {
            Show([], "Nothing has been recorded since you unlocked.");
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

    /// <summary>Brings the prompt forward; only the person's press reaches it, never a message on the pipe (D-0456).</summary>
    private void ShowPrompt()
    {
        if (_authority?.ShowPrompt() != true)
        {
            _toast("The prompt window is no longer open.");
        }

        Read(forceHistory: false);
    }

    private void End(ActivityRow? row)
    {
        if (row?.Id is { } id)
        {
            var ended = _authority?.Revoke(id) == true;
            Read(forceHistory: false);

            if (ended)
            {
                _toast($"Ended {row.Client}'s allowance.");
            }
        }
    }

    private void EndAll()
    {
        _authority?.RevokeAll();
        Read(forceHistory: false);
        _toast("Ended every allowance.");
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

        foreach (var row in _byKey.Values)
        {
            row.Dispose();
        }
    }
}
