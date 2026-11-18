using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One credential request in front of the person at this desktop, answered once.</summary>
internal sealed class ApprovalViewModel : PromptViewModel
{
    private readonly bool _offersAlways;
    private readonly string? _label;
    private readonly string _itemTitle;

    internal ApprovalViewModel(ApprovalPrompt prompt)
        : base(prompt?.Identity)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _offersAlways = prompt.OffersAlways;
        _label = prompt.Label;
        _itemTitle = prompt.ItemTitle;
        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Entry;
        Field = prompt.Field;
        Protected = prompt.Protected;
        Reason = prompt.Reason;
        Scrubbed = ScrubbedText(prompt);
        Truncation = TruncationText(prompt);
    }

    /// <summary>Whose words a reason is, said above it whatever it says.</summary>
    internal static string Says(string agent) => $"{agent} says";

    /// <summary>The agent's words as the card quotes them.</summary>
    internal static string Quote(string reason) => $"“{reason}”";

    /// <inheritdoc/>
    internal override bool OffersAlways => _offersAlways;

    /// <inheritdoc/>
    internal override string AlwaysTip => $"Don't ask again for {Agent} and this key. Change in Settings › Agents.";

    /// <inheritdoc/>
    internal override string NoAlways => Protected ? string.Empty : NoAlwaysText(_offersAlways, _label);

    /// <summary>Why a prompt offers no Always allow, or nothing when it offers one; a protected item says so on its own line.</summary>
    internal static string NoAlwaysText(bool offersAlways, string? label) => (offersAlways, label) switch
    {
        (true, _) => string.Empty,
        (false, null) => "Always allow: not offered, because this agent's bridge has no --client-label, so keypaste can't remember an answer for it.",
        _ => $"Always allow: not offered, because keypaste keeps at most {Keypaste.Core.Agents.AgentRules.MaximumRemembered} always answers. Remove some on an agent's page to add another.",
    };

    /// <summary>What was scrubbed from the item or the reason, or nothing.</summary>
    internal static string ScrubbedText(ApprovalPrompt prompt) => (prompt.EntryWasAltered, prompt.ReasonWasAltered) switch
    {
        (true, true) => "The item and the reason are not what the vault holds and the agent sent: both were scrubbed.",
        (true, false) => "The item is not what the vault holds: the stored name was scrubbed.",
        (false, true) => "The reason is not what the agent sent: it was scrubbed.",
        _ => string.Empty,
    };

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal static string TruncationText(ApprovalPrompt prompt) =>
        prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;

    /// <summary>The question's first words: Give, or Always give while confirming.</summary>
    internal string Verb => IsConfirming ? "Always give " : "Give ";

    /// <summary>The item as the card names it: its title and its vault.</summary>
    internal string Item => InVault(_itemTitle);

    /// <summary>Whether the item belongs to a protected environment, asked every time.</summary>
    internal bool Protected { get; }

    /// <summary>Whose words the quote is.</summary>
    internal string SaysLabel => Says(Agent);

    /// <summary>The agent's words, quoted.</summary>
    internal string Quoted => Quote(Reason);

    /// <summary>What allowing does, in one plain line.</summary>
    internal string Consequence => $"{Agent} will receive the value and can pass it on.";

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>The tool the request came through, an attachment's or a field's.</summary>
    internal string Tool => Field.StartsWith(CredentialFields.Attachment + " ", StringComparison.Ordinal)
        ? "keypaste.request_attachment"
        : "keypaste.request_credential";

    /// <inheritdoc/>
    protected override string Asks => $"Give {Agent} {Field} from {_itemTitle}?";

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    /// <summary>The item's group and title, shown under Details.</summary>
    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the entry or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }

    /// <inheritdoc/>
    protected override void OnAllowsChanged() => OnPropertyChanged(nameof(Verb));
}
