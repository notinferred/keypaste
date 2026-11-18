using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One variable a run can read, as its chip names it.</summary>
/// <param name="Name">The variable's name.</param>
/// <param name="Source">The item and field its value comes from.</param>
/// <param name="ChangesHowProgramsStart">Whether setting it changes how programs start, such as <c>NODE_OPTIONS</c>.</param>
internal sealed record RunVariableRow(string Name, string Source, bool ChangesHowProgramsStart);

/// <summary>An agent's request to run a command with secrets in its environment, asked every time with Allow once only (THREATS.md T-35).</summary>
internal sealed class RunApprovalViewModel : PromptViewModel
{
    internal RunApprovalViewModel(RunPrompt prompt)
        : base(prompt?.Identity)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Title = $"Let {Agent} run this command?";
        Profile = prompt.Profile;
        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Program = prompt.Program;
        Command = prompt.Command;
        Directory = prompt.Directory;
        Variables =
        [
            .. prompt.Variables.Select(variable => new RunVariableRow(
                variable.Name,
                string.Equals(variable.Field, "password", StringComparison.Ordinal) ? variable.Entry : $"{variable.Entry} · {variable.Field}",
                variable.ChangesHowProgramsStart)),
        ];
        Sources = string.Join(", ", Variables.Select(variable => $"{variable.Name} from {variable.Source}"));
        StartWarning = Variables.Where(variable => variable.ChangesHowProgramsStart).Select(variable => variable.Name).ToList() switch
        {
            [] => string.Empty,
            [var one] => $"{one} changes how programs start.",
            var several => $"{string.Join(", ", several)} change how programs start.",
        };
        Reason = prompt.Reason;
        Scrubbed = prompt.ReasonWasAltered ? "The reason is not what the agent sent: it was scrubbed." : string.Empty;
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
    }

    /// <summary>The question.</summary>
    internal string Title { get; }

    /// <inheritdoc/>
    protected override string Asks => Title;

    /// <summary>The stage the values come from, such as dev or prod.</summary>
    internal string Profile { get; }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    internal string Label { get; }

    internal string Tool { get; } = RunPrompt.ToolName;

    /// <summary>The program the command starts, resolved on this computer.</summary>
    internal string Program { get; }

    internal string Command { get; }

    internal string Directory { get; }

    internal IReadOnlyList<RunVariableRow> Variables { get; }

    /// <summary>Where each variable's value comes from, for Details.</summary>
    internal string Sources { get; }

    /// <summary>Which variables change how programs start, or nothing.</summary>
    internal string StartWarning { get; }

    internal string Reason { get; }

    /// <summary>Whose words the quote is.</summary>
    internal string SaysLabel => ApprovalViewModel.Says(Agent);

    /// <summary>The agent's words, quoted.</summary>
    internal string Quoted => ApprovalViewModel.Quote(Reason);

    internal string Scrubbed { get; }

    internal string Truncation { get; }

    /// <summary>What allowing does, in one plain line.</summary>
    internal string Consequence { get; } =
        "The command can read these values; keypaste removes them from its output. Allow only a command you would run yourself.";
}
