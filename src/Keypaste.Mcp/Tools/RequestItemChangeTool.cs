using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to change an item it can see: asking the person to allow moving it to Trash, marking it expired or
/// deleting for good an item it created through an approved save, or adding or removing the needs-update tag unasked (THREATS.md T-57).
/// </summary>
/// <remarks>
/// No value crosses this file in either direction: the request names an item, a change and a reason, and the reply says whether
/// it was made. Every call is recorded, by the owner when it answered and here otherwise, as the save tool's are (T-6).
/// </remarks>
/// <param name="options">What this bridge was started with.</param>
/// <param name="approver">The connection to the vault's owner.</param>
/// <param name="audit">The bridge's audit log.</param>
internal sealed class RequestItemChangeTool(ServerOptions options, ApproverConnection approver, AuditLog audit) : McpServerTool
{
    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => [];

    /// <inheritdoc/>
    public override Tool ProtocolTool { get; } = new()
    {
        Name = ToolText.ItemChangeToolName,
        Title = "Change an item, asking the user where it must",
        Description = ToolText.ItemChangeDescription,
        InputSchema = ToolSchemas.ItemChangeInput,
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false,

            // delete removes an item for good, once the person allows it, so a client is told the truth.
            DestructiveHint = true,
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
        var (initialized, client) = await McpAudit.CallerAsync(request, options, cancellationToken).ConfigureAwait(false);
        var asked = new ItemChangeRequest
        {
            Entry = ReadString(arguments, "entry"),
            Change = ReadString(arguments, "change"),
            Expires = ReadString(arguments, "expires"),
            Reason = ReadString(arguments, "reason"),
            Exposure = options.Exposure.Globs,
            ClientName = client.Name,
            ClientVersion = client.Version,
            ClientLabel = options.ClientLabel,
        };

        Verdict verdict;

        if (!initialized)
        {
            verdict = new Verdict(AuditDecision.Denied, AuditMethod.NotInitialized, "the agent called a tool before the initialize handshake completed", ToolText.NotInitialized);
        }
        else
        {
            try
            {
                verdict = await DecideAsync(asked, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                verdict = new Verdict(AuditDecision.Denied, AuditMethod.Cancelled, "the agent withdrew the request");
            }
            catch (Exception)
            {
                verdict = new Verdict(AuditDecision.Denied, AuditMethod.Failed, "the request could not be completed");
            }
        }

        var record = RequestRecords.ItemChange(client, asked, verdict.Decision, verdict.Method, verdict.Reason, resolved: null, item: null, verdict.Session, options.VaultKey);

        if (!verdict.Recorded && !audit.TryAppend(record, out _))
        {
            return ToolResults.Refuse(ToolText.AuditUnavailable);
        }

        return verdict.Item is { } item
            ? ToolResults.ItemChanged(item, asked.Change)
            : ToolResults.Refuse(verdict.Refusal ?? verdict.Method switch
            {
                AuditMethod.Prompt => ToolText.ItemNotChanged,
                AuditMethod.InvalidRequest => ToolText.ItemChangeRefused,
                _ => ToolText.Refusal(verdict.Method),
            });
    }

    /// <summary>Checks the arguments' shape and what this bridge may see, then asks the owner, which checks both again.</summary>
    private async ValueTask<Verdict> DecideAsync(ItemChangeRequest asked, CancellationToken cancellationToken)
    {
        if (ItemChangeRules.Check(asked, out _) is { } problem)
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.InvalidRequest, "the request did not satisfy the tool's schema", ToolText.Invalid(problem.Argument, problem.Rule));
        }

        // A path can be checked here; a handle only by the owner, which resolves it. Changing an item takes a reach of the
        // whole item, which a project tag never gives.
        if (EntryHandle.Classify(asked.Entry) == EntryAddressKind.Path && options.Exposure.Reach(EntryName.Parse(asked.Entry), []) != ExposureReach.Entry)
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.OutOfScope, "the item is outside what this agent can see whole through this bridge");
        }

        var (reply, outcome, refusal) = await approver.ChangeItemAsync(asked, cancellationToken).ConfigureAwait(false);

        if (reply is { Recorded: false })
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.Failed, McpAudit.Unrecorded, ToolText.ItemChangeUnrecorded, Session: reply.Session);
        }

        if (reply is null)
        {
            return outcome switch
            {
                ApproverOutcome.Busy => new Verdict(AuditDecision.Denied, AuditMethod.Busy, "another exchange was already in flight on this connection"),
                ApproverOutcome.Refused when refusal?.Refusal is { } method => new Verdict(AuditDecision.Denied, method, refusal.Reason),
                ApproverOutcome.NoVault => new Verdict(AuditDecision.Denied, AuditMethod.NoSession, "this server was started without a vault to ask about", ToolText.NoVault),
                ApproverOutcome.Failed => new Verdict(AuditDecision.Denied, AuditMethod.Failed, "the approver could not be asked"),
                _ => new Verdict(
                    AuditDecision.Denied,
                    AuditMethod.NoApprover,
                    "no keypaste process holds the vault unlocked",
                    outcome == ApproverOutcome.Unreachable && await LockedApp.TellAsync(options, cancellationToken).ConfigureAwait(false) ? ToolText.AppLocked : null),
            };
        }

        return new Verdict(reply.Decision, reply.Method, reply.Reason, Session: reply.Session)
        {
            Recorded = true,
            Item = reply.Decision == AuditDecision.Granted ? reply.Item : null,
        };
    }

    private static string ReadString(IDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null && arguments.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>What was decided: the words for the log, and the answer for the agent.</summary>
    private readonly record struct Verdict(AuditDecision Decision, AuditMethod Method, string Reason, string? Refusal = null, string? Session = null)
    {
        /// <summary>Whether the owner recorded this call, so the bridge writes no line of its own.</summary>
        public bool Recorded { get; init; }

        /// <summary>The item changed, on the one path that changed it.</summary>
        public string? Item { get; init; }
    }
}
