using Keypaste.Core;
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
/// exposure and every credential request is refused: the app asks through
/// <see cref="NoDesktopApproval"/>, and no standing rule is consulted, so nothing is released
/// without a person's answer.
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
/// </remarks>
internal sealed class SessionHost : IDisposable
{
    private static readonly TimeSpan _stopGrace = TimeSpan.FromSeconds(2);

    private readonly AppVaultSession _session;
    private readonly string? _approverOverride;
    private readonly Func<IApprovalChannel> _approvals;
    private readonly Lock _gate = new();
    private Hosted? _hosted;
    private bool _disposed;

    /// <param name="session">The session whose vault is served.</param>
    /// <param name="approverOverride">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="approvals">Where a person is asked, per unlock; null is <see cref="NoDesktopApproval"/>.</param>
    internal SessionHost(AppVaultSession session, string? approverOverride, Func<IApprovalChannel>? approvals = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _approverOverride = approverOverride;
        _approvals = approvals ?? (() => new NoDesktopApproval());
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

            var hosted = Hosted.TryStart(pipe, vault, _session, lifetime, _approvals(), out var failure);
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
        private readonly AppVaultSession _session;
        private readonly GrantCache _grants;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;

        private Hosted(ApproverListener listener, ApprovalGate approvals, AppVaultSession session, GrantCache grants)
        {
            _listener = listener;
            _approvals = approvals;
            _session = session;
            _grants = grants;

            // An edit in the app withdraws the grants naming what it touched before it is saved (D-0318).
            _session.Edited += OnEdited;
            _run = listener.RunAsync(_stop.Token);
        }

        internal static Hosted? TryStart(
            string pipe,
            VaultIdentity vault,
            AppVaultSession session,
            SessionLifetime lifetime,
            IApprovalChannel channel,
            out string? failure)
        {
            // Owned by the lifetime, which zeroes it when a lock ends it (D-0313).
#pragma warning disable CA2000
            var grants = lifetime.Own(new GrantCache(TimeProvider.System));
#pragma warning restore CA2000
            var approvals = new ApprovalGate(channel, TimeProvider.System, ApprovalLimits.Default);

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => session.UnlockedFor(lifetime)),
                new VaultEntryNameLister(() => session.UnlockedFor(lifetime)),
                approvals,
                grants,
                PolicyGate.None);

            try
            {
                var listener = new ApproverListener(pipe, new SessionAuthority(vault, () => session.Lifetime, handler));
                failure = null;
                return new Hosted(listener, approvals, session, grants);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                approvals.Dispose();
                failure = $"keypaste could not listen for agents: {ex.Message}";
                return null;
            }
        }

        private void OnEdited(object? sender, VaultEdit edit) => _grants.RevokeEntries(edit);

        public void Dispose()
        {
            _session.Edited -= OnEdited;
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
