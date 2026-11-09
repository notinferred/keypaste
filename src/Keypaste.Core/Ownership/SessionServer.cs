using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>How one owner answers agents for one unlock: everything that differs between the desktop and <c>keypaste approve</c>.</summary>
public sealed record SessionServerOptions
{
    /// <summary>The vault served.</summary>
    public required VaultIdentity Vault { get; init; }

    /// <summary>The unlock this serves, which owns the launch passes and forgets them when it ends (D-0313).</summary>
    public required SessionLifetime Lifetime { get; init; }

    /// <summary>The owner's live lifetime now, which a request must belong to (D-0310).</summary>
    public required Func<SessionLifetime?> CurrentLifetime { get; init; }

    /// <summary>The vault while a lifetime is live, otherwise null.</summary>
    public required Func<SessionLifetime, Vault?> UnlockedFor { get; init; }

    /// <summary>Where a person is asked.</summary>
    public required IApprovalChannel Channel { get; init; }

    /// <summary>How many requests wait for a person, and for how long.</summary>
    public ApprovalLimits Limits { get; init; } = ApprovalLimits.Default;

    /// <summary>The rules every agent's request is decided by, this computer's <c>agents.toml</c>.</summary>
    public required IAgentRules Rules { get; init; }

    /// <summary>Opens the audit log the owner records each request it answers in, or returns null when it cannot, which refuses the request before anybody is asked.</summary>
    public required Func<AuditLog?> Audit { get; init; }

    /// <summary>What <c>keypaste lock</c> runs, or null to refuse it.</summary>
    public Action? LockNow { get; init; }

    /// <summary>Where the owner says what it decided, or null.</summary>
    public Action<string>? Narrate { get; init; }

    /// <summary>What time it is.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// Runs a terminal's edit, or a browser's pairing the person confirmed, on the vault of the lifetime it was asked under and
    /// saves it under the owner's claim, or null to refuse every edit and pairing (D-0464, D-0634).
    /// </summary>
    public Func<SessionLifetime, Func<Vault, bool>, AgentSave>? WriteEdit { get; init; }

    /// <summary>
    /// When each browser's pairing was last proven, kept for the owner's life, or null for an owner that pairs no browsers;
    /// pairing also needs <see cref="WriteEdit"/> (D-0634).
    /// </summary>
    public PairingUses? BrowserUses { get; init; }

    /// <summary>Told of each terminal's edit once it is saved, or null.</summary>
    public Action<EditDone>? Edited { get; init; }

    /// <summary>Told of an exception no rule expected while a request was decided, or null (<see cref="SessionAuthority.Unexpected"/>).</summary>
    public Action<Exception>? Unexpected { get; init; }
}

/// <summary>
/// The gate, the handler, the authority and the listener one owner answers agents with for
/// one unlock, built once so the desktop and <c>keypaste approve</c> cannot assemble them differently.
/// </summary>
/// <remarks>
/// The authority is built before the vault's endpoint is bound, so the app answers its own Runs on its
/// launch endpoint even when the vault's cannot be bound (D-0451).
/// </remarks>
public sealed class SessionServer : IDisposable
{
    private readonly ApprovalGate _gate;
    private ApproverListener? _listener;
    private bool _disposed;

    private SessionServer(
        SessionAuthority authority,
        ApprovalGate gate)
    {
        Authority = authority;
        _gate = gate;
    }

    /// <summary>What answers each request.</summary>
    public SessionAuthority Authority { get; }

    /// <summary>What pairs a browser for this unlock, arming included, or null for an owner that pairs none.</summary>
    public SessionBrowsers? Browsers { get; private init; }

    /// <summary>Builds what answers agents for one unlock, binding no endpoint.</summary>
    /// <param name="options">How this owner differs.</param>
    /// <returns>The server, whose <see cref="Authority"/> answers before <see cref="Bind"/> binds the vault's endpoint, or if it never does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static SessionServer Build(SessionServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var lifetime = options.Lifetime;

#pragma warning disable CA2000 // The lifetime owns them and zeroes them when it ends.
        var launches = lifetime.Own(new LaunchPasses(options.Clock));
#pragma warning restore CA2000

        var gate = new ApprovalGate(options.Channel, options.Clock, options.Limits);

        try
        {
            var handler = new ApproverHandler(
                new VaultCredentialSource(() => options.UnlockedFor(lifetime), options.Clock),
                new VaultEntryNameLister(() => options.UnlockedFor(lifetime)),
                gate,
                options.Rules,
                options.Narrate);

            var editor = options.WriteEdit is { } write
                ? new SessionEditor(gate, options.UnlockedFor, write, options.Clock, options.Narrate, options.Edited)
                : null;

            var saver = options.WriteEdit is { } save
                ? new SessionSaver(gate, options.UnlockedFor, save, options.Clock, options.Narrate)
                : null;

            var browsers = options.BrowserUses is { } uses && options.WriteEdit is { } pair
                ? new SessionBrowsers(options.Vault, lifetime, gate, options.UnlockedFor, pair, options.Audit, uses, options.Clock, options.Narrate)
                : null;

            var authority = new SessionAuthority(
                options.Vault,
                options.CurrentLifetime,
                handler,
                options.Audit,
                new SessionEnvironments(gate, options.UnlockedFor, options.Clock, options.Narrate, launches),
                options.LockNow,
                options.Clock,
                editor,
                browsers,
                saver)
            {
                Unexpected = options.Unexpected,
            };

            if (browsers is not null)
            {
                browsers.Removed += (_, pairing) => authority.Unbind(pairing);
            }

            return new SessionServer(authority, gate) { Browsers = browsers };
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

    /// <summary>Releases the endpoint and the gate, once <see cref="RunAsync"/> has finished.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener?.Dispose();
        _gate.Dispose();
    }
}
