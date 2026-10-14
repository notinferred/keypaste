using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One credential request in front of the person at this desktop, answered once.</summary>
/// <remarks>
/// Always for exists only when the prompt offers it, and is armed with the allows: it allows this one
/// request and then asks the person to confirm making that project environment Automatic.
/// </remarks>
internal sealed class ApprovalViewModel : PromptViewModel
{
    internal ApprovalViewModel(ApprovalPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        OffersAlways = prompt.AlwaysFor is not null;
        AlwaysLabel = prompt.AlwaysFor is { } scope ? $"Always for {scope.Name}" : string.Empty;
        AlwaysForCommand = new RelayCommand(() => Decide(ApprovalAnswer.AlwaysForProject), () => IsArmed && OffersAlways && !IsAnswered);
        DependsOn(AlwaysForCommand, nameof(IsArmed), nameof(IsAnswered));

        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Entry;
        Field = prompt.Field;
        TimedSeconds = prompt.TtlSeconds;
        Lifetime = prompt.TtlSeconds > 0
            ? $"once, or for {ApprovalLimits.Describe(prompt.TtlSeconds)}"
            : prompt.OnceOnly == OnceOnly.AskEveryTime ? "once only: this agent is set to Ask every time" : "once only: protected environment";
        Reason = prompt.Reason;
        Scrubbed = (prompt.EntryWasAltered, prompt.ReasonWasAltered) switch
        {
            (true, true) => "The item and the reason are not what the vault holds and the agent sent: both were scrubbed.",
            (true, false) => "The item is not what the vault holds: the stored name was scrubbed.",
            (false, true) => "The reason is not what the agent sent: it was scrubbed.",
            _ => string.Empty,
        };
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
    }

    /// <summary>Allows this request once, then asks the person to make its project environment Automatic for the agent.</summary>
    internal RelayCommand AlwaysForCommand { get; }

    /// <summary>Whether the prompt offers Always for, which then has its own place on it.</summary>
    internal bool OffersAlways { get; }

    /// <summary>What the Always for button says, naming the project environment.</summary>
    internal string AlwaysLabel { get; }

    /// <summary>What Always for does, said beside it.</summary>
    internal string AlwaysCaption { get; } = "Allows once, then asks to make it Automatic";

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    internal string Entry { get; }

    internal string Field { get; }

    /// <summary>What the person can allow: once, or once and the timed grant, never the lifetime the agent asked for.</summary>
    internal string Lifetime { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the entry or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>Whose words the reason is, said under it whatever it says.</summary>
    internal string Attribution { get; } = "That sentence was written by the agent, not by keypaste. Treat it as a claim.";

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }

    protected override int TimedSeconds { get; }
}
