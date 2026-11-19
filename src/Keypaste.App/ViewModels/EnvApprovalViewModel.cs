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
        Values = RunApprovalViewModel.CountOfValues(prompt.Keys.Count);
        Named = RunApprovalViewModel.ShortCommand(prompt.Command);
        Keys = [.. prompt.Keys.Select((key, i) => new EnvApprovalKey(key, i < prompt.Entries.Count ? prompt.Entries[i] : string.Empty))];
        Entries = string.Join(", ", prompt.Entries.Distinct(StringComparer.Ordinal));
        FileLines = string.Join(Environment.NewLine, prompt.FileLines);
        Command = prompt.Command;
        Directory = prompt.Directory;
        Scrubbed = prompt.CommandWasAltered || prompt.DirectoryWasAltered
            ? "Characters that cannot be shown were scrubbed from the command or directory."
            : string.Empty;
    }

    /// <summary>The command as the question and the consequence name it, cut so it never wraps a line; the block holds all of it.</summary>
    internal string Named { get; }

    /// <summary>How many values the command gets, in words: <c>3 values</c>.</summary>
    internal string Values { get; }

    /// <inheritdoc/>
    protected override string Asks => $"Run {Command} with {Values}?";

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
    internal string Consequence => $"{Named} gets {Values} in its environment.";

    /// <summary>What the person checks before allowing, on its own line.</summary>
    internal string Warning { get; } = "keypaste can't check the command. Allow only a run you started.";
}

/// <summary>One variable of the set: its name, and the item its value lives in.</summary>
internal sealed record EnvApprovalKey(string Key, string Entry)
{
    /// <summary>What pointing at its chip says: the whole name and where its value comes from.</summary>
    internal string Tip => Entry.Length > 0 ? $"{Key} from {Entry}" : Key;
}
