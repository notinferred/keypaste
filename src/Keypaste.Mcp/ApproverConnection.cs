using Keypaste.Core.Ipc;

namespace Keypaste.Mcp;

/// <summary>What became of an exchange, beyond whether it produced a reply.</summary>
/// <remarks>
/// Three of these used to be a <c>bool Reachable</c>. <see cref="Busy"/> is the one that needed a
/// name: it is not a failure and not an absent approver, it is keypaste declining to stack a second
/// request behind one a person has not answered yet.
/// </remarks>
internal enum ApproverOutcome
{
    /// <summary>The approver answered. The reply is not null.</summary>
    Answered = 0,

    /// <summary>No approver could be reached at all. The refusal for this names the command to run.</summary>
    Unreachable = 1,

    /// <summary>One was reached and the exchange failed.</summary>
    Failed = 2,

    /// <summary>
    /// This connection was already carrying an exchange, so this one was refused rather than queued.
    /// </summary>
    Busy = 3,

    /// <summary>The process that answered would not attach this bridge to a session. The refusal
    /// says why.</summary>
    Refused = 4,

    /// <summary>No vault was configured, so there was nothing to name when attaching.</summary>
    NoVault = 5,
}

/// <summary>What an exchange produced.</summary>
/// <typeparam name="T">The reply's type.</typeparam>
/// <param name="Reply">The owner's reply, when it answered.</param>
/// <param name="Outcome">What became of the exchange.</param>
/// <param name="Refusal">Why the owner would not attach this bridge, when it would not.</param>
internal readonly record struct Exchange<T>(T? Reply, ApproverOutcome Outcome, AttachReply? Refusal = null)
    where T : class
{
    /// <summary>The reply and the outcome, for a caller with no use for the refusal.</summary>
    /// <param name="reply">The owner's reply, when it answered.</param>
    /// <param name="outcome">What became of the exchange.</param>
    public void Deconstruct(out T? reply, out ApproverOutcome outcome)
    {
        reply = Reply;
        outcome = Outcome;
    }
}

/// <summary>
/// The bridge's link to the process holding its vault: connects on demand, attaches before every
/// request, and reconnects once when the owner has been restarted underneath it.
/// </summary>
/// <remarks>
/// <para>
/// Connecting lazily rather than at startup is deliberate. The bridge is spawned by an MCP client
/// at the client's convenience, often long before anybody starts an approver, and refusing to start
/// without one would make keypaste look broken in the client's log rather than saying so in an
/// answer an agent can act on. The audit log is a startup precondition; an approver is not.
/// </para>
/// <para>
/// <b>Reachable and unreachable are different answers.</b> "No approver is running" is something a
/// person can fix in five seconds, and the refusal for it names the command. "The approver was
/// there and the exchange failed" is not, and says so instead. Collapsing the two would make the
/// common case unactionable.
/// </para>
/// <para>
/// <b>Attaching precedes every request</b>, so a connection left idle across a lock learns of the
/// new session before it sends anything. A request whose reply was lost is retried with the session
/// it was first sent under, never a later one: the owner refuses it rather than answer it from an
/// unlock that happened after it was asked (D-0310).
/// </para>
/// </remarks>
/// <param name="pipeName">The owner's endpoint, or null when no vault was named.</param>
/// <param name="vaultPath">The vault this bridge was configured with, or empty.</param>
internal sealed class ApproverConnection(string? pipeName, string vaultPath) : IAsyncDisposable
{
    /// <summary>
    /// How long to wait for the approver to answer the door.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Short on purpose. When no approver is running this wait is pure latency in front of a
    /// refusal, and it happens on every call until somebody starts one — which is the ordinary
    /// state of a bridge an MCP client spawned at its own convenience.
    /// </para>
    /// <para>
    /// <b>Half a second is a measurement, not a guess (DECISIONS.md D-0035).</b> A whole round trip
    /// against a running approver — process start, connect, prompt, release — measured 248 ms, while
    /// the same call with nothing listening measured 2306 ms against the two seconds this used to
    /// be. Almost none of the budget is spent when somebody is there, because
    /// <c>ApproverListener</c> always has a pending instance up, so the operating system completes
    /// the connect with no application involvement. Cutting the ceiling therefore takes 1.8 s off
    /// every refusal without touching the path that works.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private ApproverClient? _client;
    private bool _disposed;

    /// <summary>Who this bridge's client says it is, sent on every attach so the owner can count it; null sends nothing.</summary>
    /// <remarks>Display only (THREATS.md T-3): read when attaching, because the handshake that names the client comes after this connection is built.</remarks>
    internal Func<AttachClient?>? Identity { get; set; }

    /// <summary>Asks the owner to release the secrets an agent's run would inject.</summary>
    /// <param name="request">The run. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call.</param>
    /// <returns>The owner's answer, and what became of the exchange.</returns>
    /// <remarks>
    /// Never sent twice. A run whose reply was lost may already have been approved, or released, and
    /// sending it again on a fresh connection would put a second prompt in front of a person who
    /// already answered; it is reported as a failed exchange instead.
    /// </remarks>
    internal ValueTask<Exchange<RunReply>> RunAsync(RunRequest request, CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.ReleaseRunAsync(request with { Vault = vaultPath, Session = session }, token),
            cancellationToken,
            retry: false);

