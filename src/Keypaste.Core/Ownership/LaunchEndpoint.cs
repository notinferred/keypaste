using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>
/// The app's own endpoint for the runs Terminal starts on macOS, bound for each unlock and ended by its
/// lock, answering an attachment and an env request carrying a launch pass, and nothing else (D-0451).
/// </summary>
/// <remarks>
/// <para>
/// It is apart from the vault's endpoint, so a Run confirmed in the app starts while agents cannot reach
/// the vault: a <c>KEYPASTE_APPROVER</c> that names no usable pipe, or a vault's endpoint that cannot be
/// bound. Its listener refuses a peer running as another user as the vault's does (THREATS.md T-10), and
/// the unlock's own authority admits, records and releases what it reads (D-0452).
/// </para>
/// <para>
/// The app picks its name at random once, from <see cref="NewName"/>, and binds it again at each unlock,
/// as the vault's endpoint keeps its name, so a run that asks after a lock and an unlock reaches the new
/// unlock, which refuses its pass and records it.
/// </para>
/// <para>
/// Any other kind of request ends its connection unanswered, so nothing here lists, releases, grants or
/// locks for an agent, and an env request without a pass is refused unasked and recorded. An attachment
/// here names no client, so a bridge that reaches it is not shown as connected.
/// </para>
/// </remarks>
public sealed class LaunchEndpoint : IDisposable
{
    /// <summary>What every launch endpoint's name starts with.</summary>
    public const string Prefix = "keypaste-launch-";

    private static readonly HashSet<ApproverMessageKind> _serves = [ApproverMessageKind.Attach, ApproverMessageKind.EnvProfile];

    private readonly ApproverListener _listener;

    private LaunchEndpoint(string name, ApproverListener listener)
    {
        Name = name;
        _listener = listener;
    }

    /// <summary>The pipe the run Terminal starts is given as <c>--approver</c>.</summary>
    public string Name { get; }

    /// <summary>A name for the app's launch endpoint that no other process chose.</summary>
    /// <returns><see cref="Prefix"/> and 16 random hex digits.</returns>
    /// <remarks>Sixteen digits keep the socket's path, under the temporary folder, inside macOS's 104 bytes.</remarks>
    public static string NewName() => Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    /// <summary>Binds the launch endpoint for one unlock, answered by that unlock's authority.</summary>
    /// <param name="name">The app's launch endpoint name, from <see cref="NewName"/>.</param>
    /// <param name="authority">The unlock's authority, which also answers the vault's endpoint when that is bound.</param>
    /// <returns>The endpoint, bound and not yet accepting; <see cref="RunAsync"/> accepts.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="IOException">The name cannot be bound.</exception>
    /// <exception cref="UnauthorizedAccessException">The name cannot be bound.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">On Linux and macOS, the name's socket cannot be made.</exception>
    public static LaunchEndpoint Listen(string name, SessionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(authority);

        return new LaunchEndpoint(name, new ApproverListener(name, new PassesOnly(authority), _serves, "launch-"));
    }

    /// <summary>Accepts connections until cancelled.</summary>
    /// <param name="cancellationToken">Cancelled to stop accepting.</param>
    /// <returns>A task that completes once the listener has stopped accepting.</returns>
    public Task RunAsync(CancellationToken cancellationToken) => _listener.RunAsync(cancellationToken);

    /// <summary>Releases the endpoint, once <see cref="RunAsync"/> has finished.</summary>
    public void Dispose() => _listener.Dispose();

    /// <summary>The authority as the launch endpoint offers it: an attachment naming no client, and an env request held to its pass.</summary>
    /// <remarks>The listener reads no other kind, so the listing and credential methods are never called.</remarks>
    private sealed class PassesOnly(SessionAuthority authority) : IApproverHandler
    {
        public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken) =>
            authority.AttachAsync(request with { Client = null, Exposure = null }, connectionId, cancellationToken);

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the launch endpoint lists nothing");

        public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the launch endpoint releases no credential");

        public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken) =>
            authority.ReleaseLaunchAsync(request, connectionId, cancellationToken);

        public void Disconnected(string connectionId) => authority.Disconnected(connectionId);
    }
}
