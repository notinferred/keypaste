using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to ask for a linked set: two to four fields of one item and of the items <c>find_access</c> links to it,
/// which the person allows once or denies whole (PRODUCT §2, §3.2).
/// </summary>
/// <remarks>
/// It holds no vault and decides nothing, as <see cref="RequestCredentialTool"/> does not: it checks the arguments and what this
/// bridge exposes, forwards the set to the vault's owner, records the calls no owner recorded, and answers the agent. A malformed
/// set, or one naming a path this bridge does not expose, never reaches the owner.
/// </remarks>
/// <param name="options">What this bridge was started with.</param>
/// <param name="approver">The connection to the vault's owner.</param>
/// <param name="audit">The bridge's audit log.</param>
internal sealed class RequestLinkedSetTool(ServerOptions options, ApproverConnection approver, AuditLog audit) : McpServerTool
{
    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => [];

    /// <inheritdoc/>
    public override Tool ProtocolTool { get; } = new()
    {
        Name = ToolText.SetToolName,
        Title = "Request a linked set",
        Description = ToolText.SetDescription,
        InputSchema = ToolSchemas.SetInput,
        Annotations = new ToolAnnotations
        {
            // As request_credential's: a client reads a read-only or idempotent tool as safe to run unasked.
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

        var arguments = request.Params?.Arguments;
        var items = ReadItems(arguments);
        var reason = arguments is not null && arguments.TryGetValue("reason", out var said) && said.ValueKind == JsonValueKind.String
            ? said.GetString() ?? string.Empty
            : string.Empty;

        var (initialized, client) = await McpAudit.CallerAsync(request, options, cancellationToken).ConfigureAwait(false);

        Verdict verdict;

        if (!initialized)
        {
            verdict = new Verdict(
                AuditMethod.NotInitialized,
                "the agent called a tool before the initialize handshake completed",
                Refusal: ToolText.NotInitialized);
        }
        else
        {
            try
            {
                verdict = await DecideAsync(client, items ?? [], items is null, reason, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                verdict = new Verdict(AuditMethod.Cancelled, "the agent withdrew the request");
            }
            catch (Exception)
            {
                // Denying is already the answer; the line below is what has to survive a failure nobody predicted (law 3.7).
                verdict = new Verdict(AuditMethod.Failed, "the request could not be completed");
            }
        }

        if (!verdict.Recorded
            && !audit.TryAppend(
                RequestRecords.SetRefused(client, null, items ?? [], reason, verdict.Method, verdict.Reason, [], options.Exposure.Globs, verdict.Session, options.VaultKey),
                out _))
        {
            return ToolResults.Refuse(ToolText.AuditUnavailable);
        }

        return verdict.Released is { } released && items is not null
            ? ToolResults.ReleaseSet([.. released.Select((field, index) => (items[index].Entry, items[index].Field, field.Value, field.CodeSeconds))])
            : ToolResults.Refuse(verdict.Refusal ?? (verdict.Method == AuditMethod.OutOfScope ? ToolText.SetOutOfScope : ToolText.Refusal(verdict.Method)));
    }

    /// <summary>Works out the answer: the arguments, then what this bridge exposes, then the vault's owner.</summary>
    private async ValueTask<Verdict> DecideAsync(AuditClient client, List<SetItem> items, bool malformed, string reason, CancellationToken cancellationToken)
    {
        if (malformed)
        {
            return Invalid("items", "must be an array of objects, each with an entry and a field");
        }

        if (SetRequestRules.Check(items, reason) is { } problem)
        {
            return Invalid(problem.Argument, problem.Rule);
        }

        // A path can be checked here; a handle or a tag only by the owner, which checks every item again after it resolves it.
        if (items.Any(item => EntryHandle.Classify(item.Entry) == EntryAddressKind.Path && !options.Exposure.MayPermit(EntryName.Parse(item.Entry), item.Field)))
        {
            return new Verdict(AuditMethod.OutOfScope, "an item in the set is outside what this agent can see through this bridge");
        }

        var (reply, outcome, refusal) = await approver.RequestSetAsync(
            new SetRequest
            {
                Items = items,
                Reason = reason,
                Exposure = options.Exposure.Globs,
                ClientName = client.Name,
                ClientVersion = client.Version,

                // The raw label, as request_credential sends it: the agent's settings are kept under what the operator wrote.
                ClientLabel = options.ClientLabel,
            },
            cancellationToken).ConfigureAwait(false);

        if (reply is { Recorded: false })
        {
            return new Verdict(AuditMethod.Failed, McpAudit.Unrecorded, Refusal: ToolText.AuditUnavailable, Session: reply.Session);
        }

        // After the exchange as well as before: a person can say yes between the client giving up and the reply arriving.
        if (cancellationToken.IsCancellationRequested || approver.InputEnded.IsCancellationRequested)
        {
            return new Verdict(AuditMethod.Cancelled, "the agent withdrew the request") { Recorded = reply is not null };
        }

        if (outcome == ApproverOutcome.Busy)
        {
            return new Verdict(AuditMethod.Busy, "another exchange was already in flight on this connection");
        }

        if (reply is null)
        {
            return outcome switch
            {
                ApproverOutcome.Refused when refusal?.Refusal is { } method => new Verdict(method, refusal.Reason),
                ApproverOutcome.NoVault => new Verdict(AuditMethod.NoSession, "this server was started without a vault to ask about", Refusal: ToolText.NoVault),
                ApproverOutcome.Failed => new Verdict(AuditMethod.Failed, "the approver could not be asked"),
                _ => new Verdict(
                    AuditMethod.NoApprover,
                    "no keypaste process holds the vault unlocked",
                    Refusal: outcome == ApproverOutcome.Unreachable && await LockedApp.TellAsync(options, cancellationToken).ConfigureAwait(false)
                        ? ToolText.AppLocked
                        : null),
            };
        }

        // A grant must carry exactly the fields asked, each with a value, or it releases nothing.
        if (reply.Decision != AuditDecision.Granted || reply.Fields.Count != items.Count || reply.Fields.Any(field => field.Value.Length == 0))
        {
            // A grant this bridge refuses is not the owner's line for it, so the bridge writes its own.
            return new Verdict(reply.Decision == AuditDecision.Granted ? AuditMethod.Failed : reply.Method, reply.Reason, Session: reply.Session)
            {
                Recorded = reply.Decision != AuditDecision.Granted,
            };
        }

        return new Verdict(reply.Method, reply.Reason, reply.Fields, Session: reply.Session) { Recorded = true };
    }

    /// <summary>The <c>items</c> argument, or null when it is not an array of objects each holding a string entry and field and nothing else.</summary>
    private static List<SetItem>? ReadItems(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || !arguments.TryGetValue("items", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<SetItem> items = [];

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("field", out var field) || field.ValueKind != JsonValueKind.String
                || item.EnumerateObject().Count() != 2)
            {
                return null;
            }

            items.Add(new SetItem(entry.GetString()!, field.GetString()!));
        }

        return items;
    }

    private static Verdict Invalid(string argument, string rule) =>
        new(AuditMethod.InvalidRequest, "the request did not satisfy the tool's schema", Refusal: ToolText.Invalid(argument, rule));

    /// <summary>What was decided: the words for the log, and the answer for the agent.</summary>
    /// <param name="Method">How it was decided; anything but a release is a denial.</param>
    /// <param name="Reason">keypaste's own sentence for the log.</param>
    /// <param name="Released">The fields, on the one path that has them.</param>
    /// <param name="Refusal">An override for the agent-facing text, where the method alone is not specific enough.</param>
    /// <param name="Session">The unlocked session that answered, for the audit line.</param>
    private readonly record struct Verdict(
        AuditMethod Method,
        string Reason,
        IReadOnlyList<SetField>? Released = null,
        string? Refusal = null,
        string? Session = null)
    {
        /// <summary>Whether the owner recorded this call, so the bridge writes no line of its own.</summary>
        public bool Recorded { get; init; }
    }
}