    /// <summary>Asks the owner to decide one credential request.</summary>
    /// <param name="request">What the agent asked for. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call.</param>
    /// <returns>The owner's answer, and what became of the exchange.</returns>
    internal ValueTask<Exchange<CredentialReply>> RequestAsync(
        CredentialRequest request,
        CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.RequestAsync(request with { Vault = vaultPath, Session = session }, token),
            cancellationToken);

    /// <summary>Asks the owner which entry names may be shown.</summary>
    /// <param name="request">The exposure to apply. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call.</param>
    /// <returns>The reply, and what became of the exchange.</returns>
    internal ValueTask<Exchange<NamesReply>> ListAsync(
        NamesRequest request,
        CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.ListAsync(request with { Vault = vaultPath, Session = session }, token),
            cancellationToken);

    private async ValueTask<Exchange<T>> ExchangeAsync<T>(
        Func<ApproverClient, string, CancellationToken, ValueTask<T?>> exchange,
        CancellationToken cancellationToken,
        bool retry = true)
        where T : class
    {
        if (pipeName is null || vaultPath.Length == 0)
        {
            return new(null, ApproverOutcome.NoVault);
        }

        bool taken;

        try
        {
            // Wait(0), not WaitAsync: taking the slot must succeed now or refuse now. The pipe carries
            // one exchange at a time and nothing correlates two replies, so waiting here queues a request
            // behind an unanswered prompt and delivers it as a second prompt. ApprovalGate refuses this
            // one layer in, and a queue in front of that check makes it unreachable (THREATS.md T-11).
            taken = _oneAtATime.Wait(0, CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            return new(null, ApproverOutcome.Unreachable);
        }

        // Deliberately above the try below, whose finally releases the slot. A refusal never took
        // one, and releasing a slot it never held would admit two exchanges onto the stream from
        // then on - the precise failure this method exists to prevent.
        if (!taken)
        {
            return new(null, ApproverOutcome.Busy);
        }

        try
        {
            var (client, attached, outcome) = await AttachedAsync(cancellationToken).ConfigureAwait(false);

            if (client is null || attached is not { Attached: true, Session: { } session })
            {
                return new(null, outcome, attached);
            }

            var reply = await exchange(client, session, cancellationToken).ConfigureAwait(false);

            if (reply is not null)
            {
                return new(reply, ApproverOutcome.Answered);
            }

            // A caller that gave up is not an owner that died, and the difference decides whether to
            // send the request again. The connection is finished either way, but re-sending would put a
            // request nobody is waiting for in front of a person, on a fresh connection whose id scopes a
            // different grant and cooldown.
            if (cancellationToken.IsCancellationRequested || !retry)
            {
                await DropAsync().ConfigureAwait(false);

                return new(null, ApproverOutcome.Failed);
            }

            // One reconnect, and one retry under the session the request was first sent in. If the
            // owner has locked or restarted since, it refuses rather than answering from a later unlock.
            await DropAsync().ConfigureAwait(false);

            var (reconnected, _, _) = await AttachedAsync(cancellationToken).ConfigureAwait(false);

            if (reconnected is null)
            {
                return new(null, ApproverOutcome.Unreachable);
            }

            var retried = await exchange(reconnected, session, cancellationToken).ConfigureAwait(false);

            return new(retried, retried is not null ? ApproverOutcome.Answered : ApproverOutcome.Failed);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Connects if need be and attaches, reconnecting once if an idle connection has died.</summary>
    private async ValueTask<(ApproverClient? Client, AttachReply? Attached, ApproverOutcome Outcome)> AttachedAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var client = await ConnectedAsync(cancellationToken).ConfigureAwait(false);

            if (client is null)
            {
                return (null, null, ApproverOutcome.Unreachable);
            }

            var attached = await client.AttachAsync(new AttachRequest(vaultPath) { Client = Identity?.Invoke() }, cancellationToken).ConfigureAwait(false);

            if (attached is not null)
            {
                return (client, attached, attached.Attached ? ApproverOutcome.Answered : ApproverOutcome.Refused);
            }

            await DropAsync().ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return (null, null, ApproverOutcome.Failed);
    }

    private async ValueTask<ApproverClient?> ConnectedAsync(CancellationToken cancellationToken)
    {
        if (_disposed || pipeName is null)
        {
            return null;
        }

        if (_client is { IsConnected: true })
        {
            return _client;
        }

        await DropAsync().ConfigureAwait(false);

        _client = await ApproverClient.TryConnectAsync(pipeName, ConnectTimeout, cancellationToken).ConfigureAwait(false);

        return _client;
    }

    private async ValueTask DropAsync()
    {
        var going = _client;
        _client = null;

        if (going is not null)
        {
            await going.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await DropAsync().ConfigureAwait(false);
        _oneAtATime.Dispose();
    }
}
