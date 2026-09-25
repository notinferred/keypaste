using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One variable of a run as the prompt lists it: its name, where it comes from, and its tag.</summary>
/// <param name="Name">The variable the command sees.</param>
/// <param name="Source">The entry, and the field when it is not the password.</param>
/// <param name="Tag"><c>inject only</c>, and a warning when the name changes how programs start.</param>
internal sealed record RunVariableRow(string Name, string Source, string Tag);

/// <summary>
/// One agent's request to run a command with secrets in its environment, in front of the person at
/// this desktop, answered once. It holds names, never a value.
/// </summary>
/// <remarks>
/// The command and directory are shown whole: the request was refused before anybody was asked if
/// either held anything a prompt would have to scrub (D-0358). The timed allow covers this command
/// line on this connection, and the caption says it can change the files that line runs.
/// </remarks>
internal sealed class RunApprovalViewModel : PromptViewModel
{
    internal RunApprovalViewModel(RunPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var who = prompt.Label ?? prompt.Client;

        Title = $"{who} wants to run a command with {prompt.Variables.Count} secret{(prompt.Variables.Count == 1 ? string.Empty : "s")}";
        Subtitle = $"via MCP · {prompt.Directory} · profile {prompt.Profile}";
        Client = prompt.Label is { } label ? $"{prompt.Client} · label {label}" : prompt.Client;
        Tool = $"tool: {RunPrompt.ToolName}";
        Program = $"runs {prompt.Program}";
        Command = prompt.Command;
        Directory = prompt.Directory;
        Variables =
        [
            .. prompt.Variables.Select(variable => new RunVariableRow(
                variable.Name,
                string.Equals(variable.Field, "password", StringComparison.Ordinal) ? variable.Entry : $"{variable.Entry} · {variable.Field}",
                variable.ChangesHowProgramsStart ? "inject only · changes how programs start" : "inject only")),
        ];
        Reason = prompt.Reason;
        Scrubbed = prompt.ReasonWasAltered ? "The reason is not what the agent sent: it was scrubbed." : string.Empty;
        Attribution = prompt.ReasonWasTruncated
            ? "Cut short: the full text is hashed in the audit log. That sentence was written by the agent, not by keypaste. Treat it as a claim."
            : "That sentence was written by the agent, not by keypaste. Treat it as a claim.";
        OnceOnlyText = prompt.OnceOnly switch
        {
            _ when prompt.GrantSeconds > 0 => string.Empty,
            OnceOnly.ClientPolicy => "This client's policy is Ask every time: no timed grant is offered.",
            _ => "This profile is protected: it is asked about every time.",
        };
        TimedSeconds = prompt.GrantSeconds;
        TimedCaption = prompt.GrantSeconds > 0
            ? $"{who} can run this command line here again without asking until then, and can change the files it runs (scripts, package.json) in that time."
            : string.Empty;
    }

    /// <summary>What keypaste does with the values, and what it cannot stop.</summary>
    internal string Explainer { get; } =
        "keypaste will add these to the command's environment and remove their literal and escaped forms from its output. "
        + "The agent sees the names; the command itself can read the values, so allow only a command you would run yourself.";

    /// <summary>Who asks and how many secrets, as the design's heading says it.</summary>
    internal string Title { get; }

    internal string Subtitle { get; }

    /// <summary>What the client calls itself and its label. Never proof of anything.</summary>
    internal string Client { get; }

    internal string Tool { get; }

    /// <summary>The absolute path of the program that starts.</summary>
    internal string Program { get; }

    /// <summary>The command line exactly as it runs.</summary>
    internal string Command { get; }

    internal string Directory { get; }

    internal IReadOnlyList<RunVariableRow> Variables { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    internal string Scrubbed { get; }

    internal string Attribution { get; }

    /// <summary>Why no timed grant is offered, or empty.</summary>
    internal string OnceOnlyText { get; }

    internal string TimedCaption { get; }

    protected override int TimedSeconds { get; }
}
