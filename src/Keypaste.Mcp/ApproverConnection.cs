using Keypaste.Core.Approval;
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
    /// Every lane this bridge may open was already carrying an exchange, so this one was refused rather than queued.
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
/// new session before it sends anything. A listing whose reply was lost is retried with the session
/// it was first sent under, never a later one: the owner refuses it rather than answer it from an
/// unlock that happened after it was asked (D-0310). Nothing that can ask a person is sent twice.
/// </para>
/// <para>
/// <b>Every exchange is bounded.</b> It is withdrawn when the client gives up, when the client closes
/// the bridge's standard input, and once the longest wait a person could take to answer it has passed,
/// so an owner that stopped answering cannot hold a call, or its prompt, for longer, and it is not sent
/// again after any of these.
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

    /// <summary>How many exchanges this bridge carries at once: one on screen and the agent's waiting behind it (F.80).</summary>
    internal const int Lanes = ApprovalLimits.MaximumWaiting + 1;

    private readonly object _lanesLock = new();
    private readonly List<Lane> _lanes = [];
    private bool _disposed;

    /// <summary>Who this bridge's client says it is, sent on every attach so the owner can count it; null sends nothing.</summary>
    /// <remarks>Display only (THREATS.md T-3): read when attaching, because the handshake that names the client comes after this connection is built.</remarks>
    internal Func<AttachClient?>? Identity { get; set; }

    /// <summary>The globs this bridge answers with, sent on every attach so the owner can say what its agents can see; null sends nothing.</summary>
    /// <remarks>Display only (THREATS.md T-3): every request still carries its own exposure, which is what is enforced.</remarks>
    internal IReadOnlyList<string>? Exposure { get; set; }

    /// <summary>Cancelled once the client has closed the bridge's standard input, which withdraws every exchange still in flight.</summary>
    internal CancellationToken InputEnded { get; init; }

    /// <summary>
    /// The longest an exchange one prompt answers may take: the longest window a person can be given
    /// to answer, and time for the answer to get home, as the CLI waits for its owner.
    /// </summary>
    internal TimeSpan AnswerBound { get; init; } = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    /// <summary>
    /// The longest a credential request or a run may take: the windows of the prompts its agent can have ahead of it and its
    /// own, as each waits its turn, and the same margin.
    /// </summary>
    internal TimeSpan ReleaseBound { get; init; } = TimeSpan.FromSeconds((ApprovalLimits.MaximumWindowSeconds * Lanes) + 15);

    /// <summary>Asks the owner to release the secrets an agent's run would inject.</summary>
    /// <param name="request">The run. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call, which withdraws a request already sent; the owner's reply to it, saying whether it was recorded, is still returned.</param>
    /// <returns>The owner's answer, and what became of the exchange.</returns>
    /// <remarks>
    /// Never sent twice. A run whose reply was lost may already have been approved, or released, and
    /// sending it again on a fresh connection would put a second prompt in front of a person who
    /// already answered; it is reported as a failed exchange instead.
    /// </remarks>
    internal ValueTask<Exchange<RunReply>> RunAsync(RunRequest request, CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.ReleaseRunAsync(request with { Vault = vaultPath, Session = session }, token),
            ReleaseBound,
            cancellationToken,
            retry: false);

    /// <summary>Asks the owner to decide one credential request.</summary>
    /// <param name="request">What the agent asked for. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call, which withdraws a request already sent; the owner's reply to it, saying whether it was recorded, is still returned.</param>
    /// <returns>The owner's answer, and what became of the exchange.</returns>
    /// <remarks>Never sent twice, as a run is not: a request whose reply was lost may already have been answered by a person.</remarks>
    internal ValueTask<Exchange<CredentialReply>> RequestAsync(
        CredentialRequest request,
        CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.RequestAsync(request with { Vault = vaultPath, Session = session }, token),
            ReleaseBound,
            cancellationToken,
            retry: false);

    /// <summary>Asks the owner to put an agent's request to save a value to the person.</summary>
    /// <param name="request">Where the value goes and why. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call, which withdraws a request already sent.</param>
    /// <returns>The owner's answer, and what became of the exchange.</returns>
    /// <remarks>Never sent twice: a request whose reply was lost may already have been answered by a person.</remarks>
    internal ValueTask<Exchange<SaveReply>> SaveAsync(SaveRequest request, CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.SaveAsync(request with { Vault = vaultPath, Session = session }, token),
            ReleaseBound,
            cancellationToken,
            retry: false);

    /// <summary>Asks the owner which entry names may be shown.</summary>
    /// <param name="request">The exposure to apply. Its vault and session are filled in here.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up on the call, which withdraws a request already sent; the owner's reply to it, saying whether it was recorded, is still returned.</param>
    /// <returns>The reply, and what became of the exchange.</returns>
    internal ValueTask<Exchange<NamesReply>> ListAsync(
        NamesRequest request,
        CancellationToken cancellationToken) =>
        ExchangeAsync(
            (client, session, token) => client.ListAsync(request with { Vault = vaultPath, Session = session }, token),
            AnswerBound,
            cancellationToken);

    private async ValueTask<Exchange<T>> ExchangeAsync<T>(
        Func<ApproverClient, string, CancellationToken, ValueTask<T?>> exchange,
        TimeSpan bound,
        CancellationToken cancellationToken,
        bool retry = true)
        where T : class
    {
        if (pipeName is null || vaultPath.Length == 0)
        {
            return new(null, ApproverOutcome.NoVault);
        }

        // A lane carries one exchange at a time, since nothing correlates two replies on one pipe. A call made while every
        // lane is busy is refused rather than queued here: the owner's queue is where requests wait, bounded per agent
        // (THREATS.md T-11), and a queue in front of it would hide that bound.
        if (Take() is not { } lane)
        {
            return new(null, ApproverOutcome.Busy);
        }

        try
        {
            // The client giving up, its closing standard input and the bound each withdraw the request
            // the same way, and the owner's reply to the withdrawal is still read.
            using var withdrawing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, InputEnded);
            withdrawing.CancelAfter(bound);
            var withdrawn = withdrawing.Token;

            var (client, attached, outcome) = await AttachedAsync(lane, withdrawn).ConfigureAwait(false);

            if (client is null || attached is not { Attached: true, Session: { } session })
            {
                return new(null, outcome, attached);
            }

            var reply = await exchange(client, session, withdrawn).ConfigureAwait(false);

            if (reply is not null)
            {
                return new(reply, ApproverOutcome.Answered);
            }

            // A request that was withdrawn is not an owner that died, and the difference decides whether to
            // send it again. The connection is finished either way, but re-sending would put a request
            // nobody is waiting for in front of a person, on a fresh connection whose id scopes a different
            // cooldown; and one that can ask a person may already have been answered.
            if (withdrawn.IsCancellationRequested || !retry)
            {
                await lane.DropAsync().ConfigureAwait(false);

                return new(null, ApproverOutcome.Failed);
            }

            // One reconnect, and one retry under the session the request was first sent in. If the
            // owner has locked or restarted since, it refuses rather than answering from a later unlock.
            await lane.DropAsync().ConfigureAwait(false);

            var (reconnected, _, _) = await AttachedAsync(lane, withdrawn).ConfigureAwait(false);

            if (reconnected is null)
            {
                return new(null, ApproverOutcome.Unreachable);
            }

            var retried = await exchange(reconnected, session, withdrawn).ConfigureAwait(false);

            return new(retried, retried is not null ? ApproverOutcome.Answered : ApproverOutcome.Failed);
        }
        finally
        {
            lock (_lanesLock)
            {
                lane.Busy = false;
            }
        }
    }

    /// <summary>A lane free now, opened when every one open is busy and fewer than <see cref="Lanes"/> are; null otherwise.</summary>
    private Lane? Take()
    {
        lock (_lanesLock)
        {
            if (_disposed)
            {
                return null;
            }

            if (_lanes.FirstOrDefault(lane => !lane.Busy) is { } free)
            {
                free.Busy = true;
                return free;
            }

            if (_lanes.Count >= Lanes)
            {
                return null;
            }

            var opened = new Lane { Busy = true };
            _lanes.Add(opened);
            return opened;
        }
    }

    /// <summary>Connects the lane if need be and attaches, reconnecting once if an idle connection has died.</summary>
    private async ValueTask<(ApproverClient? Client, AttachReply? Attached, ApproverOutcome Outcome)> AttachedAsync(
        Lane lane,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var client = await ConnectedAsync(lane, cancellationToken).ConfigureAwait(false);

            if (client is null)
            {
                return (null, null, ApproverOutcome.Unreachable);
            }

            var attached = await client.AttachAsync(new AttachRequest(vaultPath) { Client = Identity?.Invoke(), Exposure = Exposure }, cancellationToken).ConfigureAwait(false);

            if (attached is not null)
            {
                return (client, attached, attached.Attached ? ApproverOutcome.Answered : ApproverOutcome.Refused);
            }

            await lane.DropAsync().ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return (null, null, ApproverOutcome.Failed);
    }

    private async ValueTask<ApproverClient?> ConnectedAsync(Lane lane, CancellationToken cancellationToken)
    {
        if (_disposed || pipeName is null)
        {
            return null;
        }

        if (lane.Client is { IsConnected: true })
        {
            return lane.Client;
        }

        await lane.DropAsync().ConfigureAwait(false);

        lane.Client = await ApproverClient.TryConnectAsync(pipeName, ConnectTimeout, cancellationToken).ConfigureAwait(false);

        return lane.Client;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        List<Lane> lanes;

        lock (_lanesLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lanes = [.. _lanes];
        }

        foreach (var lane in lanes)
        {
            await lane.DropAsync().ConfigureAwait(false);
        }
    }

    /// <summary>One connection to the owner, carrying one exchange at a time; it stays open for the next once its exchange ends.</summary>
    private sealed class Lane
    {
        /// <summary>Whether an exchange holds it. Read and written under the connection's lock.</summary>
        internal bool Busy { get; set; }

        internal ApproverClient? Client { get; set; }

        internal async ValueTask DropAsync()
        {
            var going = Client;
            Client = null;

            if (going is not null)
            {
                await going.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
