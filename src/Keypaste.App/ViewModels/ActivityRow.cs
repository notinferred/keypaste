using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.App.ViewModels;

/// <summary>
/// A request waiting for a person, or an allowance in force, as Agents lists it: who, what and for how
/// long, never a value, and never the reason an agent gave, which only the prompt shows (T-2).
/// </summary>
/// <remarks>
/// Every string comes from the prompt the person was shown, which sanitized the agent's name, its
/// label and the item once, so the list says exactly what the prompt said. Read again, the same request
/// or allowance updates its row in place, so the row's buttons are not made again while it counts down.
/// </remarks>
internal sealed class ActivityRow : ObservableObject
{
    private int _number;
    private string _left;
    private string _leftText = string.Empty;
    private double _fraction;

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
        _number = number;
        Client = client;
        Label = label;
        Entry = entry;
        Field = field;
        _left = left;
    }

    /// <summary>The row's place in its list, from 1, which is how the driver names one to revoke.</summary>
    internal int Number
    {
        get => _number;
        private set => Set(ref _number, value);
    }

    internal string Client { get; }

    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>How long is left, and of what.</summary>
    internal string Left
    {
        get => _left;
        private set => Set(ref _left, value);
    }

    /// <summary>The agent's grant to revoke, or null for a waiting request or a run's grant.</summary>
    internal GrantKey? Grant { get; }

    /// <summary>The grant's id, as <c>keypaste grants</c> shows it, or null for a waiting request.</summary>
    internal string? Id { get; private init; }

    /// <summary>What a grant releases, as the Agents table's second line: the entry and field, or a run's keys and set.</summary>
    internal string Detail { get; private init; } = string.Empty;

    /// <summary>A grant's or a waiting request's time left as the Agents screen says it: <c>42m left</c>.</summary>
    internal string LeftText
    {
        get => _leftText;
        private set => Set(ref _leftText, value);
    }

    /// <summary>The share of a grant's length still to run, from 0 to 1, for its bar.</summary>
    internal double Fraction
    {
        get => _fraction;
        private set => Set(ref _fraction, value);
    }

    /// <summary>What the same request or allowance is known by when it is read again: a grant's id, else what the row names.</summary>
    internal string Key => Id ?? string.Join('\n', Client, Label, Entry, Field);

    /// <summary>The client and its label on one line.</summary>
    internal string Who => $"{Client} · label {Label}";

    /// <summary>The entry and field on one line.</summary>
    internal string What => $"{Entry} · {Field}";

    /// <summary>What a waiting row says was asked for: the item and field, a run's command, or a change to an agent's settings.</summary>
    internal string Asked => Detail.Length > 0 ? Detail : What;

    /// <summary>What End is announced as: whose allowance, for what.</summary>
    internal string EndName => $"End {Client}'s allowance for {Detail}";

    /// <summary>Takes the place and time left of the same request or allowance read again.</summary>
    internal void Update(ActivityRow read)
    {
        ArgumentNullException.ThrowIfNull(read);

        Number = read.Number;
        Left = read.Left;
        LeftText = read.LeftText;
        Fraction = read.Fraction;
    }

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
            "answered for you in " + Seconds(waiting.Remaining))
        {
            LeftText = Humanized(waiting.Remaining),
        };

    /// <summary>A <c>keypaste run --session</c> request a person is being asked about.</summary>
    internal static ActivityRow WaitingEnv(int number, WaitingEnv waiting) =>
        new(
            number,
            waiting.Prompt.Requester ?? GrantSummary.EnvClient,
            "none configured",
            $"{waiting.Prompt.Project} · {waiting.Prompt.Profile} · {waiting.Prompt.Command}",
            "set",
            "answered for you in " + Seconds(waiting.Remaining))
        {
            LeftText = Humanized(waiting.Remaining),
        };

    /// <summary>
    /// A confirmation of a change that gives an agent more, named by where the request says it came from,
    /// as a claim: a terminal or the agent's own tool may have sent it, and only the person's own change is theirs.
    /// </summary>
    internal static ActivityRow WaitingChange(int number, WaitingChange waiting)
    {
        var prompt = waiting.Prompt;
        var what = prompt switch
        {
            { ResetsCeilings: true } => "Your reset of this computer's ceilings",
            { Origin: AccessOrigin.Terminal } => $"A change to {prompt.Agent}'s settings, asked from a terminal",
            { Origin: AccessOrigin.Agent } => $"A change to {prompt.Agent}'s settings, asked by {prompt.Agent} through its keypaste tool",
            _ => $"Your change to {prompt.Agent}'s settings",
        };

        return new(number, prompt.Agent, "none configured", what, "settings", "answered for you in " + Seconds(waiting.Remaining))
        {
            Detail = what,
            LeftText = Humanized(waiting.Remaining),
        };
    }

    /// <summary>A terminal's edit a person is being asked about, named by its command as a claim: any program can type one.</summary>
    internal static ActivityRow WaitingEdit(int number, WaitingEdit waiting)
    {
        var prompt = waiting.Prompt;
        var what = $"A change to {prompt.Item}, asked from a terminal";

        return new(number, prompt.Command, "none configured", what, "edit", "answered for you in " + Seconds(waiting.Remaining))
        {
            Detail = what,
            LeftText = Humanized(waiting.Remaining),
        };
    }

    /// <summary>The entries a run's grant releases and its set, or the command a <c>keypaste run --session</c> grant repeats.</summary>
    private static string EnvDetail(EnvGrantInForce grant)
    {
        var set = $"{grant.Project}/{grant.Profile}";

        return grant.Entries.Count == 0
            ? $"{grant.Command} · {set}"
            : $"{string.Join(", ", grant.Entries)} · {set}";
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
