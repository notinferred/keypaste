using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A change that would give an agent more, in front of the person at this desktop, confirmed once.</summary>
/// <remarks>
/// Allow is armed after the prompt has been up a moment, as every allow is, and is the only answer that
/// changes anything; it offers no timed grant. Where the change says it came from is a claim, and the
/// requester's reason is its own words, shown as such. A change with no reason, which is every change
/// the person makes in this app, says so in the same box, under no caption and with no line about
/// whose words it holds, so the window is the same size either way.
/// </remarks>
internal sealed class AccessChangeViewModel : PromptViewModel
{
    internal AccessChangeViewModel(AccessChangePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Agent = prompt.Agent;
        Subject = prompt.ResetsCeilings ? "this computer's ceilings, which keypaste cannot read" : "a change to its settings in this vault";
        Origin = prompt.OriginText;
        GainsCaption = prompt.ResetsCeilings ? "Beyond the defaults, each would get" : "It would gain";
        Gains = prompt.Gains;
        var given = prompt.Reason.Length > 0;
        ReasonCaption = given ? "The request says it is needed because" : string.Empty;
        Reason = given ? prompt.Reason : prompt switch
        {
            { ResetsCeilings: true } => "You chose Reset on this app's Agents screen.",
            { Origin: AccessOrigin.Settings } => "You chose this in this app's agent settings.",
            { Origin: AccessOrigin.Prompt } => "You chose this with Always for in the approval prompt.",
            _ => "No reason was given.",
        };
        Attribution = given ? "That sentence was not written by keypaste. Treat it as a claim." : string.Empty;
        Scrubbed = prompt.ReasonWasAltered ? "The reason is not what was sent: it was scrubbed." : string.Empty;
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
        Explainer = prompt switch
        {
            { ResetsCeilings: true } => $"Allowing {AccessChangePrompt.ResetText}. Allow only a change you expected.",
            { StaysBeneathDefaults: true } => $"Allowing saves this in the vault, but {AccessChangePrompt.BeneathDefaultsText}. Allow only a change you expected.",
            _ => "Allowing saves this in the vault, and the agent gets it from its next request. Allow only a change you expected.",
        };
    }

    /// <summary>The agent the change is for, sanitized, or every agent for a reset.</summary>
    internal string Agent { get; }

    /// <summary>What is being changed: the agent's settings, or this computer's ceilings for a reset.</summary>
    internal string Subject { get; }

    /// <summary>What the gains box is.</summary>
    internal string GainsCaption { get; }

    /// <summary>Where the request says it came from, said as a claim.</summary>
    internal string Origin { get; }

    /// <summary>Each thing the agent would gain, in keypaste's own words.</summary>
    internal IReadOnlyList<string> Gains { get; }

    /// <summary>What the reason box is, or empty when the request gave no reason.</summary>
    internal string ReasonCaption { get; }

    /// <summary>The requester's words, already sanitized and capped, or where the person chose it, or that it gave none.</summary>
    internal string Reason { get; }

    internal string Scrubbed { get; }

    /// <summary>Whose words the reason is, said under it whatever it says; empty when there is none.</summary>
    internal string Attribution { get; }

    internal string Truncation { get; }

    /// <summary>What allowing does, and when the agent gets it.</summary>
    internal string Explainer { get; }

    protected override int TimedSeconds => 0;
}
