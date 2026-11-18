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
        Origin = prompt.OriginText;
        Changes = prompt.Changes;
        DeletesForGood = prompt.DeletesForGood;
    }

    internal string Command { get; }

    internal string Item { get; }

    /// <summary>The question.</summary>
    internal string Question { get; } = "Allow this change from a terminal?";

    /// <inheritdoc/>
    protected override string Asks => $"{Question} {Item}, {Command}";

    internal string Origin { get; }

    internal IReadOnlyList<string> Changes { get; }

    internal bool DeletesForGood { get; }

    internal string AllowLabel => DeletesForGood ? "Delete for good" : "Allow";

    /// <summary>The key that presses the allow button, which a delete for good has none of.</summary>
    internal string AllowKey => DeletesForGood ? string.Empty : "↩";

    /// <inheritdoc/>
    internal override bool AnswersReturn => !DeletesForGood;

    internal string Explainer { get; } = "Allowing saves this in the vault now. Allow only a change you made yourself.";
}
