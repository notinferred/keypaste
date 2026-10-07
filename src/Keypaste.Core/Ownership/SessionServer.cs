using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;

namespace Keypaste.Core.Ownership;

/// <summary>How one owner answers agents for one unlock: everything that differs between the desktop and <c>keypaste agent</c>.</summary>
public sealed record SessionServerOptions
{
    /// <summary>The vault served.</summary>
    public required VaultIdentity Vault { get; init; }

    /// <summary>The unlock this serves, which owns the grants and zeroes them when it ends (D-0313).</summary>
    public required SessionLifetime Lifetime { get; init; }

    /// <summary>The owner's live lifetime now, which a request must belong to (D-0310).</summary>
    public required Func<SessionLifetime?> CurrentLifetime { get; init; }

    /// <summary>The vault while a lifetime is live, otherwise null.</summary>
    public required Func<SessionLifetime, Vault?> UnlockedFor { get; init; }

    /// <summary>Where a person is asked.</summary>
    public required IApprovalChannel Channel { get; init; }

    /// <summary>How many requests wait for a person, and for how long.</summary>
    public ApprovalLimits Limits { get; init; } = ApprovalLimits.Default;

    /// <summary>The standing rules consulted before a person is asked; <see cref="PolicyGate.None"/> asks the person every time.</summary>
    public required PolicyGate Policy { get; init; }

    /// <summary>Per-client policy, from <c>clients.toml</c>.</summary>
    public required ClientPolicySource Clients { get; init; }

    /// <summary>Opens the audit log a token or an env release is recorded in, or returns null when it cannot.</summary>
    public required Func<AuditLog?> Audit { get; init; }

    /// <summary>Subscribes to the owner's edits, which withdraw the grants naming what they touched before they are saved (D-0318).</summary>
    public required Action<EventHandler<VaultEdit>> WatchEdits { get; init; }

    /// <summary>Unsubscribes what <see cref="WatchEdits"/> subscribed.</summary>
    public required Action<EventHandler<VaultEdit>> UnwatchEdits { get; init; }

    /// <summary>What <c>keypaste lock</c> runs, or null to refuse it.</summary>
    public Action? LockNow { get; init; }

    /// <summary>Where the owner says what it decided, or null.</summary>
    public Action<string>? Narrate { get; init; }

    /// <summary>What time it is.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>
/// The grants, the gate, the handler, the authority and the listener one owner answers agents with for
/// one unlock, built once so the desktop and <c>keypaste agent</c> cannot assemble them differently.
/// </summary>
public sealed class SessionServer : IDisposable
{
    private readonly ApproverListener _listener;
    private readonly ApprovalGate _gate;
    private readonly GrantCache _grants;
    private readonly EnvGrantCache _envGrants;
    private readonly Action<EventHandler<VaultEdit>> _unwatchEdits;
    private bool _disposed;

    private SessionServer(
        ApproverListener listener,
        SessionAuthority authority,
        ApprovalGate gate,
        GrantCache grants,
        EnvGrantCache envGrants,
        Action<EventHandler<VaultEdit>> watchEdits,
        Action<EventHandler<VaultEdit>> unwatchEdits)
    {
        _listener = listener;
        Authority = authority;
        _gate = gate;
        _grants = grants;
        _envGrants = envGrants;
        _unwatchEdits = unwatchEdits;
        watchEdits(OnEdited);
    }

    /// <summary>What answers each request.</summary>
    public SessionAuthority Authority { get; }

    /// <summary>Builds what answers agents for one unlock and binds its endpoint.</summary>
    /// <param name="pipeName">The vault's endpoint, as <see cref="ApproverEndpoint"/> names it.</param>
    /// <param name="options">How this owner differs.</param>
    /// <returns>The server, bound and not yet accepting; <see cref="RunAsync"/> accepts.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="IOException">The name is already taken. Nothing built is left behind.</exception>
    /// <exception cref="UnauthorizedAccessException">The name cannot be bound. Nothing built is left behind.</exception>
    public static SessionServer Listen(string pipeName, SessionServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(pipeName);
        ArgumentNullException.ThrowIfNull(options);

        var lifetime = options.Lifetime;

#pragma warning disable CA2000 // The lifetime owns them and zeroes them when it ends (D-0313).
        var grants = lifetime.Own(new GrantCache(options.Clock));
        var envGrants = lifetime.Own(new EnvGrantCache(options.Clock));
#pragma warning restore CA2000
        var gate = new ApprovalGate(options.Channel, options.Clock, options.Limits);

        try
        {
            var handler = new ApproverHandler(
                new VaultCredentialSource(() => options.UnlockedFor(lifetime)),
                new VaultEntryNameLister(() => options.UnlockedFor(lifetime)),
                gate,
                grants,
                options.Policy,
                options.Narrate,
                clients: options.Clients);

            var authority = new SessionAuthority(
                options.Vault,
                options.CurrentLifetime,
                handler,
                new SessionEnvironments(gate, options.UnlockedFor, options.Clock, envGrants, options.Narrate, options.Audit),
                options.LockNow,
                options.Clock);

            return new SessionServer(
                new ApproverListener(pipeName, authority), authority, gate, grants, envGrants, options.WatchEdits, options.UnwatchEdits);
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

    /// <summary>Accepts connections until cancelled.</summary>
    /// <param name="cancellationToken">Cancelled to stop accepting.</param>
    /// <returns>A task that completes once the listener has stopped accepting.</returns>
    public Task RunAsync(CancellationToken cancellationToken) => _listener.RunAsync(cancellationToken);

    /// <summary>Stops watching edits and releases the endpoint and the gate, once <see cref="RunAsync"/> has finished; the lifetime zeroes the grants.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unwatchEdits(OnEdited);
        _listener.Dispose();
        _gate.Dispose();
    }

    private void OnEdited(object? sender, VaultEdit edit)
    {
        _grants.RevokeEntries(edit);
        _envGrants.RevokeEntries(edit);
    }
}
