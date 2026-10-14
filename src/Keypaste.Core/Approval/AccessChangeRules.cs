using System.Globalization;
using Keypaste.Core.Agents;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>What an agent's own request to change its settings must satisfy before anything is decided.</summary>
/// <remarks>
/// The bridge applies it so it can name the argument to the agent, and the owner applies it again to
/// every request that says an agent asked, as <see cref="CredentialRequestRules"/> is applied (D-0324).
/// Each rule completes a sentence that begins with the argument and repeats nothing that was sent.
/// </remarks>
public static class AccessChangeRules
{
    /// <summary>The rule for <c>mode</c>.</summary>
    public const string ModeRule = "must be ask, allow-for-a-while or automatic";

    /// <summary>The rule for <c>project</c>.</summary>
    public const string ProjectRule = "must be <project> or <project>:<environment>, as an item's project tag names it after env:";

    /// <summary>The rule for <c>values</c>.</summary>
    public const string ValuesRule = "must be yes or no";

    /// <summary>The rule for <c>run</c>.</summary>
    public const string RunRule = "must be on or off";

    /// <summary>The rule for <c>until</c>, which the bridge sends on in UTC, as <see cref="UntilTime.Recorded"/> writes it.</summary>
    public const string UntilRule =
        "must be a time still to come: a date such as 2026-12-31, which ends at the close of that day on this computer, or a time such as 2026-12-31T18:00:00+02:00, on this computer's clock when it has no offset";

    /// <summary>The rule for <c>reset</c>, which the tool never sends: only the person resets an agent's settings.</summary>
    public const string ResetRule = "is not one an agent may give: only its person resets an agent's settings";

    /// <summary>The rule for a <c>project</c> given with what applies to the whole agent.</summary>
    public const string ProjectRowRule = "names a row that holds only a mode and its limits, so it cannot be given with see, values or run";

    /// <summary>The rule for <c>see</c>.</summary>
    public static readonly string SeeRule = string.Create(
        CultureInfo.InvariantCulture,
        $"must be 1 to {EntryExposure.MaximumGlobs} patterns of at most {EntryExposure.MaximumGlobLength} characters, each a place such as env/** or a project's variables such as tag:env:billing");

    /// <summary>The rule for <c>per_hour</c>.</summary>
    public static readonly string PerHourRule = string.Create(
        CultureInfo.InvariantCulture,
        $"must be a whole number from 1 to {AccessLimit.MaximumPerHour}");

    /// <summary>The rule for <c>reason</c>.</summary>
    public static readonly string ReasonRule = string.Create(
        CultureInfo.InvariantCulture,
        $"must be 1 to {CredentialRequestRules.MaximumReasonLength} characters");

    /// <summary>Finds the first argument an agent's request breaks the rules with.</summary>
    /// <param name="request">The request, in <see cref="AccessEdit"/>'s words.</param>
    /// <returns>The argument and its rule, or null when every argument it gives is well formed.</returns>
    /// <remarks>Whether it names anything to change is <see cref="AccessEdit.TryCreate"/>'s to say.</remarks>
    public static CredentialRequestProblem? Check(AccessChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Reason.Length is 0 or > CredentialRequestRules.MaximumReasonLength)
        {
            return new("reason", ReasonRule);
        }

        if (request.Reset)
        {
            return new("reset", ResetRule);
        }

        if (request.Mode is { } mode && !AgentModes.TryParse(mode, out _))
        {
            return new("mode", ModeRule);
        }

        if (request.Project is { } project && !ProjectScope.TryParseName(project, out _, out _))
        {
            return new("project", ProjectRule);
        }

        if (request.See is { } see && (see.Count == 0 || !EntryExposure.TryCreate(see, out _, out _)))
        {
            return new("see", SeeRule);
        }

        if (request.Values is not (null or "yes" or "no"))
        {
            return new("values", ValuesRule);
        }

        if (request.Run is not (null or "on" or "off"))
        {
            return new("run", RunRule);
        }

        // Bounds only: an agent cannot ask for a limit to be removed, which only its person can do.
        if (request.PerHour is { } perHour
            && !(int.TryParse(perHour, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= AccessLimit.MaximumPerHour))
        {
            return new("per_hour", PerHourRule);
        }

        if (request.Until is { } until && !UntilTime.TryParseRecorded(until, out _))
        {
            return new("until", UntilRule);
        }

        return request.Project is not null && (request.See is not null || request.Values is not null || request.Run is not null)
            ? new("project", ProjectRowRule)
            : null;
    }
}
