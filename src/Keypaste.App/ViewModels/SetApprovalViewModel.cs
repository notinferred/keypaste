using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One field of a linked set as the card lists it: the field, and the item it is in by its title.</summary>
/// <param name="Field">The field's name.</param>
/// <param name="Item">The item's title.</param>
internal sealed record LinkedFieldRow(string Field, string Item);

/// <summary>An agent's request for a linked set, allowed once as a whole or denied.</summary>
internal sealed class SetApprovalViewModel : PromptViewModel
{
    internal SetApprovalViewModel(SetPrompt prompt)
        : base(prompt?.Identity)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Client = prompt.Client;
        Count = string.Create(CultureInfo.InvariantCulture, $"{prompt.Fields.Count}");
        Label = prompt.Label ?? "none configured";
        Fields = [.. prompt.Fields.Select(field => new LinkedFieldRow(field.Field, field.ItemTitle))];
        Entries = string.Join(", ", prompt.Fields.Select(field => $"{field.Entry} · {field.Field}"));
        Reason = prompt.Reason;
        Scrubbed = (prompt.EntryWasAltered, prompt.ReasonWasAltered) switch
        {
            (true, true) => "The items and the reason are not what the vault holds and the agent sent: both were scrubbed.",
            (true, false) => "An item is not what the vault holds: the stored name was scrubbed.",
            (false, true) => "The reason is not what the agent sent: it was scrubbed.",
            _ => string.Empty,
        };
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
    }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>How many values the set holds, as the question says it.</summary>
    internal string Count { get; }

    /// <inheritdoc/>
    protected override string Asks => $"Give {Agent} these {Count} values?";

    internal string Label { get; }

    internal string Tool { get; } = "keypaste.request_linked_set";

    /// <summary>The fields, in the order the agent named them.</summary>
    internal IReadOnlyList<LinkedFieldRow> Fields { get; }

    /// <summary>Each field's item by its group and title, for Details.</summary>
    internal string Entries { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>Whose words the quote is.</summary>
    internal string SaysLabel => ApprovalViewModel.Says(Agent, Scrubbed, Truncation);

    /// <summary>What keypaste altered or cut in the quote, or null.</summary>
    internal string? QuoteNote => ApprovalViewModel.NoteOn(Scrubbed, Truncation);

    /// <summary>The agent's words, quoted.</summary>
    internal string Quoted => ApprovalViewModel.Quote(Reason);

    internal string Scrubbed { get; }

    internal string Truncation { get; }

    /// <summary>What allowing does, in one plain line.</summary>
    internal string Consequence => $"{Agent} gets all {Count} and can pass them on.";
}
