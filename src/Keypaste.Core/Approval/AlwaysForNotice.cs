using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person who chose Always for is told when the change that followed did not leave the agent
/// Automatic for that environment on this computer; the release they allowed stands, once.
/// </summary>
/// <param name="Agent">The agent's name, sanitized.</param>
/// <param name="Scope">The project environment Always for named, which its grammar keeps plain.</param>
/// <param name="Why">keypaste's own words for why not, sanitized.</param>
public sealed record AlwaysForNotice(string Agent, string Scope, string Why)
{
    /// <summary>What did not happen, said after "Allowed once.".</summary>
    public string Unmade => $"{Scope} was not made Automatic for {Agent}: {Why}.";

    /// <summary>The notice for the change Always for asked for, or null when there is nothing to tell.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="scope">The project environment.</param>
    /// <param name="change">What became of the change.</param>
    /// <returns>
    /// Null when the change applies, when the person refused it, since they know, and when it was
    /// withdrawn, since a lock or the agent hanging up withdraws the release with it.
    /// </returns>
    public static AlwaysForNotice? For(string agent, ProjectScope scope, AccessChangeReply change)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(change);

        if (change.Unapplied is not { } why
            || change is { Decision: AuditDecision.Denied, Method: AuditMethod.Prompt or AuditMethod.Cancelled or AuditMethod.VaultLocked })
        {
            return null;
        }

        return new AlwaysForNotice(
            EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text,
            scope.Name,
            DisplayTextSanitizer.Sanitize(why).Text);
    }
}
