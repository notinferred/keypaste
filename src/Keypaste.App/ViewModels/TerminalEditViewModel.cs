using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A terminal's edit to the held vault, in front of the person at this desktop, answered once.</summary>
internal sealed class TerminalEditViewModel : PromptViewModel
{
    /// <summary>Who the card says is asking: a terminal, which keypaste cannot check.</summary>
    internal static readonly RequesterIdentity Terminal = new("A terminal", null, false);

    internal TerminalEditViewModel(TerminalEditPrompt prompt)
        : base(Terminal)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Command = prompt.Command;
        Item = prompt.Item;
        Question = $"Let a terminal change {prompt.Item}?";
        Changes = prompt.Changes;
        DeletesForGood = prompt.DeletesForGood;
    }

    internal string Command { get; }

    internal string Item { get; }

    /// <summary>The question, which names the item or environment once.</summary>
    internal string Question { get; }

    /// <inheritdoc/>
    protected override string Asks => $"{Question} {Command}";

    internal IReadOnlyList<string> Changes { get; }

    internal bool DeletesForGood { get; }

    internal string AllowLabel => DeletesForGood ? "Delete for good" : "Allow";

    /// <summary>The key that presses the allow button, which a delete for good has none of.</summary>
    internal string AllowKey => DeletesForGood ? string.Empty : "↩";

    /// <inheritdoc/>
    internal override bool AnswersReturn => !DeletesForGood;

    /// <summary>Where the request came from, which never proves the person asked, and what to check, on one line.</summary>
    internal string Warning { get; } = "Any program you run could have sent this. Allow only a change you made.";
}
