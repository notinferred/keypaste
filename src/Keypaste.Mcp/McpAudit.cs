using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp;

/// <summary>Who is calling, from one read of the handshake.</summary>
/// <param name="Initialized">Whether the <c>initialize</c> handshake has completed; a call is never answered otherwise.</param>
/// <param name="Client">What can honestly be said about the caller, for the owner and the audit line.</param>
internal readonly record struct Caller(bool Initialized, AuditClient Client);

/// <summary>
/// Reads who is calling from the protocol, for the owner and the audit line.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the file an SDK major version changes.</b> Version 1 carries the client's identity in
/// the <c>initialize</c> handshake, reachable as <see cref="McpServer.ClientInfo"/>; version 2
/// removes that handshake and moves identity to per-request <c>_meta</c>, where clients only
/// <em>should</em> send it. Keeping the read in one function is what makes that a contained edit
/// instead of a hunt (D-0019).
/// </para>
/// <para>
/// It also means law 3.3's "every access is logged with who" survives identity becoming optional:
/// a client that says nothing is recorded as having said nothing, and <c>--client-label</c> gives
/// the human a name of their own choosing that no connecting client can overwrite.
/// </para>
/// </remarks>
internal static class McpAudit
{
    /// <summary>The reason this bridge records for a reply that says its owner did not record the request.</summary>
    internal const string Unrecorded = "the vault's owner did not record this request";

    /// <summary>What this bridge tells the owner about its client when it attaches: display and counting only (THREATS.md T-3).</summary>
    /// <param name="declared">What the client said in its handshake, or null before it has.</param>
    /// <param name="options">The server's configuration, for the raw label.</param>
    /// <returns>The identity, or null when there is nothing to say.</returns>
    internal static AttachClient? AttachIdentity(Implementation? declared, ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var said = AuditClient.Of(declared?.Name, declared?.Version, null);

        return said.Name is null && said.Version is null && options.ClientLabel is null
            ? null
            : new AttachClient(said.Name, said.Version, options.ClientLabel);
    }

    /// <summary>How long a tool call waits for a handshake that is already in flight.</summary>
    /// <remarks>
    /// Enormous next to the gap it covers, which is thread scheduling, and small next to the three
    /// seconds <c>verify-mcp-stdio.sh</c> gives a client that never initializes at all. A client
    /// that sent nothing still waits this out and is still refused.
    /// </remarks>
    internal static readonly TimeSpan HandshakeGrace = TimeSpan.FromSeconds(1);

    /// <summary>How often the identity is re-read while waiting.</summary>
    internal static readonly TimeSpan HandshakePoll = TimeSpan.FromMilliseconds(10);

    /// <summary>Waits, bounded, for a condition that a message already in flight will satisfy.</summary>
    /// <param name="complete">The question, re-asked until it answers yes or the budget runs out.</param>
    /// <param name="grace">How long to wait.</param>
    /// <param name="clock">The clock to measure it on.</param>
    /// <param name="cancellationToken">The caller giving up.</param>
    /// <returns>The last answer, which is <c>false</c> if the budget ran out.</returns>
    /// <remarks>
    /// Split out from <see cref="CallerAsync"/> because the thing worth testing is the
    /// waiting, and the thing that cannot be tested in process is the SDK's dispatch order: a real
    /// <c>McpClient</c> performs the handshake and waits for its response, so no in-process test
    /// can produce the ordering this exists for. A predicate that flips can.
    /// </remarks>
    internal static async ValueTask<bool> AwaitCompletionAsync(
        Func<bool> complete,
        TimeSpan grace,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(complete);
        ArgumentNullException.ThrowIfNull(clock);

        // Asked once before any waiting, so the ordinary call pays nothing for this.
        if (complete())
        {
            return true;
        }

        var started = clock.GetTimestamp();

        while (clock.GetElapsedTime(started) < grace)
        {
            try
            {
                await Task.Delay(HandshakePoll, clock, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller stopped waiting. Answer with what is known rather than throwing: the
                // call still needs a verdict, and an unanswered one is recorded as nothing at all.
                return complete();
            }

            if (complete())
            {
                return true;
            }
        }

        return complete();
    }

    /// <summary>Works out who is calling, once the handshake that names them has completed or the wait for it has run out.</summary>
    /// <param name="request">The tool call in flight.</param>
    /// <param name="options">The server's configuration, for the operator-supplied label.</param>
    /// <param name="cancellationToken">The caller giving up.</param>
    /// <returns>Whether the handshake completed, and what can honestly be said about the caller.</returns>
    /// <remarks>
    /// <para>
    /// Both client-supplied strings are sanitized by <see cref="AuditClient.Of"/> before they are
    /// recorded. They are attacker-chosen text that Stage 2.2 renders in an approval dialog and
    /// <c>keypaste log</c> renders in a table — the two places an injection payload would most like to land.
    /// Absent values are recorded as absent rather than as the word "unknown": that a client said
    /// nothing is a different fact from a client calling itself nothing.
    /// </para>
    /// <para>
    /// <b>It waits, and the reason is F.5.</b> A client that writes <c>initialize</c>,
    /// <c>notifications/initialized</c> and a tool call in one go — which is legal, and which
    /// <c>verify-approval-e2e.sh</c> and <c>verify-policy-e2e.sh</c> both do — was being refused as
    /// though it had never introduced itself, because nothing orders the tool call after the
    /// handler that publishes <c>ClientInfo</c>. Observed on macOS NativeAOT twice in two attempts
    /// of release dispatch 34387033087, and once before that, when the response was to make
    /// <c>verify-mcp-stdio.sh</c> wait for the initialize response rather than to fix this.
    /// </para>
    /// <para>
    /// <b>The identity is read once, after the wait, and both answers come from that read</b>: the
    /// question "do we know who this is" and the answer "here is who this is" must not be able to
    /// disagree. Read before the wait, a client that named itself while its call waited was put to
    /// the owner and logged as an unnamed client (F.61). The protocol makes <c>clientInfo</c>
    /// required in <c>initialize</c>, so a null here means either the handshake has not happened or
    /// the client broke the rule, and both are cases to refuse.
    /// </para>
    /// <para>
    /// The guarantee is unchanged: a request is never answered for a client that cannot be named.
    /// Waiting only stops a client that <em>did</em> name itself from being told it did not. This
    /// is not authentication — a client can still call itself anything (THREATS.md T-3).
    /// </para>
    /// </remarks>
    internal static async ValueTask<Caller> CallerAsync(
        RequestContext<CallToolRequestParams> request,
        ServerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        await AwaitCompletionAsync(
            () => request.Server?.ClientInfo is not null,
            HandshakeGrace,
            TimeProvider.System,
            cancellationToken).ConfigureAwait(false);

        var declared = request.Server?.ClientInfo;

        return new Caller(
            declared is not null,
            AuditClient.Of(declared?.Name, declared?.Version, options.ClientLabel));
    }
}
