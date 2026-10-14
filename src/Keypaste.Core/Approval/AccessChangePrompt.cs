using System.Globalization;
using Keypaste.Core.Agents;

namespace Keypaste.Core.Approval;

/// <summary>Where a request to change an agent's settings says it came from.</summary>
public enum AccessOrigin
{
    /// <summary><c>keypaste mcp policy</c> in a terminal, which any program the person runs could have typed.</summary>
    Terminal = 0,

    /// <summary>The agent itself, through its keypaste tool.</summary>
    Agent = 1,

    /// <summary>The person, choosing Always for in an approval prompt.</summary>
    Prompt = 2,

    /// <summary>The person, in the owner's own agent settings. Never read from a pipe.</summary>
    Settings = 3,
}

/// <summary>
/// What a person is shown before a change that gives an agent more takes effect: the agent, exactly
/// what it would gain, where the request says it came from and the requester's reason, already sanitized.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="ApprovalPrompt"/> it has no member for a default button, a deadline or a layout
/// (THREATS.md T-2), and the gains are keypaste's own words built from the parsed change. The selectors
/// and project names in them came from the requester, so each character that could hide or reorder text
/// is shown as an escape (<see cref="DisplayTextSanitizer.Escaped"/>) and none is left out.
/// </para>
/// <para>
/// A terminal command can be typed by any program the person runs, an agent with a shell included, so
/// the origin is shown as a claim and the prompt never says the person asked.
/// </para>
/// </remarks>
public sealed record AccessChangePrompt
{
    /// <summary>Why a change a person allows does not yet give an agent more than the defaults, said after "but".</summary>
    public const string BeneathDefaultsText =
        "keypaste does not wholly understand this agent's saved settings, so the agent gets no more than the defaults until they are fixed";

    /// <summary>What a reset does, said after "Allowing".</summary>
    public const string ResetText =
        "clears every ceiling this computer kept on agents, and keeps the file keypaste cannot read beside it as clients.toml"
        + AccessCaps.KeptSuffix + ", or the next free name, so no copy kept before is replaced";

    /// <summary>The agent's name, sanitized.</summary>
    public required string Agent { get; init; }

    /// <summary>Each thing the change gives the agent, in keypaste's own words.</summary>
    public required IReadOnlyList<string> Gains { get; init; }

    /// <summary>Where the request says it came from.</summary>
    public required AccessOrigin Origin { get; init; }

    /// <summary>The requester's own reason, sanitized and capped; empty when it gave none. Untrusted text.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether the reason shown is shorter than the one sent.</summary>
    public required bool ReasonWasTruncated { get; init; }

    /// <summary>Whether the reason had anything scrubbed out of it.</summary>
    public required bool ReasonWasAltered { get; init; }

    /// <summary>
    /// Whether the agent's record would still hold parts keypaste does not understand once the change is
    /// written, so the agent gets no more than the defaults until they are fixed (<see cref="BeneathDefaultsText"/>).
    /// </summary>
    public bool StaysBeneathDefaults { get; init; }

    /// <summary>
    /// Whether this resets this computer's ceilings, which keypaste cannot read, for every agent at once:
    /// <see cref="Agent"/> is then every agent, and <see cref="Gains"/> what each gets beyond the defaults.
    /// </summary>
    public bool ResetsCeilings { get; init; }

    /// <summary>The prompt for resetting this computer's ceilings, which keypaste cannot read.</summary>
    /// <param name="records">Every record the vault holds, as <see cref="IAgentSettings.List"/> reads them.</param>
    /// <param name="zone">This computer's time zone, which an end to Automatic is shown in.</param>
    /// <returns>A prompt naming, for each agent, what the vault's settings give it beyond what an agent with no settings of its own gets.</returns>
    /// <remarks>
    /// What the file held back cannot be read, so the gains are against the defaults rather than against what
    /// applied; a record that applies only beneath the defaults gives nothing beyond them.
    /// </remarks>
    public static AccessChangePrompt ForReset(AgentRecordList records, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(zone);

        List<AcafRead> any = [.. records.Records.Where(record => !AgentNames.IsNamed(record.Name)).Select(record => record.Record)];
        List<string> gains = [];

        if (records.State == SavedRead.Current)
        {
            foreach (var agent in records.Records.Where(record => AgentNames.IsNamed(record.Name)).GroupBy(record => record.Name, StringComparer.Ordinal))
            {
                var unset = EffectiveAccess.Create(agent.Key, new AgentRecords(SavedRead.Current, [], any), AccessCaps.Empty);
                var shown = EntryNameSanitizer.Sanitize(agent.Key, ApprovalPrompt.MaximumClientLength).Text;

                if (agent.Any(record => !record.Record.Understood))
                {
                    continue;
                }

                gains.AddRange(agent
                    .SelectMany(record => AccessChange.Classify(unset, record.Record.Access).Gains.Select(gain => Words(gain, record.Record.Access, zone)))
                    .Distinct(StringComparer.Ordinal)
                    .Select(words => $"{shown}: {words}"));
            }
        }

        return new AccessChangePrompt
        {
            Agent = "Every agent",
            Gains = gains.Count > 0 ? gains : ["nothing beyond the defaults: no agent's settings in the vault give more"],
            Origin = AccessOrigin.Settings,
            Reason = string.Empty,
            ReasonWasTruncated = false,
            ReasonWasAltered = false,
            ResetsCeilings = true,
        };
    }

