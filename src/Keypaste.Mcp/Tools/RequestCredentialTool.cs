using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to ask for one field of one entry. It denies every time.
/// </summary>
/// <remarks>
/// <para>
/// CORE.md law 3.2: an agent gets one credential, one scope, one TTL, after one explicit human
/// approval, and the default is deny. The human approval flow arrives in Stage 2.2. Until it does,
/// the honest implementation of law 3.2 is to refuse — a bridge that grants before it can ask is
/// the one bug this project cannot ship.
/// </para>
/// <para>
/// <b>There is no vault code path in this file.</b> Not "there is one and it is disabled": there is
/// none, and that is checkable by reading it (THREATS.md T-8). What the file does do is validate,
/// classify and record, so that Stage 2.2 adds an approval step rather than building the whole
/// mechanism at the moment it first has a secret to hand out.
/// </para>
/// </remarks>
internal sealed class RequestCredentialTool(ServerOptions options, AuditLog audit) : McpServerTool
{
    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => [];

    /// <inheritdoc/>
    public override Tool ProtocolTool { get; } = new()
    {
        Name = ToolText.CredentialToolName,
        Title = "Request one credential",
        Description = ToolText.CredentialDescription,
        InputSchema = ToolSchemas.CredentialInput,
        Annotations = new ToolAnnotations
        {
            // ReadOnlyHint is false, and that is deliberate rather than sloppy. Literally the tool
            // only reads. But clients treat readOnlyHint as "safe to run without asking the user",
            // and answering yes to that question about a credential release would invite exactly
            // the auto-approval law 3.2 exists to prevent. Idempotent is false for the same reason:
            // each call is a separate approval event, and must not look replayable.
            ReadOnlyHint = false,
            DestructiveHint = false,
            IdempotentHint = false,
            OpenWorldHint = false,
        },
    };

    /// <inheritdoc/>
    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = McpAudit.ClientOf(request, options);
        var arguments = request.Params?.Arguments;

        var entry = ReadString(arguments, "entry");
        var field = ReadString(arguments, "field");
        var reason = ReadString(arguments, "reason");
        var ttl = ReadInteger(arguments, "ttl_seconds");

        var args = AuditArgs.ForCredentialRequest(entry, Recognised(field), ttl, reason);

        var (method, answer) = Judge(entry, field, reason, ttl);

        var record = McpAudit.Denial(
            ToolText.CredentialToolName,
            client,
            method,
            Explain(method),
            options.Exposure,
            args);

        // Log first, and refuse if the log refuses. Even a malformed call gets a line: an unlogged
        // denial is still a denial that happened without a record (CORE.md law 3.3).
        return new ValueTask<CallToolResult>(
            audit.TryAppend(record, out _) ? answer : ToolResults.Refuse(ToolText.AuditUnavailable));
    }

    /// <summary>
    /// Decides why the answer is no. Validation first, then scope, then the standing refusal.
    /// </summary>
    /// <remarks>
    /// The distinction is what makes Stage 2.2 an added branch rather than a rebuild, and it is what
    /// an agent needs in order to stop retrying: <c>out-of-scope</c> means keypaste will never
    /// discuss that entry, <c>not-implemented</c> means keypaste cannot ask yet.
    /// </remarks>
    private (AuditMethod Method, CallToolResult Answer) Judge(
        string entry,
        string field,
        string reason,
        int ttl)
    {
        if (entry.Length is 0 or > ToolSchemas.MaximumEntryLength)
        {
            return Invalid("entry", $"must be 1 to {ToolSchemas.MaximumEntryLength} characters");
        }

        if (Recognised(field) is null)
        {
            return Invalid("field", $"must be one of: {string.Join(", ", ToolSchemas.AllowedFields)}");
        }

        if (reason.Length is 0 or > ToolSchemas.MaximumReasonLength)
        {
            return Invalid("reason", $"must be 1 to {ToolSchemas.MaximumReasonLength} characters");
        }

        if (ttl is < 1 or > ToolSchemas.MaximumTtlSeconds)
        {
            return Invalid("ttl_seconds", $"must be between 1 and {ToolSchemas.MaximumTtlSeconds}");
        }

        // A path can be checked against the exposure without opening anything. A handle cannot —
        // resolving one needs the vault — so a handle falls through to the standing refusal rather
        // than being guessed at in either direction.
        if (EntryHandle.Classify(entry) == EntryAddressKind.Path && !InScope(entry))
        {
            return (AuditMethod.OutOfScope, ToolResults.Refuse(ToolText.OutOfScope));
        }

        return (AuditMethod.NotImplemented, ToolResults.Refuse(ToolText.Denied));
    }

    /// <summary>Whether a path-shaped argument names something this server may discuss.</summary>
    private bool InScope(string entry)
    {
        var separator = entry.LastIndexOf('/');

        var name = separator < 0
            ? new EntryName(string.Empty, entry)
            : new EntryName(entry[..separator], entry[(separator + 1)..]);

        return options.Exposure.Allows(name);
    }

    private static (AuditMethod Method, CallToolResult Answer) Invalid(string field, string rule) =>
        (AuditMethod.InvalidRequest, ToolResults.Refuse(ToolText.Invalid(field, rule)));

    private static string Explain(AuditMethod method) => method switch
    {
        AuditMethod.InvalidRequest => "the request did not satisfy the tool's schema",
        AuditMethod.OutOfScope => "the entry is outside this server's configured exposure",
        _ => "there is no approval path in this version, so the default deny stands",
    };

    /// <summary>The field name if keypaste knows it, or null.</summary>
    private static string? Recognised(string field)
    {
        foreach (var allowed in ToolSchemas.AllowedFields)
        {
            if (string.Equals(field, allowed, StringComparison.Ordinal))
            {
                return allowed;
            }
        }

        return null;
    }

    private static string ReadString(IDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null
        && arguments.TryGetValue(name, out var element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Reads an integer argument, or <c>-1</c> when it is missing or unusable.</summary>
    /// <remarks>
    /// A sentinel rather than a nullable, because the audit line records what was asked for and
    /// "nothing usable" is a thing worth recording rather than omitting.
    /// </remarks>
    private static int ReadInteger(IDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null
        && arguments.TryGetValue(name, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt32(out var value)
            ? value
            : -1;
}
