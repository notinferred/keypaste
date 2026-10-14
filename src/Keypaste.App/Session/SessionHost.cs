using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
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
/// before the listener stops (D-0313).
/// </para>
/// <para>
/// A failure to listen does not stop the unlock. The app is a password manager without MCP
/// (docs/PRODUCT.md law 5.6); the failure is reported instead of the endpoint.
/// </para>
/// <para>
/// Every request answered is recorded in the home's audit log before its reply leaves. A log that
/// cannot be opened does not stop the unlock either: each request is refused before anybody is asked,
/// and the next one tries the log again, so mending the file needs no new unlock.
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
    private readonly HourlyLimit _hourly;
    private readonly Lock _gate = new();
    private Hosted? _hosted;
    private bool _disposed;

    /// <param name="session">The session whose vault is served.</param>
    /// <param name="approverOverride">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="approvals">Where a person is asked, per unlock.</param>
    /// <param name="requestLock">What <c>keypaste lock</c> runs, or null to refuse it.</param>
    internal SessionHost(
        AppVaultSession session,
        string? approverOverride,
        Func<IApprovalChannel> approvals,
        Action? requestLock = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(approvals);

        _session = session;
        _approverOverride = approverOverride;
        _approvals = approvals;
        _requestLock = requestLock;

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
    internal string? Endpoint { get; private set; }

    /// <summary>Why nothing is being served for an unlocked vault, or null.</summary>
    internal string? Failure { get; private set; }

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

            string pipe;

            try
            {
                pipe = ApproverEndpoint.Resolve(null, _approverOverride, vault)!;
            }
            catch (ArgumentException ex)
            {
                Failure = $"the {ApproverEndpoint.EnvironmentVariable} setting cannot name a pipe: {ex.Message}";
                return;
            }

            var hosted = Hosted.TryStart(pipe, vault, _session, lifetime, _approvals(), _requestLock, _hourly, out var failure);
            _hosted = hosted;
            Endpoint = hosted is null ? null : pipe;
            Failure = failure;
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

    /// <summary>One listener and what it answers with, for one unlock.</summary>
    private sealed class Hosted : IDisposable
    {
        private readonly SessionServer _server;
        private readonly AuditOpener _audit;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;

        private Hosted(SessionServer server, AuditOpener audit)
        {
            _server = server;
            _audit = audit;
            _run = server.RunAsync(_stop.Token);
        }

        /// <summary>The session the authority answers under while the listener still accepts, or null.</summary>
        internal string? Serving => _run.IsCompleted ? null : _server.Authority.Serving;

        internal ApproverActivity Activity => _run.IsCompleted ? ApproverActivity.None : _server.Authority.Activity;

        internal IReadOnlyList<ConnectedClient> Clients => _run.IsCompleted ? [] : _server.Authority.Clients;

        internal IReadOnlyList<ReleaseSeen> Released => _server.Authority.Released;

        internal void RevokeClient(string label) => _server.Authority.RevokeClient(label);

        internal EffectiveAccess? Access(string? label) => _run.IsCompleted ? null : _server.Authority.Access(label);

        internal bool TrySettings(string agent, [NotNullWhen(true)] out AgentSettingsState? settings, out string problem)
        {
            if (_run.IsCompleted)
            {
                settings = null;
                problem = _notAnswering;
                return false;
            }

            return _server.Authority.TrySettings(agent, out settings, out problem);
        }

        internal ValueTask<AccessChangeReply> ChangeAccessAsync(string agent, AccessEdit edit, CancellationToken cancellationToken) =>
            _run.IsCompleted
                ? ValueTask.FromResult(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _notAnswering))
                : _server.Authority.ChangeAccessAsync(agent, edit, Surface, cancellationToken);

        internal ValueTask<AccessChangeReply> ResetCapsAsync(CancellationToken cancellationToken) =>
            _run.IsCompleted
                ? ValueTask.FromResult(new AccessChangeReply(AuditDecision.Denied, AuditMethod.VaultLocked, _notAnswering))
                : _server.Authority.ResetCapsAsync(Surface, cancellationToken);

        internal void Revoke(GrantKey key) => _server.Authority.Revoke(key);

        internal void RevokeEnvGrant(string key) => _server.Authority.RevokeEnvGrant(key);

        internal void RevokeAll() => _server.Authority.RevokeAll();

        internal static Hosted? TryStart(
            string pipe,
            VaultIdentity vault,
            AppVaultSession session,
            SessionLifetime lifetime,
            IApprovalChannel channel,
            Action? requestLock,
            HourlyLimit hourly,
            out string? failure)
        {
            AuditOpener? audit = new(KeypasteHome.AuditPath(session.Home));

            try
            {
                var server = SessionServer.Listen(pipe, new SessionServerOptions
                {
                    Vault = vault,
                    Lifetime = lifetime,
                    CurrentLifetime = () => session.Lifetime,
                    UnlockedFor = session.UnlockedFor,
                    Channel = channel,
                    Settings = new VaultAgentSettings(
                        () => session.UnlockedFor(lifetime),
                        edit => Saved(session.Write(edit, changed => changed)),
                        new AccessCapsSource(KeypasteHome.ClientsPath(session.Home))),
                    Hourly = hourly,
                    Audit = audit.Open,
                    WatchEdits = edited => session.Edited += edited,
                    UnwatchEdits = edited => session.Edited -= edited,
                    LockNow = requestLock,
                    Clock = session.Clock,
                });

                failure = null;
                var hosted = new Hosted(server, audit);
                audit = null;
                return hosted;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = $"keypaste could not listen for agents: {ex.Message}";
                return null;
            }
            finally
            {
                audit?.Dispose();
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

            try
            {
                // No time limit (D-0392): reads end on the stop token, each delivery ends a second after it (D-0420), and every handler must finish once withdrawn or stopped.
                _run.Wait();
            }
            catch (AggregateException)
            {
                // Stopping is the answer either way; a connection that faulted has already ended.
            }

            _server.Dispose();
            _audit.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>
    /// The home's audit log, opened at the first request, so an unlock creates no log, and tried again
    /// on the next request after an open that failed.
    /// </summary>
    private sealed class AuditOpener(string path) : IDisposable
    {
        private readonly Lock _gate = new();
        private AuditLog? _log;
        private bool _disposed;

        /// <summary>The log, or null while it cannot be opened, which refuses every request.</summary>
        internal AuditLog? Open()
        {
            lock (_gate)
            {
                if (_log is null && !_disposed && AuditLog.TryOpen(path, TimeProvider.System, out var opened, out _))
                {
                    _log = opened;
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
