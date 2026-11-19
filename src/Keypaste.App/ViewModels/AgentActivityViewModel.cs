using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Agents: the request waiting for the person, and one list of the agents connected and the AI tools found on this
/// computer, whose chosen row's detail holds its connection, its sessions and its rules; Connect is a sheet over it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Waiting requests are read from the session authority</b> that answers agents,
/// never from the prompt window's events, so the list says what the next request would
/// meet. Nothing here answers a request: Show prompt brings its window forward.
/// </para>
/// <para>
/// <b>The AI tools are looked for in the background</b> each time the screen opens, as
/// <c>keypaste setup</c> finds them, and again once a Connect or Disconnect has run. Each
/// row is kept by its key, updated in place and left where it is listed, so its open settings,
/// their unsaved edits and the focus in them survive the refresh; so do a waiting request's buttons.
/// One Connect sheet is open at a time, and while its plan or check runs it stays open.
/// </para>
/// <para>
/// It re-reads the authority every second so the lifetimes count down, and re-reads the audit file, which says
/// when each agent last asked, only when its length or write time has moved. The log itself is Agents › Activity.
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
    private readonly string? _home;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, AgentRow> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opened = new(StringComparer.Ordinal);
    private bool _choosing;
    private IReadOnlyList<AuditEntry> _auditEntries = [];
    private (long Length, DateTime Written)? _auditRead;
    private readonly ObservableCollection<AgentRow> _rows = [];
    private IReadOnlyList<McpToolFinding> _findings = [];
    private string _clientsSignature = string.Empty;
    private readonly Action<Action> _post;
    private readonly Action<string, ToastTone> _toast;
    private readonly ITimer _timer;
    private readonly CancellationTokenSource _closing = new();
    private int _looks;
    private string? _server;

    private readonly ObservableCollection<ActivityRow> _waiting = [];
    private readonly ObservableCollection<object> _listed = [];
    private bool _disposed;

    /// <param name="authority">The session authority that answers agents, or null when nothing here does.</param>
    /// <param name="home">The keypaste home whose audit log and the vault's agent rules are read.</param>
    /// <param name="clock">What lifetimes count down on.</param>
    /// <param name="post">Runs a tick or a finished look on the UI thread.</param>
    /// <param name="connector">What finding and connecting AI tools reaches, or null to look for none.</param>
    /// <param name="toast">Says what a press did.</param>
    /// <param name="openActivity">Opens Agents › Activity.</param>
    /// <param name="clipboard">Where a Connect pane copies a tool's install link.</param>
    /// <param name="web">What opens a tool's install link.</param>
    internal AgentActivityViewModel(
        AppAuthority? authority,
        string? home,
        TimeProvider? clock = null,
        Action<Action>? post = null,
        ClientConnector? connector = null,
        Action<string, ToastTone>? toast = null,
        Action? openActivity = null,
        ClipboardCountdown? clipboard = null,
        IWebLauncher? web = null)
    {
        _authority = authority;
        _connector = authority is null ? null : connector;
        _clipboard = clipboard;
        _web = web;
        OpenActivityCommand = new RelayCommand(() => openActivity?.Invoke(), () => openActivity is not null);
        _toast = toast ?? ((_, _) => { });
        OffersActivity = openActivity is not null;
        _auditPath = KeypasteHome.AuditPath(home);
        _home = home;
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (run => run());
        ConnectCommand = new RelayCommand<AgentRow>(row => Open(row, removes: false), row => row is { OffersConnect: true } && CanConnect);
        RepairCommand = new RelayCommand<AgentRow>(row => Open(row, removes: false), row => row is { OffersRepair: true } && CanConnect);
        DisconnectCommand = new RelayCommand<AgentRow>(row => Open(row, removes: true), row => row is { OffersDisconnect: true } && CanConnect);
        CloseSheetCommand = new RelayCommand(CloseSheet, () => Connection is not { IsBusy: true });

        Refresh();
        Look();

        _timer = _clock.CreateTimer(_ => _post(Tick), null, _tick, _tick);
    }

    /// <summary>The unlocked vaults the list's picker offers, or none where one vault is open.</summary>
    internal IReadOnlyList<VaultChoice> VaultChoices { get; init; } = [];

    /// <summary>Whether the list starts with a picker of the vault whose agents and rules it shows.</summary>
    internal bool OffersVaultChoice => VaultChoices.Count > 1;

    /// <summary>The vault whose agents and rules the list shows; choosing another shows that vault's through <see cref="ShowVault"/>.</summary>
    internal VaultChoice? ShownVault
    {
        get => VaultChoices.FirstOrDefault(choice => ReferenceEquals(choice.Vault, _authority?.Session));
        set
        {
            if (value?.Vault is { } vault && !ReferenceEquals(vault, _authority?.Session))
            {
                ShowVault?.Invoke(value);
            }
        }
    }

    /// <summary>Shows another vault's agents and rules, set by the shell.</summary>
    internal Action<VaultChoice>? ShowVault { get; init; }

    /// <summary>The agent whose detail is drawn, or null while the list is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    internal partial AgentRow? Selected { get; set; }

    internal bool HasSelected => Selected is not null;

    /// <summary>Whether the person chose something to read, so a width that holds only one pane shows the detail rather than the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDetail))]
    internal partial bool IsOpened { get; private set; }

    /// <summary>Whether the detail replaces the list when both cannot fit.</summary>
    internal bool ShowsDetail => IsOpened;

    /// <summary>Whether no agent is connected and no tool was found, so the content says so and offers Connect.</summary>
    internal bool IsEmpty => _rows.Count == 0 && !IsLooking;

    partial void OnSelectedChanged(AgentRow? value)
    {
        if (value is not null)
        {
            value.IsOpen = true;
        }

        if (!_choosing)
        {
            IsOpened = value is not null;
        }
    }

    /// <summary>Shows one agent's detail, as pressing its row does.</summary>
    internal void Choose(AgentRow row) => Selected = row;

    /// <summary>Pressing a row opens its detail over the list, also when it is the row already chosen.</summary>
    internal void Open(AgentRow row)
    {
        Selected = row;
        IsOpened = true;
    }

    /// <summary>Leaves the detail for the list, where a width holds only one of them.</summary>
    [RelayCommand]
    private void CloseDetail()
    {
        IsOpened = false;
        Settle();
    }

    /// <summary>With nothing chosen the first row stands in, shown beside the list but not opened over it.</summary>
    private void Settle()
    {
        if (Selected is not null && _rows.Contains(Selected))
        {
            return;
        }

        _choosing = true;
        Selected = _rows.FirstOrDefault(row => row.IsConnected) ?? _rows.FirstOrDefault();
        _choosing = false;
        IsOpened = false;
    }

    /// <summary>The agents connected, then the tools found on this computer, each under its heading, and the look in progress last.</summary>
    internal IReadOnlyList<object> Listed => _listed;

    /// <summary>The Connect sheet over the screen, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheet))]
    internal partial ConnectSheetViewModel? Sheet { get; private set; }

    internal bool HasSheet => Sheet is not null || Connection is not null;

    /// <summary>Opens the Connect sheet: the setup prompt, the entry for a tool set up by hand, and the tools found.</summary>
    [RelayCommand]
    internal void StartConnect() => OpenSheet(string.Empty);

    /// <summary>Opens the Connect sheet with a name for an agent that has none, so its entry gives it settings of its own.</summary>
    [RelayCommand]
    private void GiveName(AgentRow? row)
    {
        if (row is { OffersNaming: true })
        {
            OpenSheet(ConnectSheetViewModel.LabelFrom(row.GivenName));
        }
    }

    private void OpenSheet(string name)
    {
        CloseSheet();
        Sheet = new ConnectSheetViewModel([.. _rows.Where(row => row.Tool is not null && row.Presence is not null)], row => Open(row, removes: false), _clipboard, _toast, name);
    }

    /// <summary>Puts the setup prompt on the clipboard, for an AI tool to connect itself.</summary>
    [RelayCommand]
    private async Task CopySetupPromptAsync()
    {
        if (_clipboard is not null)
        {
            await _clipboard.CopyPlainAsync(ConnectSheetViewModel.SetupPrompt, Resources.Strings.SetupPromptCopied).ConfigureAwait(true);
            _toast(Resources.Strings.SetupPromptCopied, ToastTone.Success);
        }
    }

    /// <summary>The settings for every agent, in Settings › Agents, set by the shell.</summary>
    internal Action? OpenDefaults { get; init; }

    /// <summary>Settings, where the <c>keypaste</c> command is linked onto the PATH, set by the shell.</summary>
    internal Action? OpenSettingsScreen { get; init; }

    [RelayCommand]
    private void ShowDefaults()
    {
        CloseSheet();
        OpenDefaults?.Invoke();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CloseSheet();
        OpenSettingsScreen?.Invoke();
    }

    /// <summary>The AI tools found and the agents of this vault; a row stays where it was first listed while the screen is open.</summary>
    internal IReadOnlyList<AgentRow> Rows => _rows;

    /// <summary>Whether the tools on this computer are being looked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    internal partial bool IsLooking { get; private set; }

    /// <summary>The look in progress or the last one, finished once what it found is posted.</summary>
    internal Task Looking { get; private set; } = Task.CompletedTask;

    /// <summary>Opens a found tool's part of the Connect sheet.</summary>
    internal RelayCommand<AgentRow> ConnectCommand { get; }

    /// <summary>Opens a connected tool's part of the Connect sheet again, where its entry differs from what Connect writes.</summary>
    internal RelayCommand<AgentRow> RepairCommand { get; }

    /// <summary>Opens the sheet that disconnects a tool.</summary>
    internal RelayCommand<AgentRow> DisconnectCommand { get; }

    /// <summary>Closes the sheet, unless its plan or check is running.</summary>
    internal RelayCommand CloseSheetCommand { get; }

    /// <summary>A tool's part of the Connect sheet, connecting or disconnecting it, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheet), nameof(HasConnection))]
    internal partial ConnectClientViewModel? Connection { get; private set; }

    internal bool HasConnection => Connection is not null;

    /// <summary>The tool's part composing its plan, or a completed task.</summary>
    internal Task Opening { get; private set; } = Task.CompletedTask;

    /// <summary>Why a tool's connection can't be changed here, for its disabled buttons' tooltip; null while it can.</summary>
    internal string? ToolActionsTip => CanConnect ? null : _authority is null ? Resources.Strings.AgentsCantReachThisVaultSoItsConnectionsCantChange : Resources.Strings.KeypasteCantChangeAIToolsConnectionsHere;

    /// <summary>That keypaste can't use its agent settings and what it does meanwhile, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClientsProblem))]
    internal partial string ClientsProblem { get; private set; } = string.Empty;

    internal bool HasClientsProblem => ClientsProblem.Length > 0;

    /// <summary>Why, as the file reader said it, for the problem's tooltip.</summary>
    [ObservableProperty]
    internal partial string ClientsProblemDetail { get; private set; } = string.Empty;

    /// <summary>Whether the sheet's action may be primary: not while a request waits, whose countdown is the live signal.</summary>
    internal bool FormsArePrimary => !HasWaiting;

    /// <summary>Agents › Activity: what agents and tokens asked for, and what came of it.</summary>
    internal RelayCommand OpenActivityCommand { get; }

    internal bool OffersActivity { get; }

    /// <summary>What every agent can see of an item without asking, as the defaults set it; unknown while they can't be read.</summary>
    [ObservableProperty]
    internal partial string CanSee { get; private set; } = Resources.Strings.UnknownWhileAgentsCantReachTheVault;

    /// <summary>One true sentence about what agents can do with this vault.</summary>
    [ObservableProperty]
    internal partial string Status { get; private set; } = string.Empty;

    /// <summary>That every agent request is refused while the audit log will not open, or empty when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuditRefusal))]
    internal partial string AuditRefusal { get; private set; } = string.Empty;

    internal bool HasAuditRefusal => AuditRefusal.Length > 0;

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

    /// <summary>Shows the named agent's detail, listing it first when no row shows it yet; <c>*</c> opens the defaults in Settings.</summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    internal void OpenSettings(string agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (string.Equals(agent, AgentNames.Any, StringComparison.Ordinal))
        {
            OpenDefaults?.Invoke();
            return;
        }

        if (!AgentNames.IsNamed(agent))
        {
            return;
        }

        if (!_byKey.ContainsKey(agent) && _opened.Add(agent))
        {
            ReadClients(force: false);
        }

        if (_byKey.TryGetValue(agent, out var row))
        {
            Choose(row);
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
            var set = asked + activity.WaitingSets.Count;
            var save = set + activity.WaitingSaves.Count;
            var change = save + activity.WaitingChanges.Count;
            var run = change + activity.WaitingRuns.Count;
            var env = run + activity.WaitingEnvs.Count;
            waiting =
            [
                .. activity.Waiting.Select((request, i) => ActivityRow.Waiting(i + 1, request)),
                .. activity.WaitingSets.Select((request, i) => ActivityRow.WaitingSet(asked + i + 1, request)),
                .. activity.WaitingSaves.Select((request, i) => ActivityRow.WaitingSave(set + i + 1, request)),
                .. activity.WaitingChanges.Select((request, i) => ActivityRow.WaitingChange(save + i + 1, request)),
                .. activity.WaitingRuns.Select((request, i) => ActivityRow.WaitingRun(change + i + 1, request)),
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
        CanSee = rules is null || status is not AuthorityStatus.Serving ? Resources.Strings.UnknownWhileAgentsCantReachTheVault : Seen(rules);

        // With agents.toml unreadable every agent request is refused, so nobody is answered without asking.
        Status = Describe(
            status,
            status is AuthorityStatus.Serving && rules is not null
                ? [.. FieldCategories.Ruled.Where(category => rules.RuleFor(category) == CategoryRule.Allow).Select(FieldCategories.Shown)]
                : []);
        AuditRefusal = status is AuthorityStatus.Serving && _authority?.LastAuditFailure is { } failure
            ? _authority.LastAuditFailureIsWrite ? Unwritten(failure) : Unrecorded(failure)
            : string.Empty;
    }

    /// <summary>What every agent sees of an item without asking, as the defaults set it.</summary>
    private static string Seen(AgentRules rules)
    {
        var seen = rules.ShowsUsername ? Resources.Strings.CanSeeUsernames : Resources.Strings.CanSeeUsernamesOnRequest;
        return rules.ShowsLinkedHosts ? $"{seen} {Resources.Strings.CanSeeHostsAndReferences}" : seen;
    }

    /// <summary>
    /// That every agent request is refused while the audit log will not open, as the last request found
    /// it; said from that refusal, so the screen opens no log of its own.
    /// </summary>
    internal static string Unrecorded(string failure) =>
        $"keypaste refuses every agent request while its audit log won't open: {failure.TrimEnd('.')}. Fix the log file, and the next request opens it again.";

    /// <summary>That agents are refused while the open audit log cannot be written, as the last line found it.</summary>
    internal static string Unwritten(string failure) =>
        $"keypaste refuses agent requests while it can't write its audit log: {failure.TrimEnd('.')}. Free some disk space or close the other keypaste, and the next request tries again.";

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

        var read = CurrentRules(out var problem, out var detail);
        ClientsProblem = problem;
        ClientsProblemDetail = detail;

        var now = _clock.GetUtcNow();

        // A row whose settings are open is built from its name, so it stays listed, and current, until they close.
        var open = _byKey.Values.Where(row => row.IsOpen).Select(row => row.Label).OfType<string>();
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

        foreach (var card in cards.Where(card => !string.Equals(card.Key, AgentNames.Any, StringComparison.Ordinal)))
        {
            if (_byKey.TryGetValue(card.Key, out var row))
            {
                row.Update(card, now);
            }
            else
            {
                row = new AgentRow(card, now, OpenAgentSettings);
                row.PropertyChanged += OnRowChanged;
                _byKey[card.Key] = row;
            }

            row.OffersRepair = row.Presence?.State == McpToolState.Connected && row.Tool is { } tool && _server is { } server && Differs(row.Launch, server, tool.Id);
            listed.Add(row);
        }

        Place(listed);
        Section();
        OnPropertyChanged(nameof(IsEmpty));

        foreach (var (key, row) in _byKey.Where(pair => !listed.Contains(pair.Value)).ToList())
        {
            _byKey.Remove(key);
            row.PropertyChanged -= OnRowChanged;
            row.Dispose();
        }

        Settle();

        // What each row's rules are read from: each card and the file beneath them.
        var signature = string.Join(
            '\n',
            cards.Select(card => $"{card.Key}|{card.Presence}|{card.Run}")
                .Append($"*|{ClientsProblem}|{Stamp(Rules?.Path)}"));

        if (force || !string.Equals(signature, _clientsSignature, StringComparison.Ordinal))
        {
            _clientsSignature = signature;

            foreach (var row in _rows)
            {
                row.ReadSettings();
            }
        }

        return read;
    }

    /// <summary>Whether a tool's entry starts something other than what Connect writes: another program, or not under the tool's own name.</summary>
    private static bool Differs(McpEntryLaunch? launch, string server, string label)
    {
        if (launch is null || !PathIdentity.SameFile(launch.Command, server))
        {
            return true;
        }

        var at = launch.Arguments.ToList().IndexOf("--client-label");
        return at < 0 || at + 1 >= launch.Arguments.Count || !string.Equals(launch.Arguments[at + 1], label, StringComparison.Ordinal);
    }

    /// <summary>A row that moves between Connected and Found on this computer, or starts or stops the look, lists again.</summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentRow.Presence) or nameof(AgentRow.IsLive))
        {
            Section();
        }
    }

    partial void OnIsLookingChanged(bool value) => Section();

    /// <summary>
    /// Lists the rows under their headings in place, moving only what changed section, so a row's container, and the focus
    /// in it, is kept.
    /// </summary>
    private void Section()
    {
        List<object> wanted = [];
        var connected = _rows.Where(row => row.IsConnected).ToList();
        var found = _rows.Where(row => !row.IsConnected).ToList();

        if (connected.Count > 0)
        {
            wanted.Add(AgentListHeading.Connected);
            wanted.AddRange(connected);
        }

        if (found.Count > 0)
        {
            wanted.Add(AgentListHeading.Found);
            wanted.AddRange(found);
        }

        if (IsLooking)
        {
            wanted.Add(AgentListHeading.Looking);
        }

        for (var i = _listed.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(_listed[i]))
            {
                _listed.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var at = _listed.IndexOf(wanted[i]);

            if (at < 0)
            {
                _listed.Insert(i, wanted[i]);
            }
            else if (at != i)
            {
                _listed.Move(at, i);
            }
        }
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
    private AgentRules? CurrentRules(out string problem, out string detail)
    {
        if (_authority is not null && _authority.TryRules(out var held, out var ignored))
        {
            problem = ignored is null ? string.Empty : Resources.Strings.KeypasteIgnoresItsAgentSettings;
            detail = ignored ?? string.Empty;
            return held;
        }

        AgentRules? said = AgentRules.Defaults;
        var unreadable = string.Empty;

        if (Rules is { } file && !file.TryRead(out said, out unreadable))
        {
            problem = Resources.Strings.KeypasteCantReadItsAgentSettings;
            detail = $"{unreadable}. Deleting the file asks about every value again.";
            return null;
        }

        problem = string.Empty;
        detail = string.Empty;
        return said;
    }

    /// <summary>The rules of the vault the authority holds, or null while it holds none.</summary>
    private AgentRulesFile? Rules => _authority?.Session.Identity is { } vault ? AgentRulesFile.For(_home, vault) : null;

    /// <summary>The rules a row's detail opens, which re-read the screen after each change.</summary>
    private AgentSettingsViewModel? OpenAgentSettings(AgentRow row) =>
        _authority is null || row.Label is not { } agent ? null : new AgentSettingsViewModel(_authority, agent, row.Title, () => Read(force: false));

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
            _server = connector.FindServer().Target?.Server.Path;
            IsLooking = false;
            ReadClients(force: false);
        });
    }

    /// <summary>Opens a tool's part of the Connect sheet, closing what is open, and composes its plan.</summary>
    private void Open(AgentRow? row, bool removes)
    {
        if (row?.Tool is not { } tool || _authority is null || _connector is null || !CanConnect)
        {
            return;
        }

        CloseSheet();
        Choose(row);

        var pane = new ConnectClientViewModel(
            _authority.Session,
            _connector,
            tool,
            removes,
            ran: Look,
            clipboard: _clipboard,
            web: _web,
            accountVaults: LocalAccount.Vaults(_authority.Session.Home));
        pane.PropertyChanged += OnConnectionChanged;
        Connection = pane;
        RaiseConnectCommands();
        Opening = pane.PreviewAsync();
    }

    private void CloseSheet()
    {
        Sheet = null;

        if (Connection is not { } pane)
        {
            return;
        }

        Connection = null;
        pane.PropertyChanged -= OnConnectionChanged;
        pane.Dispose();
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
        RepairCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        CloseSheetCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ToolActionsTip));
    }

    /// <summary>Brings the waiting prompt's window forward with Deny focused and its allows disarmed for a second; only the person's press reaches it, never a message on the pipe.</summary>
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
        CloseSheet();
        _closing.Cancel();
        _closing.Dispose();
        _timer.Dispose();

        foreach (var row in _byKey.Values)
        {
            row.Dispose();
        }
    }
}
