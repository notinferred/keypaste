using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One <c>keypaste run --session</c> request for a project's variables in front of the person at
/// this desktop, answered once. It holds names, never a value.
/// </summary>
/// <remarks>The command is the runner's claim and the run names no agent to keep an always under, so it is allowed once at most (THREATS.md T-34).</remarks>
internal sealed class EnvApprovalViewModel : PromptViewModel
{
    internal EnvApprovalViewModel(EnvReleasePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Project = prompt.Project;
        Profile = prompt.Profile;
        Requester = prompt.Requester ?? string.Empty;
        Title = $"{(prompt.Requester is null ? "keypaste run" : "A run")} wants {prompt.Keys.Count} secret{(prompt.Keys.Count == 1 ? string.Empty : "s")}";
        Keys = [.. prompt.Keys.Select((key, i) => new EnvApprovalKey(key, i < prompt.Entries.Count ? prompt.Entries[i] : string.Empty))];
        FileLines = string.Join(Environment.NewLine, prompt.FileLines);
        Command = prompt.Command;
        Directory = prompt.Directory;
        Scrubbed = prompt.CommandWasAltered || prompt.DirectoryWasAltered
            ? "Characters that cannot be shown were scrubbed from the command or directory."
            : string.Empty;
    }

    /// <summary>Who asks and how many secrets, as the design's heading says it.</summary>
    internal string Title { get; }

    /// <inheritdoc/>
    protected override string Asks => Title;

    internal string Project { get; }

    internal string Profile { get; }

    /// <summary>Who is asking when it is not the person's own run, or empty.</summary>
    internal string Requester { get; }

    /// <summary>The variable names with the entry each comes from, which the prompt draws one row each with its tag.</summary>
    internal IReadOnlyList<EnvApprovalKey> Keys { get; }

    internal bool HasNoKeys => Keys.Count == 0;

    /// <summary>What a reference file makes of the set, one line each, or empty.</summary>
    internal string FileLines { get; }

    /// <summary>The command the runner says it will start. Its claim, not something keypaste checked.</summary>
    internal string Command { get; }

    internal string Directory { get; }

    /// <summary>What was scrubbed from the command or directory, or nothing.</summary>
    internal string Scrubbed { get; }
}

/// <summary>One variable in a run's prompt: its name and the entry its value lives in.</summary>
/// <param name="Key">The variable name.</param>
/// <param name="Entry">The entry, as the prompt shows it.</param>
internal sealed record EnvApprovalKey(string Key, string Entry);
