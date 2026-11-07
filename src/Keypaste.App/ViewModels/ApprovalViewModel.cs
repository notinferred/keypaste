using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One credential request in front of the person at this desktop, answered once.</summary>
/// <remarks>
/// Always allow exists only when the prompt offers it, and is armed with the allows: it allows this
/// one request and then asks the person to confirm the change.
/// </remarks>
internal sealed class ApprovalViewModel : PromptViewModel
{
    internal ApprovalViewModel(ApprovalPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        OffersAlways = prompt.Always is not null;
        AlwaysAllowCommand = new RelayCommand(() => Decide(ApprovalAnswer.Always), () => IsArmed && OffersAlways && !IsAnswered);
        AlwaysCaption = prompt.Always switch
        {
            { IsProject: true } offer => $"Always allow makes {offer.Covers} Automatic for this agent. It allows this once, then asks you to confirm.",
            not null => "Always allow covers this item for this agent. It allows this once, then asks you to confirm.",
            _ when prompt.OnceOnly == OnceOnly.Unnamed => ApprovalPrompt.UnnamedText,
            _ => string.Empty,
        };

        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Entry;
        Field = prompt.Field;
        Lifetime = LifetimeText(prompt.OnceOnly);
        Reason = prompt.Reason;
        Scrubbed = ScrubbedText(prompt);
        Truncation = TruncationText(prompt);
    }

    /// <summary>Whose words a reason is, said under it whatever it says.</summary>
    internal const string AttributionText = "That sentence was written by the agent, not by keypaste. Treat it as a claim.";

    /// <summary>Why a request can be allowed only once, or empty where Always allow is offered.</summary>
    internal static string LifetimeText(OnceOnly onceOnly) => onceOnly switch
    {
        OnceOnly.None => string.Empty,
        OnceOnly.LimitReached => "once only: its Automatic limit was reached",
        OnceOnly.Unnamed => "once only: this agent has no name",
        OnceOnly.Automatic => "once only: it is Automatic for this agent",
        _ => "once only: protected environment",
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

    /// <summary>Allows this request once, then asks the person to confirm the change Always allow names.</summary>
    internal RelayCommand AlwaysAllowCommand { get; }

    protected override void OnAllowsChanged() => AlwaysAllowCommand.NotifyCanExecuteChanged();

    /// <summary>Whether the prompt offers Always allow.</summary>
    internal bool OffersAlways { get; }

    /// <summary>What Always allow covers and does, said above the buttons in a place kept whether it is offered or not.</summary>
    internal string AlwaysCaption { get; }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <inheritdoc/>
    protected override string Asks => $"{Client} wants 1 secret, {Entry}";

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>Why it can be allowed only once; empty where the buttons already say what can be chosen.</summary>
    internal string Lifetime { get; }

    /// <summary>Whether <see cref="Lifetime"/> has anything to say.</summary>
    internal bool IsOnceOnly => Lifetime.Length > 0;

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the entry or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>Whose words the reason is, said under it whatever it says.</summary>
    internal string Attribution { get; } = AttributionText;

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }
}
