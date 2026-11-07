using Keypaste.Core.Agents;

namespace Keypaste.Core.Approval;

/// <summary>What Always allow on a prompt would ask the person to confirm.</summary>
/// <param name="Change">The change, which takes effect only once they confirm it.</param>
/// <param name="Covers">What it covers, for the prompt to say: a project environment, an item as shown or a command line.</param>
public sealed record AlwaysOffer(AccessEdit Change, string Covers)
{
    /// <summary>Whether it widens to a whole project environment rather than this one item or command.</summary>
    public bool IsProject => Change.Project is not null;
}
