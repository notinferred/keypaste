namespace Keypaste.Core.Ipc;

/// <summary>
/// Answers what the bridge asks. Implemented by whatever holds the unlocked vault and can reach a
/// human.
/// </summary>
/// <remarks>
/// <para>
/// Listing and releasing stay separate methods, and that is DECISIONS.md D-0022's separation surviving the
/// move onto a socket. Listing yields <see cref="EntryName"/>, which has nowhere to put a secret;
/// releasing yields a <see cref="CredentialReply"/>, or an <see cref="EnvReply"/> for a runner, the
/// only types here that have. Collapsing
/// them into one "handle a request" method would give the listing path a way to return a credential,
/// which is the single change most likely to turn <c>list_entry_names</c> into an exfiltration tool.
/// </para>
/// <para>
/// A connection id identifies the process on the other end for as long as it stays connected. It is
/// minted here rather than claimed by the peer, because a client's asserted name is not evidence of
/// anything (THREATS.md T-3) — this is, at least, evidence of being the same connection a human
/// approved something for.
/// </para>
/// </remarks>
public interface IApproverHandler
{
    /// <summary>Attaches a connection to the session holding the vault it names.</summary>
    /// <param name="request">The vault the bridge was configured with.</param>
    /// <param name="connectionId">The connection to attach.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The session, or why the connection was not attached.</returns>
    ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken);

    /// <summary>Which entry names may be shown to the agent on this connection.</summary>
    /// <param name="request">The exposure the bridge is configured with.</param>
    /// <param name="connectionId">Who is asking, for as long as they stay connected.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The names, or the reason there are none.</returns>
    ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken);

    /// <summary>Decides one credential request, asking a human if it has to.</summary>
    /// <param name="request">What the agent asked for.</param>
    /// <param name="connectionId">Who is asking, and what any resulting grant is scoped to.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The decision, and the field value on the one path that has one.</returns>
    ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken);

    /// <summary>Decides one request for a project's env set, asking a human before any of it leaves.</summary>
    /// <param name="request">The project, and the command and directory the runner will start it in.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The whole set, or why none of it was released.</returns>
    ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken);

    /// <summary>Lists the grants the current session holds, names only.</summary>
    /// <param name="request">The attachment it is made under.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>The grants, or why none were listed. A handler that does not hold grants refuses.</returns>
    ValueTask<GrantsReply> GrantsAsync(GrantsRequest request, string connectionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new GrantsReply(false, [], true, "this keypaste process does not list grants"));

    /// <summary>Ends grants of the current session.</summary>
    /// <param name="request">Which grants, and the attachment it is made under.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>How many ended. A handler that does not hold grants refuses.</returns>
    ValueTask<RevokeGrantsReply> RevokeGrantsAsync(RevokeGrantsRequest request, string connectionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RevokeGrantsReply(0, "this keypaste process does not revoke grants"));

    /// <summary>Locks now.</summary>
    /// <param name="request">The attachment it is made under.</param>
    /// <param name="connectionId">Who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes away.</param>
    /// <returns>Whether the lock started. A handler that cannot lock refuses.</returns>
    ValueTask<LockReply> LockAsync(LockRequest request, string connectionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new LockReply(false, "this keypaste process does not lock on request"));

    /// <summary>Tells the handler a connection has gone, so its grants can go with it.</summary>
    /// <param name="connectionId">The connection that ended.</param>
    void Disconnected(string connectionId);
}
