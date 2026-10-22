using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.App.Session;

/// <summary>
/// Serves the unlocked vault on its endpoint while the session is unlocked, so a
/// <c>keypaste mcp</c> configured for that vault reaches this process (D-0309).
/// </summary>
/// <remarks>
/// <para>
/// Listing is answered under the bridge's own exposure and the agent's settings, and a credential
/// request, or a <c>keypaste run --session</c> request for an env set, is put to the person through the
/// channel the app composes, which launch makes <see cref="WindowApprovalChannel"/> (D-0326, D-0341),
/// unless the person set the agent to Automatic for what it asked. A change to an agent's settings
/// that the person confirms, or one that gives it nothing more, is saved through the session's own save.
/// </para>
/// <para>
/// Everything that answers agents is built per unlock and belongs to that unlock's
/// <see cref="SessionLifetime"/>, so a lock withdraws what is waiting and zeroes the grants given,
/// before the listeners stop (D-0313).
/// </para>
/// <para>
/// A failure to listen does not stop the unlock. The app is a password manager without MCP
/// (docs/PRODUCT.md law 5.6); the failure is reported instead of the endpoint.
/// </para>
/// <para>
/// Where the app's Runs go through Terminal, each unlock is also served on a <see cref="LaunchEndpoint"/>,
/// under a name this host picks once, which only the run Terminal starts asks, so a Run starts whatever
/// agents meet: its authority is built before either endpoint is bound, and each failure to bind is
/// reported apart (D-0451).
/// </para>
/// <para>
/// Every request answered is recorded in the home's audit log before its reply leaves. A log that
/// cannot be opened does not stop the unlock either: each request is refused before anybody is asked,
/// and the next one tries the log again, so mending the file needs no new unlock. A Run, which writes a
/// line on every platform, tries it the same way before its card (D-0451, D-0496).
/// </para>
/// </remarks>
internal sealed class SessionHost : IDisposable
{
    /// <summary>What an agent's settings changed in this app are recorded as having been asked by.</summary>
    internal const string Surface = "keypaste app";

    private const string _notAnswering = "this app is not answering agents for this vault";

    private readonly AppVaultSession _session;
    private readonly string? _approverOverride;
    private readonly Func<IApprovalChannel> _approvals;
    private readonly Action? _requestLock;
    private readonly string? _launchName;
    private readonly HourlyLimit _hourly;
    private readonly Lock _gate = new();
    private Hosted? _hosted;
    private bool _disposed;

    /// <param name="session">The session whose vault is served.</param>
    /// <param name="approverOverride">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="approvals">Where a person is asked, per unlock.</param>
    /// <param name="requestLock">What <c>keypaste lock</c> runs, or null to refuse it.</param>
    /// <param name="servesLaunches">Whether each unlock is also served on a launch endpoint for the run Terminal starts (D-0451).</param>
    internal SessionHost(
        AppVaultSession session,
        string? approverOverride,
        Func<IApprovalChannel> approvals,
        Action? requestLock = null,
        bool servesLaunches = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(approvals);

        _session = session;
        _approverOverride = approverOverride;
        _approvals = approvals;
        _requestLock = requestLock;
        _launchName = servesLaunches ? LaunchEndpoint.NewName() : null;

        // One for the app's life, so locking and unlocking again does not refill an hourly limit.
        _hourly = new HourlyLimit(session.Clock);
        _session.Opened += OnOpened;
        _session.Locked += OnLocked;

        if (_session.IsUnlocked)
        {
            Start();
        }
    }

    /// <summary>The endpoint being served, or null.</summary>
    /// <summary>Raised, on whichever thread answered it, once a terminal's edit is saved in the vault.</summary>
    internal event EventHandler<EditDone>? TerminalEdited;

    internal string? Endpoint { get; private set; }

    /// <summary>Why nothing is being served for an unlocked vault, or null.</summary>
    internal string? Failure { get; private set; }

