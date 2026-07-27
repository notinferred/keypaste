using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to ask for one field of one entry. A person decides every time.
/// </summary>
/// <remarks>
/// <para>
/// CORE.md law 3.2: an agent gets one credential, one scope, one TTL, after one explicit human
/// approval, and the default is deny. This file validates the request, checks it against what the
/// server was configured to expose, forwards it to whoever can ask a person, records the answer,
/// and only then answers the agent.
/// </para>
/// <para>
/// <b>There is still no vault code path in this file, and now no decision either.</b> Not "there is
/// one and it is guarded": there is none, and that is checkable by reading it (THREATS.md T-8). The
/// credential arrives over a pipe from a process that already asked a human, and leaves in the
/// result. Nothing here can release anything on its own.
/// </para>
/// <para>
/// <b>Log first, answer second, always.</b> The record is written before the result is returned,
/// including for a call that was refused before it was understood and for one the client abandoned
/// — an unlogged denial is still an access that happened without a record (law 3.3, THREATS.md
/// T-6). If the log refuses, so does the call.
/// </para>
/// </remarks>
internal sealed class RequestCredentialTool(
    ServerOptions options,
    ApproverConnection approver,
    AuditLog audit) : McpServerTool
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
    public override async ValueTask<CallToolResult> InvokeAsync(
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

        Verdict verdict;

        // Checked before anything is decided, but after the arguments have been read, so the line
        // still records what was asked for. A refusal that does not say what was wanted is a worse
        // audit record than the request deserves (law 3.3).
        if (!McpAudit.HandshakeComplete(request))
        {
            verdict = new Verdict(
                AuditDecision.Denied,
                AuditMethod.NotInitialized,
                "the client called a tool before the initialize handshake completed",
                Refusal: ToolText.NotInitialized);
        }
        else
        {
            try
            {
                verdict = await DecideAsync(client, entry, field, reason, ttl, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The client stopped waiting. It will never read this result, and that is exactly why
                // the line below still gets written: a request that reached a person, or nearly did,
                // is an access whether or not anybody collected the answer.
                verdict = new Verdict(AuditDecision.Denied, AuditMethod.Cancelled, "the client withdrew the request");
            }
        }

        var record = McpAudit.Line(
            ToolText.CredentialToolName,
            client,
            verdict.Decision,
            verdict.Method,
            verdict.Reason,
            options.Exposure,
            args);

        // No cancellation token reaches this, and there is none to forward: appending is synchronous
        // and takes none. That is what makes "every call is logged" true even for the calls nobody
        // is waiting for any more.
        if (!audit.TryAppend(record, out _))
        {
            return ToolResults.Refuse(ToolText.AuditUnavailable);
        }

        return verdict.Released is { } released
            ? ToolResults.Release(released.Field, released.Value, released.TtlSeconds, verdict.Method)
            : ToolResults.Refuse(verdict.Refusal ?? ToolText.Refusal(verdict.Method));
    }

    /// <summary>
    /// Works out the answer. Validation first, then scope, then a person.
    /// </summary>
    /// <remarks>
    /// The order matters twice over: a malformed request never reaches the approver, and an entry
    /// this server was not configured to expose never reaches a human at all — putting an arbitrary
    /// entry name in front of somebody is most of what an attempt to mislead them would need.
    /// </remarks>
    private async ValueTask<Verdict> DecideAsync(
        AuditClient client,
        string entry,
        string field,
        string reason,
        int ttl,
        CancellationToken cancellationToken)
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

        // A path can be checked here without opening anything. A handle cannot — resolving one needs
        // the vault — so it is checked again by the approver after it resolves, which is the only
        // place that check can happen and the reason a handle is not a way around the exposure.
        if (EntryHandle.Classify(entry) == EntryAddressKind.Path && !InScope(entry))
        {
            return new Verdict(
                AuditDecision.Denied,
                AuditMethod.OutOfScope,
                "the entry is outside this server's configured exposure");
        }

        var (reply, reachable) = await approver.RequestAsync(
            new CredentialRequest
            {
                Entry = entry,
                Field = field,
                Reason = reason,
                TtlSeconds = ttl,
                Exposure = options.Exposure.Globs,
                ClientName = client.Name,
                ClientVersion = client.Version,

                // options.ClientLabel, not client.Label: the audit line's copy has been through the
                // sanitizer, and a policy rule has to compare against what the operator actually
                // wrote in the client's configuration. Two labels collapsing to one display string
                // would be a widening.
                ClientLabel = options.ClientLabel,
            },
            cancellationToken).ConfigureAwait(false);

        // Checked after the exchange as well as before it, and this is the important one. A person
        // can say yes in the moment between the client giving up and the reply arriving, and
        // without this the bridge would record a grant — and return a credential — for a request
        // nobody was waiting for. It is the same rule ApprovalGate applies to a late yes from a
        // channel, one layer further out, and it is also why "no answer" is read as cancelled
        // rather than as an approver failure: keypaste did not go wrong, the client left.
        if (cancellationToken.IsCancellationRequested)
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.Cancelled, "the client withdrew the request");
        }

        if (reply is null)
        {
            return reachable
                ? new Verdict(AuditDecision.Denied, AuditMethod.Failed, "the approver could not be asked")
                : new Verdict(AuditDecision.Denied, AuditMethod.NoApprover, "no keypaste agent is running");
        }

        if (reply.Decision != AuditDecision.Granted || reply.Value is not { Length: > 0 })
        {
            return new Verdict(AuditDecision.Denied, reply.Method, reply.Reason);
        }

        return new Verdict(
            AuditDecision.Granted,
            reply.Method,
            reply.Reason,
            new Released(field, reply.Value, reply.TtlSeconds));
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

    private static Verdict Invalid(string field, string rule) =>
        new(
            AuditDecision.Denied,
            AuditMethod.InvalidRequest,
            "the request did not satisfy the tool's schema",
            Refusal: ToolText.Invalid(field, rule));

    /// <summary>The field name if keypaste knows it, or null.</summary>
    private static string? Recognised(string field) =>
        CredentialFields.IsReleasable(field) ? field : null;

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

    /// <summary>One field, released. The only shape in this file that can hold a credential.</summary>
    private readonly record struct Released(string Field, string Value, int TtlSeconds);

    /// <summary>
    /// What was decided, in the two forms it is needed: the words for the log and the answer for
    /// the agent.
    /// </summary>
    /// <param name="Decision">Whether anything was released.</param>
    /// <param name="Method">How it was decided. Also selects the refusal an agent reads.</param>
    /// <param name="Reason">keypaste's own sentence for the log. Never shown to the agent.</param>
    /// <param name="Released">The field, on the one path that has one.</param>
    /// <param name="Refusal">
    /// An override for the agent-facing text, used only where the method alone is not specific
    /// enough — a malformed argument has to name which argument.
    /// </param>
    private readonly record struct Verdict(
        AuditDecision Decision,
        AuditMethod Method,
        string Reason,
        Released? Released = null,
        string? Refusal = null);
}
