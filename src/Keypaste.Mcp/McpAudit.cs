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
    /// Stage 2.4 renders in a table — the two places an injection payload would most like to land.
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
        new()
        {
            Tool = tool,
            Client = client,
            Args = args ?? AuditArgs.None,
            Decision = AuditDecision.Denied,
            Method = method,
            Reason = reason,
            Exposure = exposure.Globs,
        };

    private static string? Clean(string? value) =>
        value is null ? null : EntryNameSanitizer.Sanitize(value, MaximumClientTextLength).Text;
}