    /// <summary>Why no launch endpoint is served for an unlocked vault that should have one, or null.</summary>
    internal string? LaunchFailure { get; private set; }

    /// <summary>Why the audit log every request answered here is recorded in does not open now, tried as a request tries it, or null when it opens or nothing is served.</summary>
    internal string? AuditFailure
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.AuditFailure;
            }
        }
    }

    /// <summary>
    /// Why the audit log did not open when a request or a Run's check last tried it this unlock, or null
    /// when it opened then or nothing has tried it; reading it opens nothing, so an unlock alone creates no log.
    /// </summary>
    internal string? LastAuditFailure
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.LastAuditFailure;
            }
        }
    }

    /// <summary>
    /// The session agents reaching <see cref="Endpoint"/> would be answered under, read from the
    /// authority answering them, or null when nothing here answers.
    /// </summary>
    /// <remarks>
    /// Null once the accept loop has ended, and whenever the authority would refuse a request as
    /// locked, whatever a pipe of that name would accept.
    /// </remarks>
    internal (string Session, string Endpoint)? Serving
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.Serving is { } session && Endpoint is { } endpoint ? (session, endpoint) : null;
            }
        }
    }

    /// <summary>
    /// The session the run Terminal starts would be answered under on the launch endpoint, read from the
    /// authority answering it, or null when nothing here answers it.
    /// </summary>
    /// <remarks>Null once the launch endpoint's accept loop has ended, whatever the vault's endpoint does.</remarks>
    internal string? Launching
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.Launching;
            }
        }
    }

    /// <summary>What the authority answering agents has waiting for a person and has granted.</summary>
    /// <remarks>Nothing once the accept loop has ended or the lifetime it answers for has.</remarks>
    internal ApproverActivity Activity
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.Activity ?? ApproverActivity.None;
            }
        }
    }

    /// <summary>The bridges attached to the live session that said who their client is.</summary>
    internal IReadOnlyList<ConnectedClient> Clients
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.Clients ?? [];
            }
        }
    }

    /// <summary>What the live session has released, newest first.</summary>
    internal IReadOnlyList<ReleaseSeen> Released
    {
        get
        {
            lock (_gate)
            {
                return _hosted?.Released ?? [];
            }
        }
    }

    /// <summary>What applies to an agent's requests now, or null when nothing here answers or its settings cannot be read.</summary>
    /// <param name="label">The bridge's raw label, or null.</param>
    /// <returns>What applies, or null.</returns>
    internal EffectiveAccess? Access(string? label)
    {
        lock (_gate)
        {
            return _hosted?.Access(label);
        }
    }

    /// <summary>One agent's settings as the person's own screen shows them.</summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    /// <param name="settings">Its settings, when they could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>Whether they were read; never while nothing here answers agents.</returns>
    internal bool TrySettings(string agent, [NotNullWhen(true)] out AgentSettingsState? settings, out string problem)
    {
        Hosted? hosted;

        lock (_gate)
        {
            hosted = _hosted;
        }

        // Outside the lock, which a lock of the session takes: reading the settings reads the session's vault.
        if (hosted is null)
        {
            settings = null;
            problem = _notAnswering;
            return false;
        }

        return hosted.TrySettings(agent, out settings, out problem);
    }

    /// <summary>Changes an agent's settings for the person at this app, recorded on its own line as keypaste app.</summary>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="cancellationToken">Cancelled when the screen stops waiting, which withdraws a confirmation on screen.</param>
    /// <returns>What became of it.</returns>
    internal ValueTask<AccessChangeReply> ChangeAccessAsync(string agent, AccessEdit edit, CancellationToken cancellationToken)
    {
        Hosted? hosted;

        lock (_gate)
        {
            hosted = _hosted;
        }

        // Outside the lock: a change that gives more waits for the person, and a lock must not wait for it.
        return hosted?.ChangeAccessAsync(agent, edit, cancellationToken)
            ?? ValueTask.FromResult(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _notAnswering));
    }

    /// <summary>Resets this computer's ceilings, which keypaste cannot read, once the person confirms what each agent then gets.</summary>
    /// <param name="cancellationToken">Cancelled when the screen stops waiting, which withdraws the confirmation.</param>
    /// <returns>What became of it.</returns>
    internal ValueTask<AccessChangeReply> ResetCapsAsync(CancellationToken cancellationToken)
    {
        Hosted? hosted;

        lock (_gate)
        {
            hosted = _hosted;
        }

        return hosted?.ResetCapsAsync(cancellationToken)
            ?? ValueTask.FromResult(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _notAnswering));
    }

    /// <summary>
    /// Brings the prompt waiting for the person forward with its allows disarmed for another second (D-0456).
    /// Called on the UI thread; nothing on the endpoint reaches it.
    /// </summary>
    /// <returns>Whether a prompt was waiting on screen.</returns>
    internal bool ShowPrompt()
    {
        Hosted? hosted;

        lock (_gate)
        {
            hosted = _hosted;
        }

        return hosted?.ShowPrompt() == true;
    }

    /// <summary>Ends every grant given to a bridge started with this label.</summary>
    /// <param name="label">The raw label.</param>
    internal void RevokeClient(string label)
    {
        lock (_gate)
        {
            _hosted?.RevokeClient(label);
        }
    }

    /// <summary>Ends one grant in the authority, so the next matching request is asked again.</summary>
    /// <param name="key">The grant, as <see cref="Activity"/> listed it.</param>
    internal void Revoke(GrantKey key)
    {
        lock (_gate)
        {
            _hosted?.Revoke(key);
        }
    }

    /// <summary>Ends one timed grant a person gave a repeated <c>keypaste run --session</c>.</summary>
    /// <param name="key">The grant, as <see cref="ApproverActivity.EnvGrants"/> listed it.</param>
    internal void RevokeEnvGrant(string key)
    {
        lock (_gate)
        {
            _hosted?.RevokeEnvGrant(key);
        }
    }

    /// <summary>Ends every grant in the authority.</summary>
    internal void RevokeAll()
    {
        lock (_gate)
        {
            _hosted?.RevokeAll();
        }
    }

    /// <summary>Issues the pass a Run confirmed in the app hands the <c>keypaste run --session</c> Terminal starts on macOS (D-0452).</summary>
    /// <param name="claim">The request the confirmation named, and the set it showed.</param>
    /// <param name="lifetimeId">The session the confirmation was answered in.</param>
    /// <returns>The pass and the launch endpoint that run must ask, read together, or null when no launch endpoint listens or that session has ended.</returns>
    internal (string Pass, string Endpoint)? IssueLaunch(LaunchClaim claim, string lifetimeId)
    {
        lock (_gate)
        {
            return _hosted?.IssueLaunch(claim, lifetimeId);
        }
    }

    /// <summary>Forgets a pass Terminal was never handed.</summary>
    /// <param name="pass">The pass <see cref="IssueLaunch"/> returned.</param>
    internal void WithdrawLaunch(string pass)
    {
        lock (_gate)
        {
            _hosted?.WithdrawLaunch(pass);
        }
    }

    /// <summary>Records a Run the app starts itself on Windows and Linux in the authority's log, before the terminal starts (D-0496).</summary>
    /// <param name="set">The set the confirmation released.</param>
    /// <param name="lifetimeId">The session the confirmation was answered in.</param>
    /// <returns>Whether the line was written; false when nothing is hosted for that session.</returns>
    internal bool RecordRun(Core.EnvResolved set, string lifetimeId)
    {
        lock (_gate)
        {
            return _hosted?.RecordRun(set, lifetimeId) == true;
        }
    }

    /// <summary>The grants in force, as <c>keypaste grants</c> lists them: names and ids, never a value.</summary>
    internal IReadOnlyList<GrantSummary> Grants() => GrantSummary.Of(Activity);

    /// <summary>Ends the grant with this id, as <c>keypaste grants revoke</c> names it.</summary>
    /// <param name="id">The grant's id, from <see cref="Grants"/>.</param>
    /// <returns>Whether a grant with that id was in force.</returns>
    internal bool Revoke(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_gate)
        {
            var activity = _hosted?.Activity ?? ApproverActivity.None;

            if (activity.Grants.FirstOrDefault(
                    inForce => string.Equals(GrantId.Of(inForce.Key), id, StringComparison.OrdinalIgnoreCase)) is { } grant)
            {
                _hosted!.Revoke(grant.Key);
                return true;
            }

            if (activity.EnvGrants.FirstOrDefault(
                    inForce => string.Equals(GrantId.OfEnv(inForce.Key), id, StringComparison.OrdinalIgnoreCase)) is { } envGrant)
            {
                _hosted!.RevokeEnvGrant(envGrant.Key);
                return true;
            }

            return false;
        }
    }

    private void OnOpened(object? sender, EventArgs e) => Start();

    private void OnLocked(object? sender, VaultLockReason reason) => Stop();

    private void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StopLocked();

            if (_session.Identity is not { } vault || _session.Lifetime is not { } lifetime)
            {
                return;
            }

            var hosted = Hosted.Start(vault, _session, lifetime, _approvals(), _requestLock, _hourly, _approverOverride, _launchName, done => TerminalEdited?.Invoke(this, done));
            _hosted = hosted;
            Endpoint = hosted.Endpoint;
            Failure = hosted.Failure;
            LaunchFailure = hosted.LaunchFailure;
        }
    }

    private void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    private void StopLocked()
    {
        var hosted = _hosted;
        _hosted = null;
        Endpoint = null;
        Failure = null;
        LaunchFailure = null;
        hosted?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session.Opened -= OnOpened;
            _session.Locked -= OnLocked;
            StopLocked();
        }
    }

    /// <summary>What answers for one unlock, and the endpoints it is served on.</summary>
    private sealed class Hosted : IDisposable
    {
        private readonly SessionServer _server;
        private readonly AuditOpener _audit;
        private readonly IApprovalChannel _channel;
        private readonly LaunchEndpoint? _launches;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task? _agents;
        private readonly Task? _launching;

        private Hosted(SessionServer server, AuditOpener audit, IApprovalChannel channel, string? endpoint, string? failure, LaunchEndpoint? launches, string? launchFailure)
        {
            _server = server;
            _audit = audit;
            _channel = channel;
            _launches = launches;
            Endpoint = endpoint;
            Failure = failure;
            LaunchFailure = launchFailure;
            _agents = endpoint is null ? null : server.RunAsync(_stop.Token);
            _launching = launches?.RunAsync(_stop.Token);
        }

        /// <summary>The vault's endpoint, or null when it could not be bound.</summary>
        internal string? Endpoint { get; }

        internal string? Failure { get; }

        internal string? LaunchFailure { get; }

        internal string? AuditFailure => _audit.Failure;

        internal string? LastAuditFailure => _audit.LastFailure;

        /// <summary>The session the authority answers agents under while the vault's endpoint still accepts, or null.</summary>
        internal string? Serving => AnswersAgents ? _server.Authority.Serving : null;

        /// <summary>The session the authority answers the run Terminal starts under while the launch endpoint still accepts, or null.</summary>
        internal string? Launching => _launching is { IsCompleted: false } ? _server.Authority.Serving : null;

        internal ApproverActivity Activity => AnswersAgents ? _server.Authority.Activity : ApproverActivity.None;

        internal IReadOnlyList<ConnectedClient> Clients => AnswersAgents ? _server.Authority.Clients : [];

        internal IReadOnlyList<ReleaseSeen> Released => _server.Authority.Released;

        private bool AnswersAgents => _agents is { IsCompleted: false };

        internal void RevokeClient(string label) => _server.Authority.RevokeClient(label);

        internal bool ShowPrompt() => AnswersAgents && _channel is WindowApprovalChannel prompt && prompt.BringForward();

        internal EffectiveAccess? Access(string? label) => AnswersAgents ? _server.Authority.Access(label) : null;

        internal bool TrySettings(string agent, [NotNullWhen(true)] out AgentSettingsState? settings, out string problem)
        {
            if (!AnswersAgents)
            {
                settings = null;
                problem = _notAnswering;
                return false;
            }

            return _server.Authority.TrySettings(agent, out settings, out problem);
        }

        internal ValueTask<AccessChangeReply> ChangeAccessAsync(string agent, AccessEdit edit, CancellationToken cancellationToken) =>
            !AnswersAgents
                ? ValueTask.FromResult(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _notAnswering))
                : _server.Authority.ChangeAccessAsync(agent, edit, Surface, cancellationToken);

        internal ValueTask<AccessChangeReply> ResetCapsAsync(CancellationToken cancellationToken) =>
            !AnswersAgents
                ? ValueTask.FromResult(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _notAnswering))
                : _server.Authority.ResetCapsAsync(Surface, cancellationToken);

        internal void Revoke(GrantKey key) => _server.Authority.Revoke(key);

        internal void RevokeEnvGrant(string key) => _server.Authority.RevokeEnvGrant(key);

        internal void RevokeAll() => _server.Authority.RevokeAll();

        internal (string Pass, string Endpoint)? IssueLaunch(LaunchClaim claim, string lifetimeId) =>
            _launches is { } launches && _launching is { IsCompleted: false } && _server.Authority.IssueLaunch(claim, lifetimeId) is { } pass
                ? (pass, launches.Name)
                : null;

        internal void WithdrawLaunch(string pass) => _server.Authority.WithdrawLaunch(pass);

        internal bool RecordRun(Core.EnvResolved set, string lifetimeId) => _server.Authority.RecordAppRun(set, lifetimeId);

        /// <summary>Builds the unlock's authority, then binds its launch endpoint when it has one and the vault's endpoint, each reporting why it could not be bound.</summary>
        internal static Hosted Start(
            VaultIdentity vault,
            AppVaultSession session,
            SessionLifetime lifetime,
            IApprovalChannel channel,
            Action? requestLock,
            HourlyLimit hourly,
            string? approverOverride,
            string? launchName,
            Action<EditDone> edited)
        {
            AuditOpener? audit = new(KeypasteHome.AuditPath(session.Home));
            SessionServer? server = null;
            LaunchEndpoint? launches = null;

            try
            {
                server = SessionServer.Build(new SessionServerOptions
                {
                    Vault = vault,
                    Lifetime = lifetime,
                    CurrentLifetime = () => session.Lifetime,
                    UnlockedFor = session.UnlockedFor,
                    Channel = channel,
                    // Saved only on this unlock's vault, as a terminal's edit is, never on a later one's.
                    Settings = new VaultAgentSettings(
                        () => session.UnlockedFor(lifetime),
                        edit => Saved(session.WriteFor(lifetime, edit, changed => changed)),
                        new AccessCapsSource(KeypasteHome.ClientsPath(session.Home))),
                    Hourly = hourly,
                    Audit = audit.Open,
                    WatchEdits = edited => session.Edited += edited,
                    UnwatchEdits = edited => session.Edited -= edited,
                    LockNow = requestLock,
                    Clock = session.Clock,

                    // A terminal's edit, once the person allows it in the prompt window, is saved
                    // through this session, never on a later unlock's vault (D-0464).
                    WriteEdit = (asked, edit) => Saved(session.WriteFor(asked, edit, changed => changed)),
                    ClientsPath = KeypasteHome.ClientsPath(session.Home),
                    Edited = edited,
                });

                string? launchFailure = null;

                if (launchName is not null)
                {
                    try
                    {
                        launches = LaunchEndpoint.Listen(launchName, server.Authority);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
                    {
                        launchFailure = $"keypaste could not open a private connection for Terminal: {ex.Message}";
                    }
                }

                var (endpoint, failure) = BindForAgents(server, approverOverride, vault);
                var hosted = new Hosted(server, audit, channel, endpoint, failure, launches, launchFailure);
                audit = null;
                server = null;
                launches = null;
                return hosted;
            }
            finally
            {
                launches?.Dispose();
                server?.Dispose();
                audit?.Dispose();
            }
        }

        /// <summary>Binds the vault's endpoint, returning its name, or why it could not be bound.</summary>
        private static (string? Endpoint, string? Failure) BindForAgents(SessionServer server, string? approverOverride, VaultIdentity vault)
        {
            string pipe;

            try
            {
                pipe = ApproverEndpoint.Resolve(null, approverOverride, vault)!;
            }
            catch (ArgumentException ex)
            {
                return (null, $"the {ApproverEndpoint.EnvironmentVariable} setting cannot name a pipe: {ex.Message}");
            }

            try
            {
                server.Bind(pipe);
                return (pipe, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
            {
                return (null, $"keypaste could not listen for agents: {ex.Message}");
            }
        }

        /// <summary>What saving an agent's settings through the session's own save came to.</summary>
        private static AgentSave Saved(WriteResult<bool> written) => written.Outcome switch
        {
            WriteOutcome.Saved => AgentSave.Saved,
            WriteOutcome.NothingToSave => AgentSave.Unchanged,
            WriteOutcome.Locked => new AgentSave(AgentSaveOutcome.Locked, "the vault is locked"),
            WriteOutcome.ChangedOnDisk => new AgentSave(AgentSaveOutcome.ChangedOnDisk, "another program changed the vault since the app opened it; reload it in the app first"),
            _ => new AgentSave(AgentSaveOutcome.Failed, written.Reason ?? "the vault could not be saved"),
        };

        public void Dispose()
        {
            _stop.Cancel();

            // No time limit (D-0392): reads end on the stop token, each delivery ends a second after it (D-0420), and every handler must finish once withdrawn or stopped.
            Stopped(_agents);
            Stopped(_launching);

            _server.Dispose();
            _launches?.Dispose();
            _audit.Dispose();
            _stop.Dispose();
        }

        private static void Stopped(Task? accepting)
        {
            try
            {
                accepting?.Wait();
            }
            catch (AggregateException)
            {
                // Stopping is the answer either way; a connection that faulted has already ended.
            }
        }
    }

    /// <summary>
    /// The home's audit log, opened at the first request or a Run's check before its card, so an
    /// unlock alone creates no log, and tried again at the next after an open that failed.
    /// </summary>
    private sealed class AuditOpener(string path) : IDisposable
    {
        private readonly Lock _gate = new();
        private AuditLog? _log;
        private string? _lastFailure;
        private bool _disposed;

        /// <summary>Why the log does not open now, or null when it opens.</summary>
        internal string? Failure => TryOpen(out var failure) is null ? failure : null;

        /// <summary>Why the log did not open when it was last tried, or null when it opened or has not been tried; reading it opens nothing.</summary>
        internal string? LastFailure
        {
            get
            {
                lock (_gate)
                {
                    return _lastFailure;
                }
            }
        }

        /// <summary>The log, or null while it cannot be opened, which refuses every request.</summary>
        internal AuditLog? Open() => TryOpen(out _);

        private AuditLog? TryOpen(out string? failure)
        {
            lock (_gate)
            {
                failure = null;

                if (_log is null && !_disposed)
                {
                    if (AuditLog.TryOpen(path, TimeProvider.System, out var opened, out var error))
                    {
                        _log = opened;
                    }
                    else
                    {
                        failure = error;
                    }

                    _lastFailure = failure;
                }

                return _log;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _log?.Dispose();
                _log = null;
            }
        }
    }
}
