using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>An agent's request to change an item, in front of the person at this desktop, allowed once (THREATS.md T-57).</summary>
/// <remarks>
/// Allow is armed a moment after the prompt is up, as every allow is, and is the only answer that changes anything; it offers
/// no always, since no rule allows a change. The change is keypaste's own words; the reason is the agent's claim.
/// </remarks>
internal sealed class ItemChangeViewModel : PromptViewModel
{
    internal ItemChangeViewModel(ItemChangePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Item = prompt.Item;
        Change = prompt.Says;
        DeletesForGood = prompt.DeletesForGood;
        Reason = prompt.Reason;
        Scrubbed = prompt.WasAltered ? "The item or the reason is not what the agent sent: it was scrubbed." : string.Empty;
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
        Explainer = prompt.Change switch
        {
            ItemChangeKind.Delete => "Allowing deletes it for good: Trash will not hold it, and nothing can bring it back.",
            ItemChangeKind.MoveToTrash => "Allowing moves it to Trash now, where you can restore it.",
            _ => "Allowing saves this in the vault now; the item's history keeps how it was.",
        };
    }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    /// <summary>The item, sanitized.</summary>
    internal string Item { get; }

    /// <summary>The change, in keypaste's words.</summary>
    internal string Change { get; }

    /// <summary>Whether allowing deletes the item for good, which draws Allow as danger.</summary>
    internal bool DeletesForGood { get; }

    internal string AllowLabel => DeletesForGood ? "Delete for good" : "Allow";

    /// <summary>What allowing does.</summary>
    internal string Explainer { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }

    /// <summary>Whose words the reason is, said under it whatever it says.</summary>
    internal string Attribution { get; } = ApprovalViewModel.AttributionText;

    /// <inheritdoc/>
    protected override string Asks => $"{Client} asks you to change {Item}";
}
