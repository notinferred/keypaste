using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One field of a linked set as the prompt lists it: its item and its field.</summary>
/// <param name="Entry">The item, as every prompt shows it.</param>
/// <param name="Field">The field.</param>
internal sealed record LinkedFieldRow(string Entry, string Field);

/// <summary>
/// An agent's linked set in front of the person at this desktop, allowed once or denied as a whole. It holds names, never a value.
/// </summary>
/// <remarks>A set offers no always answer, so nothing is kept for it (PRODUCT §2).</remarks>
internal sealed class SetApprovalViewModel : PromptViewModel
{
    internal SetApprovalViewModel(SetPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Client = prompt.Client;
        Wants = string.Create(CultureInfo.InvariantCulture, $" wants {prompt.Fields.Count} secrets together");
        Title = prompt.Client + Wants;
        Label = prompt.Label ?? "none configured";
        Fields = [.. prompt.Fields.Select(field => new LinkedFieldRow(field.Entry, field.Field))];
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

    /// <summary>Who asks and how many secrets, as the design's heading says it.</summary>
    internal string Title { get; }

    /// <inheritdoc/>
    protected override string Asks => Title;

    /// <summary>What the heading says after the client, drawn apart so a long client name is trimmed and this is not.</summary>
    internal string Wants { get; }

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    internal string Tool { get; } = "tool: keypaste.request_linked_set";

    /// <summary>Each item and field, in the order the agent asked.</summary>
    internal IReadOnlyList<LinkedFieldRow> Fields { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the items or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>Whose words the reason is, said under it whatever it says.</summary>
    internal string Attribution { get; } = ApprovalViewModel.AttributionText;

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }

    /// <summary>What allowing does, and that it is the whole set or nothing.</summary>
    internal string Explainer { get; } =
        "Allowing returns every value above to the agent at once, which can read them and pass them on. "
        + "Deny refuses them all. Allow only a request you expected.";
}
