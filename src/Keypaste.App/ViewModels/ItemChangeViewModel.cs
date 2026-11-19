using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>An agent's request to change an item, asked every time and answered once.</summary>
internal sealed class ItemChangeViewModel : PromptViewModel
{
    private readonly string _itemTitle;

    internal ItemChangeViewModel(ItemChangePrompt prompt)
        : base(prompt?.Identity)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _itemTitle = prompt.ItemTitle;
        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Item;
        Change = prompt.Says;
        DeletesForGood = prompt.DeletesForGood;
        Question = prompt.Change switch
        {
            ItemChangeKind.MoveToTrash => $"Let {Agent} move {_itemTitle} to Trash?",
            ItemChangeKind.MarkExpired when prompt.Expires is { } day =>
                $"Let {Agent} mark {_itemTitle} as expired on {day.ToString(ItemChangeRules.DayFormat, CultureInfo.InvariantCulture)}?",
            ItemChangeKind.MarkExpired => $"Let {Agent} mark {_itemTitle} as expired now?",
            _ => $"Let {Agent} delete {_itemTitle} for good?",
        };
        Reason = prompt.Reason;
        Scrubbed = prompt.WasAltered ? "The item or the reason is not what the agent sent: it was scrubbed." : string.Empty;
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
        Explainer = prompt.Change switch
        {
            ItemChangeKind.Delete when prompt.Created is { } created =>
                $"An agent created it on {created.ToString(ItemChangeRules.DayFormat, CultureInfo.InvariantCulture)}. Trash will not hold it, and nothing can bring it back.",
            ItemChangeKind.Delete => "An agent created it. Trash will not hold it, and nothing can bring it back.",
            ItemChangeKind.MoveToTrash => "It moves to Trash now, where you can restore it.",
            _ => "keypaste saves this in the vault now; the item's history keeps how it was.",
        };
    }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    internal string Label { get; }

    /// <summary>The item's group and title, shown under Details.</summary>
    internal string Entry { get; }

    /// <summary>The item as the card names it: its title and its vault.</summary>
    internal string Item => InVault(_itemTitle);

    /// <summary>The change in Core's words, shown under Details.</summary>
    internal string Change { get; }

    /// <summary>The question: what the agent would do to the item.</summary>
    internal string Question { get; }

    /// <summary>Whether allowing deletes an item for good, which the allow button says and Return never presses.</summary>
    internal bool DeletesForGood { get; }

    internal string AllowLabel => DeletesForGood ? "Delete for good" : "Allow";

    /// <summary>The key that presses the allow button, which a delete for good has none of.</summary>
    internal string AllowKey => DeletesForGood ? string.Empty : "↩";

    /// <inheritdoc/>
    internal override bool AnswersReturn => !DeletesForGood;

    /// <summary>What allowing does, in one plain line.</summary>
    internal string Explainer { get; }

    internal string Reason { get; }

    /// <summary>Whose words the quote is.</summary>
    internal string SaysLabel => ApprovalViewModel.Says(Agent, Scrubbed, Truncation);

    /// <summary>What keypaste altered or cut in the quote, or null.</summary>
    internal string? QuoteNote => ApprovalViewModel.NoteOn(Scrubbed, Truncation);

    /// <summary>The agent's words, quoted.</summary>
    internal string Quoted => ApprovalViewModel.Quote(Reason);

    internal string Scrubbed { get; }

    internal string Truncation { get; }

    internal string Tool { get; } = "keypaste.request_item_change";

    /// <inheritdoc/>
    protected override string Asks => Question;
}
