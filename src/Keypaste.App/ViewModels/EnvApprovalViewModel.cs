using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A runner's request for a project's env set, in front of the person at this desktop, answered once.</summary>
internal sealed class EnvApprovalViewModel : PromptViewModel
{
    internal EnvApprovalViewModel(EnvReleasePrompt prompt)
        : base(prompt?.Identity)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Project = prompt.Project;
        Profile = prompt.Profile;
        AskedBy = prompt.Requester ?? string.Empty;
        Title = $"Start this command with {prompt.Keys.Count} secret{(prompt.Keys.Count == 1 ? string.Empty : "s")}?";
        Keys = [.. prompt.Keys.Select((key, i) => new EnvApprovalKey(key, i < prompt.Entries.Count ? prompt.Entries[i] : string.Empty))];
        Entries = string.Join(", ", prompt.Entries.Distinct(StringComparer.Ordinal));
        FileLines = string.Join(Environment.NewLine, prompt.FileLines);
        Command = prompt.Command;
        Directory = prompt.Directory;
        Scrubbed = prompt.CommandWasAltered || prompt.DirectoryWasAltered
            ? "Characters that cannot be shown were scrubbed from the command or directory."
            : string.Empty;
    }

    /// <summary>The question, which names how many values the command gets.</summary>
    internal string Title { get; }

    /// <inheritdoc/>
    protected override string Asks => Title;

    internal string Project { get; }

    /// <summary>The stage the set belongs to, such as dev or prod.</summary>
    internal string Profile { get; }

    /// <summary>Who asks when it is not the person's own run, such as a scoped token; empty otherwise.</summary>
    internal string AskedBy { get; }

    internal IReadOnlyList<EnvApprovalKey> Keys { get; }

    internal bool HasNoKeys => Keys.Count == 0;

    /// <summary>The items the values come from, for Details.</summary>
    internal string Entries { get; }

    internal string FileLines { get; }

    internal string Command { get; }

    internal string Directory { get; }

    internal string Scrubbed { get; }

    /// <summary>What allowing does, in one plain line.</summary>
    internal string Consequence { get; } = "keypaste can't check the command. Allow only a run you started: every value goes into its environment.";
}

/// <summary>One variable of the set: its name, and the item its value lives in.</summary>
internal sealed record EnvApprovalKey(string Key, string Entry);
