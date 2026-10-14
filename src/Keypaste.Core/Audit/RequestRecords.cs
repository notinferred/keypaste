using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tokens;

namespace Keypaste.Core.Audit;

/// <summary>The tool name each kind of agent request is recorded under.</summary>
public static class AuditTools
{
    /// <summary>A listing of entry names.</summary>
    public const string List = "list_entry_names";

    /// <summary>A request for one field of one entry.</summary>
    public const string Credential = "request_credential";

    /// <summary>A command run with released values in its environment: an agent's, or <c>keypaste run --session</c>'s or <c>--token</c>'s.</summary>
    public const string Run = "run";

    /// <summary>A change to an agent's settings, from whichever surface asked for it.</summary>
    public const string AccessChange = "access_change";
}

/// <summary>The audit line of each kind of agent request, built here for every process that writes one.</summary>
/// <remarks>
/// A grant and a denial go through the same builder and the same fields, whichever process records
/// them. Two shapes would be two chances to leave something out of one of them, and the line that
/// matters most to a person reading the log later is the one that says yes. Nothing a builder is
/// given is written as a value: a reply's released set gives the line its entries and its count.
/// </remarks>
public static class RequestRecords
{
    /// <summary>The client a <c>keypaste run --session</c> request is recorded under.</summary>
    public const string SessionRunner = "keypaste run --session";

    /// <summary>The line for a listing of entry names.</summary>
    /// <param name="client">Who asked.</param>
    /// <param name="decision">Whether anything was listed.</param>
    /// <param name="method">How that was decided.</param>
    /// <param name="reason">keypaste's own explanation. Trusted text.</param>
    /// <param name="exposure">The globs the asking bridge was configured to expose.</param>
    /// <param name="session">The unlocked session that answered, or null.</param>
    /// <param name="vault">The vault's identity key, or null when none was named.</param>
    /// <returns>The record to append.</returns>
    public static AuditRecord Listing(
        AuditClient client,
        AuditDecision decision,
        AuditMethod method,
        string reason,
        IReadOnlyList<string> exposure,
        string? session,
        string? vault)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(exposure);

