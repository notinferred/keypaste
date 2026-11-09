using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Agents: the request waiting for the person, the AI tools on this computer
/// and the agents that asked, each with its settings, and what was recorded since the app was unlocked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Waiting requests are read from the session authority</b> that answers agents
/// (D-0330), never from the prompt window's events, so the list says what the next request would
/// meet. Nothing here answers a request: Show the prompt brings its window forward (D-0456).
/// </para>
/// <para>
/// <b>The AI tools are looked for in the background</b> each time the screen opens, as
/// <c>keypaste setup</c> finds them, and again once a Connect or Disconnect has run (D-0455). Each
/// row is kept by its key, updated in place and left where it is listed, so an open Settings disclosure,
/// its unsaved edits, an open pane and the focus or open drop-down in them survive the refresh; so do a
/// waiting request's buttons. One Connect pane is open at a time, and while its plan
/// or check runs no other Connect, Connect again or Disconnect can start.
/// </para>
/// <para>
/// It re-reads the authority every second so the lifetimes count down, and re-reads the audit file, which says
/// when each agent last asked, only when its length or write time has moved. The log itself is Agents › History.
/// </para>
/// </remarks>
internal sealed partial class AgentActivityViewModel : ObservableObject, IDisposable, IHoldsDraft
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly AppAuthority? _authority;
    private readonly ClientConnector? _connector;
    private readonly ClipboardCountdown? _clipboard;
    private readonly IWebLauncher? _web;
    private readonly string _auditPath;
    private readonly AgentRulesFile _rules;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, AgentRow> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opened = new(StringComparer.Ordinal);
    private IReadOnlyList<AuditEntry> _auditEntries = [];
    private (long Length, DateTime Written)? _auditRead;
    private readonly ObservableCollection<AgentRow> _rows = [];
    private IReadOnlyList<McpToolFinding> _findings = [];
    private string _clientsSignature = string.Empty;
    private readonly Action<Action> _post;
    private readonly Action<string, ToastTone> _toast;
    private readonly ITimer _timer;
    private readonly CancellationTokenSource _closing = new();
    private bool _looked;
    private int _looks;
    private AgentRow? _connecting;

    private readonly ObservableCollection<ActivityRow> _waiting = [];
    private bool _disposed;

    /// <param name="authority">The session authority that answers agents, or null when nothing here does.</param>
    /// <param name="home">The keypaste home whose audit log and <c>agents.toml</c> are read.</param>
    /// <param name="clock">What lifetimes count down on.</param>
    /// <param name="post">Runs a tick or a finished look on the UI thread.</param>
    /// <param name="connector">What finding and connecting AI tools reaches, or null to look for none.</param>
    /// <param name="toast">Says what a press did.</param>
    /// <param name="openHistory">Opens Agents › History.</param>
    /// <param name="clipboard">Where a Connect pane copies a tool's install link.</param>
    /// <param name="web">What opens a tool's install link.</param>
    internal AgentActivityViewModel(
        AppAuthority? authority,
        string? home,
        TimeProvider? clock = null,
        Action<Action>? post = null,
        ClientConnector? connector = null,
        Action<string, ToastTone>? toast = null,
        Action? openHistory = null,
        ClipboardCountdown? clipboard = null,
        IWebLauncher? web = null)
    {
        _authority = authority;
        _connector = authority is null ? null : connector;
        _clipboard = clipboard;
        _web = web;
        OpenHistoryCommand = new RelayCommand(() => openHistory?.Invoke(), () => openHistory is not null);
        _toast = toast ?? ((_, _) => { });
        OffersHistory = openHistory is not null;
        _auditPath = KeypasteHome.AuditPath(home);
        _rules = new AgentRulesFile(KeypasteHome.AgentsPath(home));
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (run => run());
        ConnectCommand = new RelayCommand<AgentRow>(row => Open(row, removes: false), row => row is { OffersConnect: true } && CanConnect);
        ConnectAgainCommand = new RelayCommand<AgentRow>(row => Open(row, removes: false), row => row is { OffersConnectAgain: true } && CanConnect);
        DisconnectCommand = new RelayCommand<AgentRow>(row => Open(row, removes: true), row => row is { OffersDisconnect: true } && CanConnect);
        CloseConnectionCommand = new RelayCommand(CloseConnection, () => Connection is { IsBusy: false });


        Refresh();
        Look();

        _timer = _clock.CreateTimer(_ => _post(Tick), null, _tick, _tick);
    }

    /// <summary>The AI tools found and the agents of this vault, Every other agent last; a row stays where it was first listed while the screen is open.</summary>
    internal IReadOnlyList<AgentRow> Rows => _rows;

    /// <summary>Whether the tools on this computer are being looked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTools))]
    internal partial bool IsLooking { get; private set; }

    /// <summary>The look in progress or the last one, finished once what it found is posted.</summary>
    internal Task Looking { get; private set; } = Task.CompletedTask;

    /// <summary>Why a listed agent has no Settings, said once for them all, and what would give it some; empty with none listed.</summary>
    internal string UnnamedAgentsNote => !_rows.Any(row => !row.HasSettings)
        ? string.Empty
        : $"{Resources.Strings.AgentsWithoutALabel} {(_rows.Any(row => row.OffersConnect) ? Resources.Strings.ConnectAToolFromThisListToGive : Resources.Strings.SetupGivesEachToolALabel)}";

    internal bool HasUnnamedAgentsNote => UnnamedAgentsNote.Length > 0;

    /// <summary>Whether the last look found none of the tools keypaste connects.</summary>
    internal bool HasNoTools => _connector is not null && _looked && !IsLooking && _findings.Count == 0;

    /// <summary>The tools keypaste looks for on this computer, named when none was found.</summary>
    internal string NoTools => _connector is null
        ? string.Empty
        : $"{Resources.Strings.NoAIToolsFound} {string.Join(", ", McpClientCatalog.For(_connector.Machine.Platform).Select(tool => tool.DisplayName))}. {Resources.Strings.InstallOneThenOpenAgentsAgain}";

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

    /// <summary>Why <c>agents.toml</c> cannot be used, or empty. While it is set, every agent request is refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClientsProblem))]
    internal partial string ClientsProblem { get; private set; } = string.Empty;

    internal bool HasClientsProblem => ClientsProblem.Length > 0;

    /// <summary>Whether a pane's own action may be primary: not while a request waits, whose countdown is the live signal (D-0375).</summary>
    internal bool FormsArePrimary => !HasWaiting;

    /// <summary>Agents › History: what agents and tokens asked for, and what came of it.</summary>
    internal RelayCommand OpenHistoryCommand { get; }

    internal bool OffersHistory { get; }

    /// <summary>The rules every agent's request is decided by on this computer; none without an owner to apply them.</summary>
    internal AgentRulesViewModel? RulesForEveryAgent { get; init; }

    internal bool HasAgentRules => RulesForEveryAgent is not null;

    /// <summary>One true sentence about what agents can do with this vault.</summary>
    [ObservableProperty]
    internal partial string Status { get; private set; } = string.Empty;

    /// <summary>Why waiting requests cannot be read, or empty when they were.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable), nameof(IsUnavailable), nameof(NothingWaiting))]
    internal partial string Unavailable { get; private set; } = string.Empty;

    internal bool IsAvailable => Unavailable.Length == 0;

    internal bool IsUnavailable => !IsAvailable;

    /// <summary>What the person is being asked about; at most one, because the gate asks one at a time.</summary>
    internal IReadOnlyList<ActivityRow> Waiting => _waiting;

    internal bool NothingWaiting => IsAvailable && _waiting.Count == 0;

    internal bool HasWaiting => _waiting.Count > 0;

    /// <summary>The audit log history is read from.</summary>
    internal string AuditPath => _auditPath;

    private bool CanConnect => _connector is not null && _authority is not null && Connection is not { IsBusy: true };

    /// <summary>Reads the authority and the log again.</summary>
    [RelayCommand]
    internal void Refresh() => Read(force: true);

    /// <summary>The status as the screen says it, with nothing allowed without asking.</summary>
    internal static string Describe(AuthorityStatus status) => Describe(status, []);

    /// <summary>The status as the screen says it.</summary>
    /// <param name="status">Who answers agents.</param>
    /// <param name="allowed">Each kind of value this computer's rules release to every agent without asking.</param>
    internal static string Describe(AuthorityStatus status, IReadOnlyList<string> allowed) => status switch
    {
        AuthorityStatus.Serving serving =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"This app (process {serving.Owner.ProcessId}, unlock {serving.Session}) holds this vault for agents while it is unlocked. ")
            + "AI tools connected to this vault reach it here and can list the item names they are allowed to see. "
            + "Each credential request is asked about in a prompt window, and nothing is released unless you allow it, "
            + (allowed.Count == 0
                ? "or Settings allows that kind of value for every agent."
                : $"except what Settings allows for every agent: {string.Join(", ", allowed)}."),
        AuthorityStatus.NotServing notServing => $"Agents cannot reach this vault: {notServing.Reason.TrimEnd('.')}. Lock and unlock the vault to try again.",
        AuthorityStatus.HeldBy held => held.Sentence,
        _ => "Agents cannot reach this vault right now. Lock and unlock the vault to try again.",
    };

    /// <inheritdoc/>
    public string? UnsavedDraft => null;

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
            Read(force: false);
        }
    }

    private void Read(bool force)
    {
        var status = _authority?.Status ?? new AuthorityStatus.Locked();
        List<ActivityRow> waiting = [];

        if (status is AuthorityStatus.Serving serving)
        {
            var activity = _authority!.Activity;
            Unavailable = string.Empty;
            var asked = activity.Waiting.Count;
            var run = asked + activity.WaitingRuns.Count;
            var env = run + activity.WaitingEnvs.Count;
            waiting =
            [
                .. activity.Waiting.Select((request, i) => ActivityRow.Waiting(i + 1, request)),
                .. activity.WaitingRuns.Select((request, i) => ActivityRow.WaitingRun(asked + i + 1, request)),
                .. activity.WaitingEnvs.Select((request, i) => ActivityRow.WaitingEnv(run + i + 1, request)),
                .. activity.WaitingEdits.Select((request, i) => ActivityRow.WaitingEdit(env + i + 1, request)),
            ];
        }
        else
        {
            Unavailable = "Waiting requests can't be read, because this app is not answering agents for this vault. Lock and unlock the vault to try again.";
        }

        Relist(_waiting, waiting);
        OnPropertyChanged(nameof(HasWaiting));
        OnPropertyChanged(nameof(FormsArePrimary));
        OnPropertyChanged(nameof(NothingWaiting));
        ShowPromptCommand.NotifyCanExecuteChanged();

        var rules = ReadClients(force);

        OnPropertyChanged(nameof(UnnamedAgentsNote));
        OnPropertyChanged(nameof(HasUnnamedAgentsNote));

        // With agents.toml unreadable every agent request is refused, so nobody is answered without asking.
        var said = Describe(
            status,
            status is AuthorityStatus.Serving && rules is not null
                ? [.. FieldCategories.Ruled.Where(category => rules.RuleFor(category) == CategoryRule.Allow).Select(FieldCategories.Shown)]
                : []);
        Status = status is AuthorityStatus.Serving && _authority?.LastAuditFailure is { } unopened
            ? $"{said} {Unrecorded(unopened)}"
            : said;
    }

    /// <summary>
    /// That every agent request is refused while the audit log will not open, as the last request found
    /// it; said from that refusal, so the screen opens no log of its own.
    /// </summary>
    internal static string Unrecorded(string failure) =>
        $"keypaste refuses every agent request while its audit log won't open: {failure.TrimEnd('.')}. Fix the log file, and the next request opens it again.";

    /// <summary>
    /// Builds the rows from the tools found, who is attached, this vault's audit lines and <c>agents.toml</c>,
    /// keeping each row that was already listed.
    /// </summary>
    /// <returns>This computer's rules, or null when <c>agents.toml</c> cannot be read.</returns>
    private AgentRules? ReadClients(bool force)
    {
        var file = new FileInfo(_auditPath);
        (long, DateTime)? stamp = file.Exists ? (file.Length, file.LastWriteTimeUtc) : null;

        if (force || stamp != _auditRead)
        {
            _auditRead = stamp;
            _auditEntries = stamp is not null && AuditReader.TryRead(_auditPath, out var entries, out _, out _) ? entries : [];
        }

        var read = CurrentRules(out var problem);
        ClientsProblem = problem;

        var now = _clock.GetUtcNow();

        // A row whose settings or pane are open is built from its name, so it stays listed, and current, until they close.
        var open = _byKey.Values.Where(row => row.IsOpen || row.HasConnection).Select(row => row.Label).OfType<string>();
        var known = read ?? AgentRules.Defaults;

        foreach (var name in _opened.Concat(open).Where(AgentNames.IsNamed).Distinct(StringComparer.Ordinal).ToList())
        {
            if (known.Agents.Count < AgentRules.MaximumAgents && !known.Agents.Any(agent => string.Equals(agent.Name, name, StringComparison.Ordinal)))
            {
                known = known with { Agents = [.. known.Agents, new KnownAgent(name, false)] };
            }
        }

        var cards = McpClientCards.Build(
            _authority?.Clients ?? [],
            _auditEntries,
            _authority?.Session.Identity?.Key ?? string.Empty,
            known,
            _findings,
            now);
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

        // What each summary is read from: each card and the file beneath them.
        var signature = string.Join(
            '\n',
            cards.Select(card => $"{card.Key}|{card.Presence}|{card.Run}")
                .Append($"*|{ClientsProblem}|{Stamp(_rules.Path)}"));

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

        return read;
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

    /// <summary>
    /// The rules agents are decided by, as the authority checks their seal, or as the file says while nothing here
    /// answers agents; null when the file cannot be read, which refuses every agent request.
    /// </summary>
    private AgentRules? CurrentRules(out string problem)
    {
        if (_authority is not null && _authority.TryRules(out var held, out var ignored))
        {
            problem = ignored is null ? string.Empty : $"{char.ToUpperInvariant(ignored[0])}{ignored[1..]}: every value is asked about and no agent runs commands.";
            return held;
        }

        if (!_rules.TryRead(out var said, out var unreadable))
        {
            problem = $"{unreadable}. Every agent request is refused until it is fixed; deleting it asks about every value again.";
            return null;
        }

        problem = string.Empty;
        return said;
    }

    /// <summary>Says on each named row whether it may ask to run commands.</summary>
    private void Summarize()
    {
        var rules = CurrentRules(out _);

        foreach (var row in _rows)
        {
            row.Summary = row.Label is { } label && !row.IsEveryOther && rules is not null && rules.Runs(label) ? "may ask to run commands" : string.Empty;
        }
    }

    /// <summary>The panel a row's Settings opens, which re-reads the screen after each change.</summary>
    private AgentSettingsViewModel? OpenAgentSettings(string agent) =>
        _authority is null ? null : new AgentSettingsViewModel(_authority, agent, () =>
        {
            Read(force: false);
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
            OnPropertyChanged(nameof(HasNoTools));
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

        var pane = new ConnectClientViewModel(_authority.Session, _connector, tool, removes, ran: Look, cancelled: Cancelled, clipboard: _clipboard, web: _web);
        pane.PropertyChanged += OnConnectionChanged;
        _connecting = row;
        row.Connection = pane;
        OnPropertyChanged(nameof(Connection));
        CloseConnectionCommand.NotifyCanExecuteChanged();
        RaiseConnectCommands();
        Opening = pane.PreviewAsync();
    }

    private void Cancelled()
    {
        CloseConnection();
        _toast("Cancelled. Nothing was changed.", ToastTone.Information);
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

        OnPropertyChanged(nameof(Connection));
        CloseConnectionCommand.NotifyCanExecuteChanged();
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
        ConnectCommand.NotifyCanExecuteChanged();
        ConnectAgainCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        CloseConnectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Brings the waiting prompt's window forward with Deny focused and its allows disarmed for a second; only the person's press reaches it, never a message on the pipe (D-0456).</summary>
    [RelayCommand(CanExecute = nameof(HasWaiting))]
    private void ShowPrompt()
    {
        if (_authority?.ShowPrompt() != true)
        {
            _toast("The prompt window is no longer open.", ToastTone.Information);
        }

        Read(force: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RulesForEveryAgent?.Dispose();
        _closing.Cancel();
        _closing.Dispose();
        _timer.Dispose();

        foreach (var row in _byKey.Values)
        {
            row.Dispose();
        }
    }
}
