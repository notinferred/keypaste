using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;

namespace Keypaste.Cli.Tests;

/// <summary>
/// A real <see cref="SessionAuthority"/> behind a real <see cref="ApproverListener"/> on a pipe of its
/// own, holding the harness's vault, with grants a test gives it directly.
/// </summary>
/// <remarks>
/// The grants verbs never open the vault, so no vault is unlocked here: the owner answers from its
/// grant cache, which is where a person's approvals live, and from agents' settings a test holds in
/// memory. It records in the harness home's log only when a test asks it to.
/// </remarks>
internal sealed class SessionOwner : IAsyncDisposable
{
    /// <summary>What every grant here holds, so a test can prove it never reaches the terminal.</summary>
    internal const string Sentinel = "sk_live_grants_verb_sentinel";

    private readonly GrantCache _grants = new(TimeProvider.System);
    private readonly ApprovalGate _gate;
    private readonly ApproverListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _running;
    private readonly AuditLog? _log;

    private SessionOwner(CliHarness harness, LockKind locking, bool records)
    {
        Pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        harness.Environment[ApproverEndpoint.EnvironmentVariable] = Pipe;
        Lifetime.Own(_grants);

        if (records && !AuditLog.TryOpen(KeypasteHome.AuditPath(harness.Environment[KeypasteHome.EnvironmentVariable]), TimeProvider.System, out _log, out var error))
        {
            throw new InvalidOperationException(error);
        }

        _gate = new ApprovalGate(Asked, TimeProvider.System, ApprovalLimits.Default);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => null),
            new VaultEntryNameLister(() => null),
            _gate,
            _grants,
            Settings);

        Action? lockNow = locking switch
        {
            LockKind.StopsListening => StopListening,
            LockKind.KeepsListening => Lifetime.End,
            _ => null,
        };

        Authority = new SessionAuthority(
            VaultIdentity.Of(KeypasteHome.Resolve(harness.Environment[KeypasteHome.EnvironmentVariable]), harness.VaultPath),
            () => Lifetime,
            handler,
            audit: () => _log,
            lockNow: lockNow);

        _listener = new ApproverListener(Pipe, Authority);
        _running = _listener.RunAsync(_stop.Token);
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

    internal string Pipe { get; }

    /// <summary>The agents' settings this owner reads and writes.</summary>
    internal FakeAgentSettings Settings { get; } = new();

    /// <summary>Where a person would be asked: every credential is refused, and a change is answered as a test says.</summary>
    internal NobodyAsked Asked { get; } = new();

    internal SessionAuthority Authority { get; }

    internal SessionLifetime Lifetime { get; } = new();

    /// <summary>Stops listening once the lifetime has ended and the listener has wound down.</summary>
    internal Task Stopped => _running;

    internal static SessionOwner Start(CliHarness harness, LockKind locking = LockKind.None, bool records = false) => new(harness, locking, records);

    /// <summary>Gives a grant, as a person approving an agent's request would.</summary>
    /// <returns>The id <c>keypaste grants</c> names it by.</returns>
    internal string Grant(string client, string group, string title, string field = "password", int seconds = 2520)
    {
        var entry = new EntryName(group, title);
        var key = new GrantKey("conn-" + client, EntryHandle.For(entry), field);

        using var value = new ReleasedField(field, Sentinel);
        _grants.Store(key, value, TimeSpan.FromSeconds(seconds), ApprovalPrompt.For(client, entry, field, "deploy", seconds));

        return GrantId.Of(key);
    }

    private void StopListening()
    {
        Lifetime.End();
        _stop.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        try
        {
            await _running;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Tearing the listener down is how it stops.
        }

        _listener.Dispose();
        Lifetime.Dispose();
        _gate.Dispose();
        _grants.Dispose();
        _log?.Dispose();
        _stop.Dispose();
    }

    internal sealed class NobodyAsked : IApprovalChannel
    {
        /// <summary>How a confirmation of a change that gives an agent more is answered; a no unless a test says otherwise.</summary>
        internal ApprovalAnswer Change { get; set; } = ApprovalAnswer.Denied;

        /// <summary>The last such confirmation asked.</summary>
        internal AccessChangePrompt? LastChange { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
        {
            LastChange = prompt;
            return ValueTask.FromResult(Change);
        }
    }
}
