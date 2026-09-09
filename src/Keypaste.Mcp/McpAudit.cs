using Keypaste.Core;
using Keypaste.Core.Audit;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp;

/// <summary>
/// Turns a protocol request into an audit line.
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
    /// <summary>The longest client-supplied name or version kept.</summary>
    internal const int MaximumClientTextLength = 64;

    /// <summary>Works out who is calling.</summary>
    /// <param name="request">The tool call in flight.</param>
    /// <param name="options">The server's configuration, for the operator-supplied label.</param>
    /// <returns>What can honestly be said about the caller.</returns>
    /// <remarks>
    /// Both client-supplied strings go through <see cref="EntryNameSanitizer"/> before they are
    /// recorded. They are attacker-chosen text that Stage 2.2 renders in an approval dialog and
    /// <c>keypaste log</c> renders in a table — the two places an injection payload would most like to land.
    /// Absent values are recorded as absent rather than as the word "unknown": that a client said
    /// nothing is a different fact from a client calling itself nothing.
    /// </remarks>
    internal static AuditClient ClientOf(RequestContext<CallToolRequestParams> request, ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var declared = request.Server?.ClientInfo;

        return new AuditClient(
            Clean(declared?.Name),
            Clean(declared?.Version),
            Clean(options.ClientLabel));
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
    /// Split out from <see cref="HandshakeCompleteAsync"/> because the thing worth testing is the
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

    /// <summary>Whether the <c>initialize</c> handshake has completed for this connection.</summary>
    /// <param name="request">The tool call in flight.</param>
    /// <param name="cancellationToken">The caller giving up.</param>
    /// <returns><c>true</c> once the client's identity is known.</returns>
    /// <remarks>
    /// <para>
    /// Read off the same place <see cref="ClientOf"/> reads, deliberately: the question "do we know
    /// who this is" and the answer "here is who this is" must not be able to disagree. The protocol
    /// makes <c>clientInfo</c> required in <c>initialize</c>, so a null here means either the
    /// handshake has not happened or the client broke the rule, and both are cases to refuse.
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
    /// The guarantee is unchanged: a request is never answered for a client that cannot be named.
    /// Waiting only stops a client that <em>did</em> name itself from being told it did not. This
    /// is not authentication — a client can still call itself anything (THREATS.md T-3).
    /// </para>
    /// </remarks>
    internal static ValueTask<bool> HandshakeCompleteAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AwaitCompletionAsync(
            () => request.Server?.ClientInfo is not null,
            HandshakeGrace,
            TimeProvider.System,
            cancellationToken);
    }

    /// <summary>Builds the line for a call that was refused.</summary>
    /// <param name="tool">Which tool was called.</param>
    /// <param name="client">Who called it.</param>
    /// <param name="method">Why the answer was no.</param>
    /// <param name="reason">keypaste's own explanation. Trusted text.</param>
    /// <param name="exposure">What this server was configured to expose.</param>
    /// <param name="args">What was asked for.</param>
    /// <returns>The record to append.</returns>
    internal static AuditRecord Denial(
        string tool,
        AuditClient client,
        AuditMethod method,
        string reason,
        EntryExposure exposure,
        AuditArgs? args = null) =>
        Line(tool, client, AuditDecision.Denied, method, reason, exposure, args);

    /// <summary>Builds the line for a call, whatever the answer was.</summary>
    /// <param name="tool">Which tool was called.</param>
    /// <param name="client">Who called it.</param>
    /// <param name="decision">Whether anything was released.</param>
    /// <param name="method">How that was decided.</param>
    /// <param name="reason">keypaste's own explanation. Trusted text.</param>
    /// <param name="exposure">What this server was configured to expose.</param>
    /// <param name="args">What was asked for.</param>
    /// <returns>The record to append.</returns>
    /// <remarks>
    /// A grant and a denial go through the same builder and the same fields. Two shapes would be
    /// two chances to leave something out of one of them, and the line that matters most to a
    /// person reading the log later is the one that says yes.
    /// </remarks>
    internal static AuditRecord Line(
        string tool,
        AuditClient client,
        AuditDecision decision,
        AuditMethod method,
        string reason,
        EntryExposure exposure,
        AuditArgs? args = null)
    {
        ArgumentNullException.ThrowIfNull(exposure);

        return new AuditRecord
        {
            Tool = tool,
            Client = client,
            Args = args ?? AuditArgs.None,
            Decision = decision,
            Method = method,
            Reason = reason,
            Exposure = exposure.Globs,
        };
    }

    private static string? Clean(string? value) =>
        value is null ? null : EntryNameSanitizer.Sanitize(value, MaximumClientTextLength).Text;
}
