using System.Globalization;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to ask for a change to its own settings. One that gives it more waits for a person.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own settings and nobody else's.</b> The request carries this bridge's <c>--client-label</c>
/// and no name the agent supplies, so an agent can ask only about the settings that follow it. A
/// bridge started without a label has none of its own, and is refused before anybody is asked. The
/// owner checks the claim again against the label the connection attached as.
/// </para>
/// <para>
/// <b>Bounds, never their removal.</b> An agent may ask for Automatic until a time or at most so many an
/// hour, and the person sees the limit before confirming; it cannot ask for a limit to be removed or for
/// its settings to be reset. A time it gives is read on this computer's clock unless it names its
/// offset, and sent in UTC, as <c>keypaste clients</c> sends one.
/// </para>
/// <para>
/// <b>The owner decides.</b> It classifies the change: one that gives the agent nothing more applies
/// at once on this computer, and any other is put to the person with the agent's reason, under the
/// window, slot and cooldown a credential request has (THREATS.md T-11). This file validates the
/// request, forwards it and answers the agent. With nothing holding the vault it makes only a change
/// that lowers this computer's ceilings, which any program running as the person can already make,
/// and never one that could give more.
/// </para>
/// <para>
/// <b>Log first, answer second, once.</b> The owner records every change it answers, and says so in
/// its reply; this file records the rest before the result is returned, as the credential tool does
/// (THREATS.md T-6).
/// </para>
/// </remarks>
internal sealed class RequestAccessChangeTool(
    ServerOptions options,
    ApproverConnection approver,
    AuditLog audit,
    TimeProvider clock) : McpServerTool
{
    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => [];

    /// <inheritdoc/>
    public override Tool ProtocolTool { get; } = new()
    {
        Name = ToolText.AccessToolName,
        Title = "Ask for a change to this agent's settings",
        Description = ToolText.AccessDescription,
        InputSchema = ToolSchemas.AccessChangeInput,
        Annotations = new ToolAnnotations
        {
            // Never read-only or idempotent, so no client runs it unasked or folds two calls into one: each
            // is a request a person may answer. Not destructive: it changes what this agent may reach and
            // nothing the person holds, and what gives the agent more waits for them.
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
        var reason = arguments is not null
            && arguments.TryGetValue("reason", out var element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : string.Empty;

        var (initialized, client) = await McpAudit.CallerAsync(request, options, cancellationToken).ConfigureAwait(false);

        Verdict verdict;

        if (!initialized)
        {
            verdict = new Verdict(
                AuditDecision.Denied,
                AuditMethod.NotInitialized,
                "the agent called a tool before the initialize handshake completed",
                ToolText.NotInitialized);
        }
        else
        {
            try
            {
                verdict = await DecideAsync(arguments, reason, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                verdict = new Verdict(AuditDecision.Denied, AuditMethod.Cancelled, "the agent withdrew the request", ToolText.Cancelled);
            }
            catch (Exception)
            {
                // As in the credential tool: an exception is the shape most likely to skip the line below.
                verdict = new Verdict(AuditDecision.Denied, AuditMethod.Failed, "the request could not be completed", ToolText.AccessFailed);
            }
        }

        var record = RequestRecords.AccessChange(
            client,
            reason,
            verdict.Decision,
            verdict.Method,
            verdict.Reason,
            session: null,
            options.VaultKey);

        if (!verdict.Recorded && !audit.TryAppend(record, out _))
        {
            // A change already made stays made, since refusing to say so would only hide it: what gave the
            // agent nothing more needs no record to be safe, and what gave more a person confirmed.
            return verdict.Decision == AuditDecision.Granted
                ? ToolResults.Changed(ToolText.AccessUnrecorded(verdict.Method))
                : ToolResults.Refuse(ToolText.AuditUnavailable);
        }

        return verdict.Decision == AuditDecision.Granted
            ? ToolResults.Changed(verdict.Said)
            : ToolResults.Refuse(verdict.Said);
    }

    /// <summary>Works out the answer: this bridge's name first, then the arguments, then the owner.</summary>
    private async ValueTask<Verdict> DecideAsync(
        IDictionary<string, JsonElement>? arguments,
        string reason,
        CancellationToken cancellationToken)
    {
        if (options.ClientLabel is not { } label)
        {
            return new Verdict(
                AuditDecision.Denied,
                AuditMethod.InvalidRequest,
                "this bridge was started without --client-label, so its agent has no settings of its own to change",
                ToolText.AccessUnnamed);
        }

        CredentialRequestProblem? problem = null;
        var asked = new AccessChangeRequest(label, reason)
        {
            Origin = AccessOrigin.Agent,
            Mode = Word(arguments, "mode", AccessChangeRules.ModeRule, ref problem),
            Project = Word(arguments, "project", AccessChangeRules.ProjectRule, ref problem),
            See = Patterns(arguments, ref problem),
            Values = Word(arguments, "values", AccessChangeRules.ValuesRule, ref problem),
            Run = Word(arguments, "run", AccessChangeRules.RunRule, ref problem),
            PerHour = PerHour(arguments, ref problem),
            Until = Until(arguments, ref problem),
        };

        if ((problem ?? AccessChangeRules.Check(asked)) is { } broken)
        {
            return new Verdict(
                AuditDecision.Denied,
                AuditMethod.InvalidRequest,
                $"the {broken.Argument} argument {broken.Rule}",
                ToolText.Invalid(broken.Argument, broken.Rule));
        }

        if (!AccessEdit.TryCreate(asked.Project, asked.Mode, asked.See, asked.Values, asked.Run, asked.PerHour, asked.Until, false, out var edit, out _))
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.InvalidRequest, "the request named nothing to change", ToolText.AccessNothing);
        }

        // The change itself on the lines this bridge writes, as the owner names it on the lines it writes.
        var named = $" ({EntryNameSanitizer.Sanitize(label, ApprovalPrompt.MaximumClientLength).Text}: {edit})";

        var (reply, outcome, refusal) = await approver.ChangeAccessAsync(asked, cancellationToken).ConfigureAwait(false);

        if (reply is { Recorded: false })
        {
            // The change may have been made, so this line says what the owner said happened.
            return new Verdict(reply.Decision, reply.Method, $"{reply.Reason}; {McpAudit.Unrecorded}", Told(reply));
        }

        if (reply is not null)
        {
            return new Verdict(reply.Decision, reply.Method, reply.Reason, Told(reply)) { Recorded = true };
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.Cancelled, "the agent withdrew the request" + named, ToolText.Cancelled);
        }

        return outcome switch
        {
            ApproverOutcome.Busy =>
                new Verdict(AuditDecision.Denied, AuditMethod.Busy, "another exchange was already in flight on this connection" + named, ToolText.Busy),
            ApproverOutcome.Refused when refusal?.Refusal is { } method =>
                new Verdict(AuditDecision.Denied, method, refusal.Reason + named, ToolText.AccessRefusal(method)),
            ApproverOutcome.NoVault =>
                new Verdict(AuditDecision.Denied, AuditMethod.NoSession, "this server was started without a vault to ask about" + named, ToolText.NoVault),
            ApproverOutcome.Failed =>
                new Verdict(AuditDecision.Denied, AuditMethod.Failed, "the approver could not be asked" + named, ToolText.AccessFailed),
            ApproverOutcome.Unreachable => await LoweredAsync(label, edit, named, cancellationToken).ConfigureAwait(false),
            _ => new Verdict(AuditDecision.Denied, AuditMethod.NoApprover, "no keypaste process holds the vault unlocked" + named, ToolText.AccessNoOwner),
        };
    }

    /// <summary>
    /// With nothing holding the vault, makes a change that only lowers this computer's ceilings, as
    /// <c>keypaste clients</c> does, so an agent's narrowing of itself holds while the vault is locked;
    /// anything that could give more, or that only the vault's settings hold, waits for an owner.
    /// </summary>
    private async ValueTask<Verdict> LoweredAsync(string label, AccessEdit edit, string named, CancellationToken cancellationToken)
    {
        const string nobody = "no keypaste process holds the vault unlocked";

        if (options.ClientsPath is not { } clients)
        {
            return new Verdict(
                AuditDecision.Denied,
                AuditMethod.NoApprover,
                $"{nobody}, and this bridge's --audit-log is not its home's and it was given no --clients, so it cannot tell which ceilings the owner reads" + named,
                ToolText.AccessCeilingsUnknown);
        }

        CapsChange changed;
        string problem;

        try
        {
            changed = AccessCaps.TryChange(clients, current => current.TryLower(label, edit, out var lowered) ? lowered : null, out problem);
        }
        catch (ArgumentException tooMany)
        {
            return new Verdict(AuditDecision.Denied, AuditMethod.Failed, $"{nobody}, and this computer's ceilings could not hold it: {tooMany.Message}" + named, ToolText.AccessFailed);
        }

        switch (changed)
        {
            case CapsChange.Unreadable:
                return new Verdict(AuditDecision.Denied, AuditMethod.Failed, $"{nobody}, and this computer's ceilings are not usable: {problem}" + named, ToolText.AccessFailed);
            case CapsChange.Declined:
                return new Verdict(
                    AuditDecision.Denied,
                    AuditMethod.NoApprover,
                    nobody + named,
                    await LockedApp.TellAsync(options, cancellationToken).ConfigureAwait(false) ? ToolText.AccessAppLocked : ToolText.AccessNoOwner);
            case CapsChange.Unwritten:
                return new Verdict(AuditDecision.Denied, AuditMethod.Failed, $"{nobody}, and this computer's ceilings could not be written: {problem}" + named, ToolText.AccessFailed);
        }

        return new Verdict(
            AuditDecision.Granted,
            AuditMethod.Narrowing,
            "applied on this computer with nothing holding the vault, since it gives the agent nothing more" + named,
            ToolText.AccessNarrowed);
    }

    /// <summary>What the agent reads for the owner's answer.</summary>
    private static string Told(AccessChangeReply reply) => reply.Decision != AuditDecision.Granted
        ? ToolText.AccessRefusal(reply.Method)
        : reply.Method == AuditMethod.Narrowing ? ToolText.AccessNarrowed : ToolText.AccessConfirmed;

    /// <summary>An optional word: null when it is absent, and a problem, the first one found, when it is not a string.</summary>
    private static string? Word(IDictionary<string, JsonElement>? arguments, string name, string rule, ref CredentialRequestProblem? problem)
    {
        if (arguments is null || !arguments.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        problem ??= new CredentialRequestProblem(name, rule);
        return null;
    }

    /// <summary>The <c>per_hour</c> limit in the words the owner reads: null when absent, and a problem when it is not a whole number in range.</summary>
    private static string? PerHour(IDictionary<string, JsonElement>? arguments, ref CredentialRequestProblem? problem)
    {
        if (arguments is null || !arguments.TryGetValue("per_hour", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var perHour)
            && perHour is >= 1 and <= AccessLimit.MaximumPerHour)
        {
            return perHour.ToString(CultureInfo.InvariantCulture);
        }

        problem ??= new CredentialRequestProblem("per_hour", AccessChangeRules.PerHourRule);
        return null;
    }

    /// <summary>The <c>until</c> time, read on this computer's clock and sent in UTC: null when absent, and a problem when it is not a time still to come.</summary>
    private string? Until(IDictionary<string, JsonElement>? arguments, ref CredentialRequestProblem? problem)
    {
        if (arguments is null || !arguments.TryGetValue("until", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String
            && element.GetString() is { } text
            && UntilTime.TryRead(text, clock.LocalTimeZone, clock.GetUtcNow(), out var until, out _))
        {
            return UntilTime.Recorded(until);
        }

        problem ??= new CredentialRequestProblem("until", AccessChangeRules.UntilRule);
        return null;
    }

    /// <summary>The <c>see</c> patterns: null when absent, and a problem when they are not a short list of strings.</summary>
    private static List<string>? Patterns(IDictionary<string, JsonElement>? arguments, ref CredentialRequestProblem? problem)
    {
        if (arguments is null || !arguments.TryGetValue("see", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Array
            && element.GetArrayLength() <= EntryExposure.MaximumGlobs
            && element.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return [.. element.EnumerateArray().Select(item => item.GetString() ?? string.Empty)];
        }

        problem ??= new CredentialRequestProblem("see", AccessChangeRules.SeeRule);
        return null;
    }

    /// <summary>What was decided, in the two forms it is needed: the words for the log and the answer for the agent.</summary>
    /// <param name="Decision">Whether the change took effect.</param>
    /// <param name="Method">How it was decided.</param>
    /// <param name="Reason">keypaste's own sentence for the log, naming the change. Never shown to the agent.</param>
    /// <param name="Said">What the agent reads.</param>
    private readonly record struct Verdict(AuditDecision Decision, AuditMethod Method, string Reason, string Said)
    {
        /// <summary>Whether the owner recorded this call, so the bridge writes no line of its own.</summary>
        public bool Recorded { get; init; }
    }
}
