using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to ask the person to save a value it never sees, at a place it names (F.79). The person enters
/// the value where the vault is unlocked; the agent learns only whether it was saved.
/// </summary>
/// <remarks>
/// No value crosses this file in either direction: the request names a place and a reason, and the reply says whether it was
/// saved. Every call is recorded, by the owner when it answered and here otherwise, as the credential tool's are (T-6).
/// </remarks>
/// <param name="options">What this bridge was started with.</param>
/// <param name="approver">The connection to the vault's owner.</param>
/// <param name="audit">The bridge's audit log.</param>
internal sealed class SaveValueTool(ServerOptions options, ApproverConnection approver, AuditLog audit) : McpServerTool
{
    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => [];

    /// <inheritdoc/>
    public override Tool ProtocolTool { get; } = new()
    {
        Name = ToolText.SaveToolName,
        Title = "Ask the user to save a value",
        Description = ToolText.SaveDescription,
        InputSchema = ToolSchemas.SaveInput,
        Annotations = new ToolAnnotations
        {
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
        var (initialized, client) = await McpAudit.CallerAsync(request, options, cancellationToken).ConfigureAwait(false);
        var asked = new SaveRequest
        {
            Item = ReadString(arguments, "item"),
            Field = ReadString(arguments, "field"),
            Project = ReadString(arguments, "project"),
            Environment = ReadString(arguments, "environment"),
            Key = ReadString(arguments, "key"),
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

        var record = RequestRecords.Save(client, asked, verdict.Decision, verdict.Method, verdict.Reason, verdict.Session, options.VaultKey);

        if (!verdict.Recorded && !audit.TryAppend(record, out _))
        {
            return ToolResults.Refuse(ToolText.AuditUnavailable);
        }

        return verdict.Where is { } where
            ? ToolResults.Saved(where)
            : ToolResults.Refuse(verdict.Refusal ?? (verdict.Method == AuditMethod.Prompt ? ToolText.NotSaved : ToolText.Refusal(verdict.Method)));
    }

    /// <summary>Checks the arguments' shape and what this bridge may see, then asks the owner, which checks both again.</summary>
    private async ValueTask<Verdict> DecideAsync(SaveRequest asked, CancellationToken cancellationToken)
    {
        if (Core.Approval.SaveRequestRules.Check(asked) is { } problem)
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.InvalidRequest, "the request did not satisfy the tool's schema", ToolText.Invalid(problem.Argument, problem.Rule));
        }

        if (!asked.ForProject && !options.Exposure.MayPermit(EntryName.Parse(asked.Item), asked.Field))
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.OutOfScope, "the place is outside what this agent can see through this bridge");
        }

        var (reply, outcome, refusal) = await approver.SaveAsync(asked, cancellationToken).ConfigureAwait(false);

        if (reply is { Recorded: false })
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.Failed, McpAudit.Unrecorded, ToolText.AuditUnavailable, Session: reply.Session);
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
            Where = reply.Decision == AuditDecision.Granted ? reply.Where : null,
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

        /// <summary>Where the value was saved, on the one path that saved it.</summary>
        public string? Where { get; init; }
    }
}
