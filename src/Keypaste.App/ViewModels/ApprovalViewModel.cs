using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One credential request in front of the person at this desktop, answered once.</summary>
internal sealed class ApprovalViewModel : PromptViewModel
{
    private readonly bool _offersAlways;
    private readonly string? _agent;

    internal ApprovalViewModel(ApprovalPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _offersAlways = prompt.OffersAlways;
        _agent = prompt.Label;
        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Entry;
        Field = prompt.Field;
        Reason = prompt.Reason;
        Scrubbed = ScrubbedText(prompt);
        Truncation = TruncationText(prompt);
    }

    /// <summary>Whose words a reason is, said under it whatever it says.</summary>
    internal const string AttributionText = "That sentence was written by the agent, not by keypaste. Treat it as a claim.";

    /// <inheritdoc/>
    internal override bool OffersAlways => _offersAlways;

    /// <inheritdoc/>
    internal override bool OffersAlwaysBlock => _offersAlways;

    /// <inheritdoc/>
    protected override string Choices => ChoicesText(_offersAlways, _agent);

    /// <inheritdoc/>
    protected override string Confirmation(ApprovalAnswer always) => always == ApprovalAnswer.AlwaysBlocked
        ? $"Always refuse {_agent} this item's {Field} without asking you? Remove it on its page in Agents to be asked again."
        : $"Always give {_agent} this item's {Field} without asking you? Remove it on its page in Agents to be asked again.";

    /// <summary>What is said above a prompt's choices: what an always keeps, or why none is offered.</summary>
    internal static string ChoicesText(bool offersAlways, string? agent) => (offersAlways, agent) switch
    {
        (true, _) => $"An always answer is kept for {agent} on this computer until you remove it.",
        (false, null) => "This agent's bridge has no --client-label, so keypaste can't remember an answer for it.",
        _ => $"keypaste keeps at most {Keypaste.Core.Agents.AgentRules.MaximumRemembered} always answers. Remove some on an agent's page to add another.",
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

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>The tool the request came through, an attachment's or a field's.</summary>
    internal string Tool => Field.StartsWith(CredentialFields.Attachment + " ", StringComparison.Ordinal)
        ? "tool: keypaste.request_attachment"
        : "tool: keypaste.request_credential";

    /// <inheritdoc/>
    protected override string Asks => $"{Client} wants 1 secret, {Entry}";

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the entry or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>Whose words the reason is, said under it whatever it says.</summary>
    internal string Attribution { get; } = AttributionText;

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }
}
