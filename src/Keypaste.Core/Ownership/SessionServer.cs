using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>How one owner answers agents for one unlock: everything that differs between the desktop and <c>keypaste agent</c>.</summary>
public sealed record SessionServerOptions
{
    /// <summary>The vault served.</summary>
    public required VaultIdentity Vault { get; init; }

    /// <summary>The unlock this serves, which owns the grants and launch passes and forgets them when it ends (D-0313).</summary>
    public required SessionLifetime Lifetime { get; init; }

    /// <summary>The owner's live lifetime now, which a request must belong to (D-0310).</summary>
    public required Func<SessionLifetime?> CurrentLifetime { get; init; }

    /// <summary>The vault while a lifetime is live, otherwise null.</summary>
    public required Func<SessionLifetime, Vault?> UnlockedFor { get; init; }

    /// <summary>Where a person is asked.</summary>
    public required IApprovalChannel Channel { get; init; }

    /// <summary>How many requests wait for a person, and for how long.</summary>
    public ApprovalLimits Limits { get; init; } = ApprovalLimits.Default;

    /// <summary>Each agent's settings in the vault, and this computer's ceilings in <c>clients.toml</c>.</summary>
    public required IAgentSettings Settings { get; init; }

    /// <summary>The owner's hourly limits on Automatic, which outlive one unlock and end with the owner's process.</summary>
    public required HourlyLimit Hourly { get; init; }

    /// <summary>Opens the audit log the owner records each request it answers in, or returns null when it cannot, which refuses the request before anybody is asked.</summary>
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
/// <remarks>
/// The authority is built before the vault's endpoint is bound, so the app answers its own Runs on its
/// launch endpoint even when the vault's cannot be bound (D-0451).
/// </remarks>
public sealed class SessionServer : IDisposable
{
    private readonly ApprovalGate _gate;
    private readonly GrantCache _grants;
    private readonly EnvGrantCache _envGrants;
    private readonly Action<EventHandler<VaultEdit>> _unwatchEdits;
    private ApproverListener? _listener;
    private bool _disposed;

    private SessionServer(
        SessionAuthority authority,
        ApprovalGate gate,
        GrantCache grants,
        EnvGrantCache envGrants,
        Action<EventHandler<VaultEdit>> watchEdits,
        Action<EventHandler<VaultEdit>> unwatchEdits)
    {
        Authority = authority;
        _gate = gate;
        _grants = grants;
        _envGrants = envGrants;
        _unwatchEdits = unwatchEdits;
        watchEdits(OnEdited);
    }

    /// <summary>What answers each request.</summary>
    public SessionAuthority Authority { get; }

    /// <summary>Builds what answers agents for one unlock, binding no endpoint.</summary>
    /// <param name="options">How this owner differs.</param>
    /// <returns>The server, whose <see cref="Authority"/> answers before <see cref="Bind"/> binds the vault's endpoint, or if it never does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static SessionServer Build(SessionServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var lifetime = options.Lifetime;

#pragma warning disable CA2000 // The lifetime owns them and zeroes them when it ends (D-0313).
        var grants = lifetime.Own(new GrantCache(options.Clock));
        var envGrants = lifetime.Own(new EnvGrantCache(options.Clock));
        var launches = lifetime.Own(new LaunchPasses(options.Clock));
#pragma warning restore CA2000
        var gate = new ApprovalGate(options.Channel, options.Clock, options.Limits);

        try
        {
            var handler = new ApproverHandler(
                new VaultCredentialSource(() => options.UnlockedFor(lifetime)),
                new VaultEntryNameLister(() => options.UnlockedFor(lifetime)),
                gate,
                grants,
                options.Settings,
                options.Narrate,
                hourly: options.Hourly,
                clock: options.Clock);

            var authority = new SessionAuthority(
                options.Vault,
                options.CurrentLifetime,
                handler,
                options.Audit,
                new SessionEnvironments(gate, options.UnlockedFor, options.Clock, envGrants, options.Narrate, launches),
                options.LockNow,
                options.Clock);

            return new SessionServer(authority, gate, grants, envGrants, options.WatchEdits, options.UnwatchEdits);
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

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

        SessionServer? server = Build(options);

        try
        {
            server.Bind(pipeName);
            var bound = server;
            server = null;
            return bound;
        }
        finally
        {
            server?.Dispose();
        }
    }

    /// <summary>Binds the vault's endpoint, which <see cref="RunAsync"/> then accepts on.</summary>
    /// <param name="pipeName">The vault's endpoint, as <see cref="ApproverEndpoint"/> names it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The vault's endpoint is already bound.</exception>
    /// <exception cref="IOException">The name is already taken.</exception>
    /// <exception cref="UnauthorizedAccessException">The name cannot be bound.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">On Linux and macOS, something at the name's socket path that cannot be replaced.</exception>
    public void Bind(string pipeName)
    {
        ArgumentNullException.ThrowIfNull(pipeName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listener is not null)
        {
            throw new InvalidOperationException("the vault's endpoint is already bound");
        }

        _listener = new ApproverListener(pipeName, Authority);
    }

    /// <summary>Accepts connections on the vault's endpoint until cancelled.</summary>
    /// <param name="cancellationToken">Cancelled to stop accepting.</param>
    /// <returns>A task that completes once the listener has stopped accepting.</returns>
    /// <exception cref="InvalidOperationException">The vault's endpoint is not bound.</exception>
    public Task RunAsync(CancellationToken cancellationToken) =>
        (_listener ?? throw new InvalidOperationException("the vault's endpoint is not bound")).RunAsync(cancellationToken);

    /// <summary>Stops watching edits and releases the endpoint and the gate, once <see cref="RunAsync"/> has finished; the lifetime zeroes the grants.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unwatchEdits(OnEdited);
        _listener?.Dispose();
        _gate.Dispose();
    }

    private void OnEdited(object? sender, VaultEdit edit)
    {
        _grants.RevokeEntries(edit);
        _envGrants.RevokeEntries(edit);
    }
}
