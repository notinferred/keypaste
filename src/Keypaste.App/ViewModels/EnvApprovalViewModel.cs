using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One <c>keypaste run --session</c> request for a project's variables in front of the person at
/// this desktop, answered once. It holds names, never a value.
/// </summary>
internal sealed class EnvApprovalViewModel : PromptViewModel
{
    internal EnvApprovalViewModel(EnvReleasePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Project = prompt.Project;
        Keys = prompt.Keys.Count == 0 ? "(none)" : string.Join(Environment.NewLine, prompt.Keys);
        Command = prompt.Command;
        Directory = prompt.Directory;
        Scrubbed = prompt.CommandWasAltered || prompt.DirectoryWasAltered
            ? "Characters that cannot be shown were scrubbed from the command or directory."
            : string.Empty;
    }

    internal string Project { get; }

    /// <summary>The variable names, one per line.</summary>
    internal string Keys { get; }

    /// <summary>The command the runner says it will start. Its claim, not something keypaste checked.</summary>
    internal string Command { get; }

    internal string Directory { get; }

    /// <summary>What was scrubbed from the command or directory, or nothing.</summary>
    internal string Scrubbed { get; }
}
