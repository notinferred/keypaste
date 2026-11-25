using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;

namespace Keypaste.Cli.Tests;

/// <summary>
/// A real <see cref="SessionAuthority"/> behind a real <see cref="ApproverListener"/> on a pipe of its
/// own, holding the harness's vault.
/// </summary>
/// <remarks>
/// The verbs it serves never open the vault, so no vault is unlocked here: the owner answers from its
/// grant cache, which is where a person's approvals live, and from agents' settings a test holds in
/// memory. It records in the harness home's log only when a test asks it to.
/// </remarks>
internal sealed class SessionOwner : IAsyncDisposable
{
    /// <summary>What every grant here holds, so a test can prove it never reaches the terminal.</summary>
    internal const string Sentinel = "sk_live_session_owner_sentinel";

    private readonly ApprovalGate _gate;
    private readonly ServedOwner _served;
    private readonly AuditLog? _log;

    private SessionOwner(CliHarness harness, LockKind locking, bool records)
    {
        var pipe = ServedOwner.NewPipeName();
        harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;

        if (records && !AuditLog.TryOpen(KeypasteHome.AuditPath(harness.Environment[KeypasteHome.EnvironmentVariable]), TimeProvider.System, out _log, out var error))
        {
            throw new InvalidOperationException(error);
        }

        _gate = new ApprovalGate(Asked, TimeProvider.System, ApprovalLimits.Default);

        Action? lockNow = locking switch
        {
            LockKind.StopsListening => StopListening,
            LockKind.KeepsListening => Lifetime.End,
            _ => null,
        };

        _served = ServedOwner.Serve(
            new SessionAuthority(
                VaultIdentity.Of(KeypasteHome.Resolve(harness.Environment[KeypasteHome.EnvironmentVariable]), harness.VaultPath),
                () => Lifetime,
                ServedOwner.VaultHandler(() => null, _gate, Settings),
                audit: () => _log,
                lockNow: lockNow),
            pipe);
    }

    /// <summary>What <c>keypaste lock</c> does to this owner.</summary>
    internal enum LockKind
    {
        /// <summary>Refuses it, as an owner composed without a lock action does.</summary>
        None,

        /// <summary>Ends the lifetime and stops listening, as <c>keypaste approve</c> does.</summary>
        StopsListening,

        /// <summary>Ends the lifetime and keeps listening, as the desktop does until its host stops.</summary>
        KeepsListening,
    }

    internal string Pipe => _served.PipeName;

    /// <summary>The agents' rules this owner decides by.</summary>
    internal FakeAgentRules Settings { get; } = new();

    /// <summary>Where a person would be asked: every credential is refused, and a change is answered as a test says.</summary>
    internal NobodyAsked Asked { get; } = new();

    internal SessionAuthority Authority => _served.Authority;

    internal SessionLifetime Lifetime { get; } = new();

    /// <summary>Stops listening once the lifetime has ended and the listener has wound down.</summary>
    internal Task Stopped => _served.Stopped;

    internal static SessionOwner Start(CliHarness harness, LockKind locking = LockKind.None, bool records = false) => new(harness, locking, records);

    private void StopListening()
    {
        Lifetime.End();
        _served.StopListening();
    }

    public async ValueTask DisposeAsync()
    {
        await _served.DisposeAsync();
        Lifetime.Dispose();
        _gate.Dispose();
        _log?.Dispose();
    }

    internal sealed class NobodyAsked : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

    }
}
