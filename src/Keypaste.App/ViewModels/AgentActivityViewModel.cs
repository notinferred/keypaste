using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
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
    private bool _isConnectOpen;

    private string _status = string.Empty;
    private string _serving = string.Empty;
    private string _unavailable = string.Empty;
    private IReadOnlyList<ActivityRow> _waiting = [];
    private IReadOnlyList<ActivityRow> _grants = [];
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
        ClipboardCountdown? clipboard = null)
    {
        _authority = authority;
        _toast = toast ?? (_ => { });
        Connect = authority is null ? null : new ConnectClientViewModel(authority.Session, connector ?? ClientConnector.ForThisProcess());
        Tokens = authority is null ? null : new ScopedTokensViewModel(authority.Session, clipboard, _toast);
        ToggleConnectCommand = new RelayCommand(() => IsConnectOpen = !IsConnectOpen, () => Connect is not null);
        _auditPath = KeypasteHome.AuditPath(home);
        _clientsPath = KeypasteHome.ClientsPath(home);
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (run => run());
        SetPolicyCommand = new RelayCommand<PolicyChoice>(chosen =>
        {
            if (chosen is not null)
            {
                SetPolicy(chosen.Row, chosen.Policy);
            }
        });
        ResetClientsCommand = new RelayCommand(ResetClients, () => _clientsProblem.Length > 0);

        RefreshCommand = new RelayCommand(Refresh);
        RevokeCommand = new RelayCommand<ActivityRow>(Revoke, row => row?.Id is not null);
        RevokeAllCommand = new RelayCommand(RevokeAll, () => _grants.Count > 0);

        Refresh();

        _timer = _clock.CreateTimer(_ => _post(Tick), null, _tick, _tick);
    }

    /// <summary>The MCP clients of this vault, with the policy each is held to; the <c>*</c> card last.</summary>
    internal IReadOnlyList<ClientCardRow> Clients => _clients;

    /// <summary>The words each policy is chosen by.</summary>
    internal static IReadOnlyList<string> PolicyOptions => ClientCardRow.PolicyOptions;

    /// <summary>Gives a client a policy, writing <c>clients.toml</c>.</summary>
    internal RelayCommand<PolicyChoice> SetPolicyCommand { get; }

    /// <summary>Replaces a clients file that cannot be read with an empty one.</summary>
    internal RelayCommand ResetClientsCommand { get; }

    /// <summary>Why <c>clients.toml</c> cannot be used, or empty. While it is set, every agent request is refused.</summary>
    internal string ClientsProblem
    {
        get => _clientsProblem;
        private set
        {
            if (Set(ref _clientsProblem, value))
            {
                Raise(nameof(HasClientsProblem));
                ResetClientsCommand.RaiseCanExecuteChanged();
            }
        }
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

    internal RelayCommand ToggleConnectCommand { get; }

    /// <summary>The vault's scoped tokens, minting one and revoking one.</summary>
    internal ScopedTokensViewModel? Tokens { get; }

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
        private set
        {
            if (Set(ref _serving, value))
            {
                Raise(nameof(IsServing));
            }
        }
    }

    internal bool IsServing => _serving.Length > 0;

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

    internal bool HasWaiting => _waiting.Count > 0;

    /// <summary>The grants in force, soonest to end first.</summary>
    internal IReadOnlyList<ActivityRow> Grants => _grants;

    internal bool NoGrants => IsAvailable && _grants.Count == 0;

    internal bool HasGrants => _grants.Count > 0;

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
        Raise(nameof(NothingWaiting));
        Raise(nameof(HasWaiting));
        Raise(nameof(Grants));
        Raise(nameof(NoGrants));
        Raise(nameof(HasGrants));
        RevokeAllCommand.RaiseCanExecuteChanged();

        ReadHistory(session, forceHistory);
        ReadClients(forceHistory);
    }

    /// <summary>Builds the client cards from who is attached, this vault's audit lines and <c>clients.toml</c>.</summary>
    private void ReadClients(bool force)
    {
        var file = new FileInfo(_auditPath);
        (long, DateTime)? stamp = file.Exists ? (file.Length, file.LastWriteTimeUtc) : null;

        if (force || stamp != _auditRead)
        {
            _auditRead = stamp;
            _auditEntries = stamp is not null && AuditReader.TryRead(_auditPath, out var entries, out _, out _) ? entries : [];
        }

        ClientPolicies policies;

        if (ClientPolicies.TryLoad(_clientsPath, out var loaded, out var problem))
        {
            policies = loaded;
            ClientsProblem = string.Empty;
        }
        else
        {
            policies = ClientPolicies.Empty;
            ClientsProblem = $"{_clientsPath}: {problem}. Every agent request is refused until it is fixed, deleted or reset.";
        }

        var now = _clock.GetUtcNow();
        var cards = McpClientCards.Build(
            _authority?.Clients ?? [],
            _auditEntries,
            _authority?.Session.Identity?.Key ?? string.Empty,
            policies,
            now);

        var signature = string.Join('\n', cards.Select(card => $"{card.Key}|{card.Status}|{card.Connections}|{card.Policy}|{card.LastSeen}"))
            + "\n*|" + policies.For(ClientPolicies.AnyClient) + "|" + ClientsProblem;

        if (!force && string.Equals(signature, _clientsSignature, StringComparison.Ordinal))
        {
            return;
        }

        _clientsSignature = signature;
        _clients =
        [
            .. cards.Select(card => new ClientCardRow(card, card.Policy, now, (row, policy) => SetPolicy(row, policy))),
            new ClientCardRow(null, policies.For(ClientPolicies.AnyClient), now, (row, policy) => SetPolicy(row, policy)),
        ];
        Raise(nameof(Clients));
    }

    private void SetPolicy(ClientCardRow row, ClientPolicy policy)
    {
        if (row.Label is not { } label || !row.CanSetPolicy)
        {
            return;
        }

        if (!ClientPolicies.TryLoad(_clientsPath, out var current, out var problem))
        {
            ClientsProblem = $"{_clientsPath}: {problem}. Every agent request is refused until it is fixed, deleted or reset.";
            return;
        }

        var before = row.Label == ClientPolicies.AnyClient ? current.For(null) : current.For(label);

        if (!ClientPolicies.TrySave(_clientsPath, current.With(label, policy), out var error))
        {
            ClientsProblem = $"{_clientsPath} could not be written: {error}";
            return;
        }

        row.Held(policy);

        // A stricter policy also ends what that client already holds, so this list matches it.
        // The catch-all row cannot name the clients it covers, so it ends every grant instead.
        if (policy != ClientPolicy.SessionGrants && policy != before)
        {
            if (label == ClientPolicies.AnyClient)
            {
                _authority?.RevokeAll();
            }
            else
            {
                _authority?.RevokeClient(label);
            }
        }

        Read(forceHistory: false);
    }

    private void ResetClients()
    {
        if (ClientPolicies.TrySave(_clientsPath, ClientPolicies.Empty, out var error))
        {
            ClientsProblem = string.Empty;
            ReadClients(force: true);
        }
        else
        {
            ClientsProblem = $"{_clientsPath} could not be written: {error}";
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
        _timer.Dispose();
        Connect?.Dispose();
        Tokens?.Dispose();
    }
}