        return new AuditRecord
        {
            Tool = AuditTools.List,
            Client = client,
            Decision = decision,
            Method = method,
            Reason = reason,
            Exposure = exposure,
            Session = session,
            Vault = vault,
        };
    }

    /// <summary>The line for a listing that named entries.</summary>
    /// <param name="client">Who asked.</param>
    /// <param name="count">How many entries the listing named.</param>
    /// <param name="exposure">The globs the asking bridge was configured to expose.</param>
    /// <param name="session">The unlocked session that answered, or null.</param>
    /// <param name="vault">The vault's identity key, or null when none was named.</param>
    /// <returns>The record to append.</returns>
    public static AuditRecord Listed(
        AuditClient client,
        int count,
        IReadOnlyList<string> exposure,
        string? session,
        string? vault) =>
        Listing(client, AuditDecision.Granted, AuditMethod.Exposure, $"listed {count} entry names", exposure, session, vault);

    /// <summary>The line for a request for one field of one entry.</summary>
    /// <param name="client">Who asked.</param>
    /// <param name="entry">The <c>entry</c> argument as the agent wrote it.</param>
    /// <param name="field">The <c>field</c> argument as the agent wrote it; one keypaste does not release is recorded as <c>invalid</c>.</param>
    /// <param name="ttlSeconds">The lifetime the agent asked for, or <c>-1</c> when it was missing or unusable.</param>
    /// <param name="statedReason">The agent's stated reason, verbatim.</param>
    /// <param name="decision">Whether the field was released.</param>
    /// <param name="method">How that was decided.</param>
    /// <param name="reason">keypaste's own explanation. Trusted text.</param>
    /// <param name="resolvedEntry">The entry the owner resolved the argument to, or null when it got no further.</param>
    /// <param name="grantedSeconds">How long the release stays granted; kept only on a grant.</param>
    /// <param name="exposure">The globs the asking bridge was configured to expose.</param>
    /// <param name="session">The unlocked session that answered, or null.</param>
    /// <param name="vault">The vault's identity key, or null when none was named.</param>
    /// <returns>The record to append.</returns>
    public static AuditRecord Credential(
        AuditClient client,
        string entry,
        string field,
        int ttlSeconds,
        string statedReason,
        AuditDecision decision,
        AuditMethod method,
        string reason,
        string? resolvedEntry,
        int? grantedSeconds,
        IReadOnlyList<string> exposure,
        string? session,
        string? vault)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(exposure);

        var args = AuditArgs.ForCredentialRequest(entry, CredentialFields.IsReleasable(field) ? field : null, ttlSeconds, statedReason);

        // The agent is told to prefer a handle, so the argument is usually an opaque k1_… that
        // answers nothing a reader of the log asks; the name it resolved to does.
        if (resolvedEntry is { Length: > 0 })
        {
            args = args with
            {
                Entry = EntryNameSanitizer.SanitizePath(resolvedEntry, maximumLength: AuditArgs.EntryLength).Text,
                EntryKind = EntryAddressKind.Path,
            };
        }

        return new AuditRecord
        {
            Tool = AuditTools.Credential,
            Client = client,
            Args = args,
            Decision = decision,
            Method = method,
            Reason = reason,
            Exposure = exposure,
            Session = session,
            GrantedSeconds = decision == AuditDecision.Granted ? grantedSeconds : null,
            Vault = vault,
        };
    }

    /// <summary>The line for an agent's request to run a command.</summary>
    /// <param name="client">Who asked.</param>
    /// <param name="asked">The run as the agent asked for it.</param>
    /// <param name="program">The program the command resolved to, or null before it was resolved.</param>
    /// <param name="decision">Whether the values were released.</param>
    /// <param name="method">How that was decided.</param>
    /// <param name="reason">keypaste's own explanation. Trusted text.</param>
    /// <param name="grantedSeconds">How long the release stays granted; kept only on a grant.</param>
    /// <param name="entries">Each entry the run released or asked about, as <see cref="ApprovalPrompt.Shown"/> writes it, or null.</param>
    /// <param name="exposure">The globs the asking bridge was configured to expose.</param>
    /// <param name="session">The unlocked session that answered, or null.</param>
    /// <param name="vault">The vault's identity key, or null when none was named.</param>
    /// <returns>The record to append, its command cut to <see cref="AuditRecord.CommandLength"/> and its whole argv committed to by hash.</returns>
    public static AuditRecord Run(
        AuditClient client,
        RunArguments asked,
        string? program,
        AuditDecision decision,
        AuditMethod method,
        string reason,
        int? grantedSeconds,
        IReadOnlyList<string>? entries,
        IReadOnlyList<string> exposure,
        string? session,
        string? vault)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(exposure);

        var group = asked.Project is { } project && asked.References is null
            ? EnvProfileNames.SetName(project, asked.Profile ?? EnvProfileNames.Default)
            : null;
        var shown = DisplayTextSanitizer.Sanitize(EnvReleasePrompt.CommandLine(asked.Command), EnvReleasePrompt.MaximumCommandLength).Text;

        return new AuditRecord
        {
            Tool = AuditTools.Run,
            Client = client,
            Args = AuditArgs.ForRun(group, asked.Reason),
            Decision = decision,
            Method = method,
            Reason = reason,
            Exposure = exposure,
            Session = session,
            GrantedSeconds = decision == AuditDecision.Granted ? grantedSeconds : null,
            Vault = vault,
            Entries = entries,
            Command = shown.Length > AuditRecord.CommandLength ? shown[..(AuditRecord.CommandLength - 1)] + "…" : shown,
            CommandSha256 = asked.Command.Count == 0 ? null : AuditRecord.HashOf([program ?? asked.Command[0], .. asked.Command.Skip(1)]),
        };
    }

    /// <summary>The line for a request to change an agent's settings.</summary>
    /// <param name="client">Who asked: the surface's own name, or the agent's client, with the label of the agent the change is for.</param>
    /// <param name="statedReason">The requester's own reason, verbatim, or empty when it gave none.</param>
    /// <param name="decision">Whether the change took effect.</param>
    /// <param name="method">How that was decided: a person's confirmation, a narrowing applied at once, or a refusal.</param>
    /// <param name="reason">keypaste's own explanation, naming the change. Trusted text.</param>
    /// <param name="session">The unlocked session that answered, or null.</param>
    /// <param name="vault">The vault's identity key, or null when none was named.</param>
    /// <returns>The record to append.</returns>
    public static AuditRecord AccessChange(
        AuditClient client,
        string statedReason,
        AuditDecision decision,
        AuditMethod method,
        string reason,
        string? session,
        string? vault)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(statedReason);
        ArgumentNullException.ThrowIfNull(reason);

        return new AuditRecord
        {
            Tool = AuditTools.AccessChange,
            Client = client,
            Args = statedReason.Length == 0 ? AuditArgs.None : AuditArgs.ForRun(null, statedReason),
            Decision = decision,
            Method = method,
            Reason = reason,
            Session = session,
            Vault = vault,
        };
    }

    /// <summary>The line for a <c>keypaste run --session</c> request.</summary>
    /// <param name="project">The project asked for.</param>
    /// <param name="profile">The profile asked for.</param>
    /// <param name="method">How it was decided.</param>
    /// <param name="reply">The owner's answer; a released set gives the line its entries and its count.</param>
    /// <param name="session">The session the request named.</param>
    /// <param name="vault">The vault's identity key.</param>
    /// <param name="confirmedInApp">Whether the request carried the launch pass of the app's Run confirmation, which a release's reason then names.</param>
    /// <returns>The record to append.</returns>
    public static AuditRecord EnvRun(
        string project,
        string profile,
        AuditMethod method,
        EnvReply reply,
        string? session,
        string? vault,
        bool confirmedInApp = false) =>
        SetRun(
            new AuditClient(SessionRunner, CoreInfo.Version, null),
            project,
            profile,
            method,
            reply,
            said => confirmedInApp && reply.Set.Outcome == EnvOutcome.Resolved ? $"{said}, confirmed in the app" : said,
            session,
            vault);

    /// <summary>The line for a <c>keypaste run --token</c> request.</summary>
    /// <param name="project">The project asked for.</param>
    /// <param name="profile">The profile asked for.</param>
    /// <param name="token">The token the request verified as, or null when it did not verify.</param>
    /// <param name="reply">The owner's answer; a released set gives the line its entries and its count.</param>
    /// <param name="session">The session the request named.</param>
    /// <param name="vault">The vault's identity key.</param>
    /// <returns>The record to append, its reason naming the token as <see cref="TokenAuditReason"/> words it.</returns>
    public static AuditRecord TokenRun(
        string project,
        string profile,
        TokenInfo? token,
        EnvReply reply,
        string? session,
        string? vault) =>
        SetRun(
            new AuditClient("keypaste run --token", CoreInfo.Version, null),
            project,
            profile,
            AuditMethod.Token,
            reply,
            said => token is null ? said : TokenAuditReason.Format(token.Id, EntryNameSanitizer.Sanitize(token.Name).Text, said),
            session,
            vault);

    private static AuditRecord SetRun(
        AuditClient client,
        string project,
        string profile,
        AuditMethod method,
        EnvReply reply,
        Func<string, string> reason,
        string? session,
        string? vault)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(reply);

        var released = reply.Set.Outcome == EnvOutcome.Resolved;
        var said = released
            ? $"{reply.Set.Variables.Count} variable(s)"
            : reply.Reason.Length > 0 ? reply.Reason : reply.Set.Refusal;

        return new AuditRecord
        {
            Tool = AuditTools.Run,
            Client = client,
            Args = new AuditArgs
            {
                Entry = EntryNameSanitizer.SanitizePath(
                    $"{EnvConvention.RootGroup}/{project}/{profile}",
                    maximumLength: AuditArgs.EntryLength).Text,
            },
            Decision = released ? AuditDecision.Granted : AuditDecision.Denied,
            Method = method,
            Reason = reason(said),
            Session = session,
            Vault = vault,
            Entries = released ? reply.Set.Entries.Select(ApprovalPrompt.Shown).ToList() : null,
        };
    }
}
