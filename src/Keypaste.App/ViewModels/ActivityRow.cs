using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.App.ViewModels;

/// <summary>
/// A request waiting for a person, or a grant in force, as Agent Activity lists it: who, what and
/// for how long, never a value.
/// </summary>
/// <remarks>
/// Every string comes from the <see cref="ApprovalPrompt"/> the person was shown, which sanitized
/// the client's name, its label and the entry once, so the list says exactly what the prompt said.
/// </remarks>
internal sealed class ActivityRow
{
    private ActivityRow(int number, ApprovalPrompt prompt, TimeSpan remaining, string lifetime, GrantKey? grant)
        : this(number, prompt.Client, prompt.Label ?? "none configured", prompt.Entry, prompt.Field, lifetime + " " + Seconds(remaining))
    {
        Grant = grant;
        Id = grant is { } key ? GrantId.Of(key) : null;
        Detail = $"{prompt.Entry} · {prompt.Field}";
        LeftText = Humanized(remaining);
        Fraction = Share(remaining, prompt.TtlSeconds > 0 ? prompt.TtlSeconds : ApprovalLimits.DefaultMaximumTtlSeconds);
    }

    private ActivityRow(int number, string client, string label, string entry, string field, string left)
    {
        Number = number;
        Client = client;
        Label = label;
        Entry = entry;
        Field = field;
        Left = left;
    }

    /// <summary>The row's place in its list, from 1, which is how the driver names one to revoke.</summary>
    internal int Number { get; }

    internal string Client { get; }

    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>How long is left, and of what.</summary>
    internal string Left { get; }

    /// <summary>The agent's grant to revoke, or null for a waiting request or a run's grant.</summary>
    internal GrantKey? Grant { get; }

    /// <summary>The grant's id, as <c>keypaste grants</c> shows it, or null for a waiting request.</summary>
    internal string? Id { get; private init; }

    /// <summary>What a grant releases, as the Agents table's second line: the entry and field, or a run's keys and set.</summary>
    internal string Detail { get; private init; } = string.Empty;

    /// <summary>A grant's time left as the Agents table says it: <c>42m left</c>.</summary>
    internal string LeftText { get; private init; } = string.Empty;

    /// <summary>The share of a grant's length still to run, from 0 to 1, for its bar.</summary>
    internal double Fraction { get; private init; }

    /// <summary>The client and its label on one line.</summary>
    internal string Who => $"{Client} · label {Label}";

    /// <summary>The entry and field on one line.</summary>
    internal string What => $"{Entry} · {Field}";

    internal static ActivityRow Waiting(int number, WaitingRequest waiting) =>
        new(number, waiting.Prompt, waiting.Remaining, "answered for you in", grant: null);

    internal static ActivityRow Granted(int number, GrantInForce grant) =>
        new(number, grant.Approved, grant.Remaining, "ends in", grant.Key);

    /// <summary>A timed grant a person gave a repeated <c>keypaste run --session</c> or an agent's run; its names were sanitized for the prompt that gave it.</summary>
    internal static ActivityRow EnvGranted(int number, EnvGrantInForce grant) =>
        new(
            number,
            grant.Client ?? GrantSummary.EnvClient,
            grant.Label is { } label ? EntryNameSanitizer.Sanitize(label, ApprovalPrompt.MaximumClientLength).Text : "none configured",
            $"{grant.Project} · {grant.Profile} · {grant.Command}",
            grant.Client is null ? "set" : "run",
            "ends in " + Seconds(grant.Remaining))
        {
            Id = GrantId.OfEnv(grant.Key),
            Detail = EnvDetail(grant),
            LeftText = Humanized(grant.Remaining),
            Fraction = Share(grant.Remaining, EnvGrantCache.GrantSeconds(ApprovalLimits.Default)),
        };

    /// <summary>An agent's run a person is being asked about.</summary>
    internal static ActivityRow WaitingRun(int number, WaitingRun waiting) =>
        new(
            number,
            waiting.Prompt.Client,
            waiting.Prompt.Label ?? "none configured",
            $"{waiting.Prompt.Command} · {string.Join(", ", waiting.Prompt.Variables.Select(variable => variable.Name))}",
            "run",
            "answered for you in " + Seconds(waiting.Remaining));

    /// <summary>A <c>keypaste run --session</c> request a person is being asked about.</summary>
    internal static ActivityRow WaitingEnv(int number, WaitingEnv waiting) =>
        new(
            number,
            waiting.Prompt.Requester ?? GrantSummary.EnvClient,
            "none configured",
            $"{waiting.Prompt.Project} · {waiting.Prompt.Profile} · {waiting.Prompt.Command}",
            "set",
            "answered for you in " + Seconds(waiting.Remaining));

    /// <summary>The keys a run's grant releases and its set, or the command a <c>keypaste run --session</c> grant repeats.</summary>
    private static string EnvDetail(EnvGrantInForce grant)
    {
        var set = $"{grant.Project}/{grant.Profile}";

        return grant.Entries.Count == 0
            ? $"{grant.Command} · {set}"
            : $"{string.Join(", ", grant.Entries.Select(entry => entry[(entry.LastIndexOf('/') + 1)..]))} · {set}";
    }

    /// <summary>Whole minutes under an hour, then hours and minutes; rounded up, as <see cref="Seconds"/> is.</summary>
    private static string Humanized(TimeSpan remaining)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        var minutes = (seconds + 59) / 60;

        return seconds < 60 ? string.Create(CultureInfo.InvariantCulture, $"{seconds}s left")
            : minutes < 60 ? string.Create(CultureInfo.InvariantCulture, $"{minutes}m left")
            : minutes % 60 == 0 ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h left")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h {minutes % 60}m left");
    }

    private static double Share(TimeSpan remaining, int lengthSeconds) =>
        lengthSeconds <= 0 ? 0 : Math.Clamp(remaining.TotalSeconds / lengthSeconds, 0, 1);

    // Rounded up, so a grant with half a second left does not read as over.
    private static string Seconds(TimeSpan remaining) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds))} s");
}
