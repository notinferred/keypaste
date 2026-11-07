using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person who chose Always allow is told when the change that followed did not take effect; the
/// release they allowed stands, once.
/// </summary>
/// <param name="Agent">The agent's name, sanitized.</param>
/// <param name="Offer">What Always allow asked for.</param>
/// <param name="Why">keypaste's own words for why not, sanitized.</param>
public sealed record AlwaysNotice(string Agent, AlwaysOffer Offer, string Why)
{
    /// <summary>What did not happen, said after "Allowed once.".</summary>
    public string Unmade => Offer.IsProject
        ? $"{Offer.Covers} was not made Automatic for {Agent}: {Why}."
        : $"{Offer.Covers} was not always allowed for {Agent}: {Why}.";

    /// <summary>The notice for the change Always allow asked for, or null when there is nothing to tell.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="offer">What Always allow asked for.</param>
    /// <param name="change">What became of the change.</param>
    /// <returns>
    /// Null when the change applies, when the person refused it, since they know, and when it was
    /// withdrawn, since a lock or the agent hanging up withdraws the release with it.
    /// </returns>
    public static AlwaysNotice? For(string agent, AlwaysOffer offer, AccessChangeReply change)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(change);

        if (change.Unapplied is not { } why
            || change is { Decision: AuditDecision.Denied, Method: AuditMethod.Prompt or AuditMethod.Cancelled or AuditMethod.VaultLocked })
        {
            return null;
        }

        return new AlwaysNotice(
            EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text,
            offer,
            DisplayTextSanitizer.Sanitize(why).Text);
    }
}
