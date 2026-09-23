using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;

namespace Keypaste.App.Session;

/// <summary>
/// Serves the unlocked vault on its endpoint while the session is unlocked, so a
/// <c>keypaste-mcp</c> configured for that vault reaches this process (D-0309).
/// </summary>
/// <remarks>
/// <para>
/// Until approving in the desktop exists (STEPS 4.4), listing is answered under the bridge's own
/// exposure and every credential request is refused: there is nowhere in the app to ask a person,
/// and no standing rule is consulted, so nothing is released without a person's answer.
/// </para>
/// <para>
/// A failure to listen does not stop the unlock. The app is a password manager without MCP
/// (docs/PRODUCT.md law 5.6); the failure is reported instead of the endpoint.
/// </para>
/// </remarks>
internal sealed class SessionHost : IDisposable
{
    private static readonly TimeSpan _stopGrace = TimeSpan.FromSeconds(2);

    private readonly AppVaultSession _session;
    private readonly string? _approverOverride;
    private readonly Lock _gate = new();
    private Hosted? _hosted;
    private bool _disposed;

    /// <param name="session">The session whose vault is served.</param>
    /// <param name="approverOverride">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    internal SessionHost(AppVaultSession session, string? approverOverride)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _approverOverride = approverOverride;
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

            if (_session.Identity is not { } vault)
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

            var hosted = Hosted.TryStart(pipe, vault, _session, out var failure);
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
        private readonly ApproverListener _listener;
        private readonly ApprovalGate _approvals;
        private readonly GrantCache _grants;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;

        private Hosted(ApproverListener listener, ApprovalGate approvals, GrantCache grants)
        {
            _listener = listener;
            _approvals = approvals;
            _grants = grants;
            _run = listener.RunAsync(_stop.Token);
        }

        internal static Hosted? TryStart(string pipe, VaultIdentity vault, AppVaultSession session, out string? failure)
        {
            var grants = new GrantCache(TimeProvider.System);
            var approvals = new ApprovalGate(new NoDesktopApproval(), TimeProvider.System, ApprovalLimits.Default);

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => session.Unlocked),
                new VaultEntryNameLister(() => session.Unlocked),
                approvals,
                grants,
                PolicyGate.None);

            try
            {
                var listener = new ApproverListener(pipe, new SessionAuthority(vault, () => session.SessionId, handler));
                failure = null;
                return new Hosted(listener, approvals, grants);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                approvals.Dispose();
                grants.Dispose();
                failure = $"keypaste could not listen for agents: {ex.Message}";
                return null;
            }
        }

        public void Dispose()
        {
            _stop.Cancel();

            try
            {
                _run.Wait(_stopGrace);
            }
            catch (AggregateException)
            {
                // Stopping is the answer either way; a connection that faulted has already ended.
            }

            _listener.Dispose();
            _approvals.Dispose();
            _grants.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>The app has nowhere to ask a person yet, so every request that needs one is refused.</summary>
    private sealed class NoDesktopApproval : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.NoChannel);
    }
}
