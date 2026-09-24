using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One credential request in front of the person at this desktop, answered once.</summary>
internal sealed class ApprovalViewModel : PromptViewModel
{
    internal ApprovalViewModel(ApprovalPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Entry;
        Field = prompt.Field;
        Lifetime = string.Create(CultureInfo.InvariantCulture, $"{prompt.TtlSeconds} seconds");
        Reason = prompt.Reason;
        Scrubbed = (prompt.EntryWasAltered, prompt.ReasonWasAltered) switch
        {
            (true, true) => "The entry and the reason are not what the vault holds and the agent sent: both were scrubbed.",
            (true, false) => "The entry is not what the vault holds: the stored name was scrubbed.",
            (false, true) => "The reason is not what the agent sent: it was scrubbed.",
            _ => string.Empty,
        };
        Attribution = prompt.ReasonWasTruncated
            ? "Cut short: the full text is hashed in the audit log. That sentence was written by the agent, not by keypaste. Treat it as a claim."
            : "That sentence was written by the agent, not by keypaste. Treat it as a claim.";
    }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>How long a grant would live: the lifetime that will apply, not the one asked for.</summary>
    internal string Lifetime { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the entry or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    internal string Attribution { get; }
}