    /// <summary>Where the request came from, as the prompt says it.</summary>
    public string OriginText => Origin switch
    {
        AccessOrigin.Agent => $"asked by {Agent} through its keypaste tool",
        AccessOrigin.Prompt => "asked from the approval prompt's Always for",
        AccessOrigin.Settings => "asked from this app's agent settings",
        _ => "asked from a terminal; any program you run could have typed it",
    };

    /// <summary>Builds the prompt for one change, sanitizing everything untrusted on the way in.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="change">The change, classified against what applies now.</param>
    /// <param name="origin">Where the request says it came from.</param>
    /// <param name="reason">The requester's own reason, verbatim, or empty.</param>
    /// <param name="zone">This computer's time zone, which an end to Automatic is shown in.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    public static AccessChangePrompt For(string agent, AccessChange change, AccessOrigin origin, string reason, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(zone);

        // None given is none shown: the sanitizer's placeholder for an empty name would read as a reason.
        var shownReason = reason.Length == 0
            ? new SanitizedName(string.Empty, WasAltered: false)
            : EntryNameSanitizer.SanitizeProse(reason, ApprovalPrompt.MaximumReasonLength);

        return new AccessChangePrompt
        {
            Agent = EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text,
            Gains = [.. change.Gains.Select(gain => Words(gain, change.Proposed, zone))],
            Origin = origin,
            Reason = shownReason.Text,
            ReasonWasTruncated = reason.Length > ApprovalPrompt.MaximumReasonLength,
            ReasonWasAltered = shownReason.WasAltered,
        };
    }

    /// <summary>A mode as a person reads it.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Its words.</returns>
    public static string Words(AgentMode mode) => mode switch
    {
        AgentMode.Ask => "Ask every time",
        AgentMode.Automatic => "Automatic: released without asking, recorded in history",
        _ => "Ask, allow for a while",
    };

    /// <summary>A limit on Automatic as a person reads it, its end on this computer's clock.</summary>
    /// <param name="limit">The limit.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <returns>Its words, or <c>no limit</c>.</returns>
    public static string Words(AccessLimit limit, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(limit);
        ArgumentNullException.ThrowIfNull(zone);

        List<string> parts = [];

        if (limit.Until is { } until)
        {
            parts.Add($"until {UntilTime.Shown(until, zone)}");
        }

        if (limit.PerHour is { } perHour)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"at most {perHour} an hour"));
        }

        return parts.Count == 0 ? "no limit" : string.Join(", ", parts);
    }

    /// <summary>One thing a change gives an agent, as a person reads it.</summary>
    /// <param name="gain">The gain.</param>
    /// <param name="proposed">The settings the change would leave the agent with.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <returns>Its words, naming its project or environment, and the limit Automatic would have.</returns>
    public static string Words(AccessGain gain, AgentAccess proposed, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(gain);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(zone);

        var where = gain.Scope is { } scope ? DisplayTextSanitizer.Escaped(scope.Name) : "by default";
        var decided = proposed.Decide(gain.Scope?.Project, gain.Scope?.Environment);

        return gain.Dimension switch
        {
            AccessDimension.See => $"sees {string.Join(", ", proposed.See.Globs.Select(DisplayTextSanitizer.Escaped))}",
            AccessDimension.Values => "is given values, not only into commands it runs",
            AccessDimension.Run => "may run commands, each still asked about",
            AccessDimension.Mode when decided.Mode == AgentMode.Automatic && !decided.Limit.IsNone =>
                $"{where}: {Automatic(decided.Limit, zone)}: released without asking, recorded in history",
            AccessDimension.Mode => $"{where}: {Words(decided.Mode)}",
            _ => $"{where}: {Automatic(decided.Limit, zone)}",
        };
    }

    /// <summary>Automatic with its limit, such as <c>Automatic until 2026-12-31 23:59 +02:00, at most 20 an hour</c>.</summary>
    private static string Automatic(AccessLimit limit, TimeZoneInfo zone) => limit switch
    {
        { IsNone: true } => "Automatic with no limit",
        { Until: not null } => $"Automatic {Words(limit, zone)}",
        _ => $"Automatic, {Words(limit, zone)}",
    };
}
