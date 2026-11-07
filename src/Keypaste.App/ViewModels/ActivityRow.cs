using System.Globalization;
using Keypaste.Core.Approval;
using Keypaste.Core.Ownership;

namespace Keypaste.App.ViewModels;

/// <summary>
/// A request waiting for a person, as Agents lists it: who, what and for how long, never a value, and never the reason an agent gave, which only the prompt shows (T-2).
/// </summary>
/// <remarks>
/// Every string comes from the prompt the person was shown, which sanitized the agent's name, its
/// label and the item once, so the list says exactly what the prompt said. Read again, the same request
/// updates its row in place, so the row's buttons are not made again while it counts down.
/// </remarks>
internal sealed partial class ActivityRow : ObservableObject
{
    private ActivityRow(int number, ApprovalPrompt prompt, TimeSpan remaining, string lifetime)
        : this(number, prompt.Client, prompt.Label ?? "none configured", prompt.Entry, prompt.Field, lifetime + " " + Seconds(remaining))
    {
        Detail = $"{prompt.Entry} · {prompt.Field}";
        LeftText = Humanized(remaining);
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

    /// <summary>The row's place in its list, from 1, which is how the driver names one.</summary>
    [ObservableProperty]
    internal partial int Number { get; private set; }

    internal string Client { get; }

    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>How long is left, and of what.</summary>
    [ObservableProperty]
    internal partial string Left { get; private set; }

    /// <summary>What was asked for, as the Agents table's second line: the entry and field, or a change's words.</summary>
    internal string Detail { get; private init; } = string.Empty;

    /// <summary>A waiting request's time left as the Agents screen says it: <c>42m left</c>.</summary>
    [ObservableProperty]
    internal partial string LeftText { get; private set; } = string.Empty;

    /// <summary>What the same request is known by when it is read again: what the row names.</summary>
    internal string Key => string.Join('\n', Client, Label, Entry, Field);

    /// <summary>The client and its label on one line.</summary>
    internal string Who => $"{Client} · label {Label}";

    /// <summary>The entry and field on one line.</summary>
    internal string What => $"{Entry} · {Field}";

    /// <summary>What a waiting row says was asked for: the item and field, a run's command, or a change to an agent's settings.</summary>
    internal string Asked => Detail.Length > 0 ? Detail : What;

    /// <summary>Takes the place and time left of the same request read again.</summary>
    internal void Update(ActivityRow read)
    {
        ArgumentNullException.ThrowIfNull(read);

        Number = read.Number;
        Left = read.Left;
        LeftText = read.LeftText;
    }

    internal static ActivityRow Waiting(int number, WaitingRequest waiting) =>
        new(number, waiting.Prompt, waiting.Remaining, "answered for you in");

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
            waiting.Prompt.Requester ?? SessionAuthority.RunnerClient,
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
            { ResetsCeilings: true } => "Your reset of this computer's limits",
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

    // Rounded up, so a request with half a second left does not read as over.
    private static string Seconds(TimeSpan remaining) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds))} s");
}
