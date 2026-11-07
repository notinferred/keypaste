namespace Keypaste.Core.Approval;

/// <summary>One of the items an agent's request names, as the vault holds it, for the person to pick from when more than one does.</summary>
/// <param name="Name">The item, carrying its UUID, so a read reaches it alone.</param>
/// <param name="Username">Its username, verbatim; shown to the person, never sent to the agent.</param>
/// <param name="Url">Its URL, verbatim; shown to the person, never sent to the agent.</param>
public sealed record AnsweringItem(EntryName Name, string Username, string Url);

/// <summary>One item on a picker prompt, already sanitized, with what Always allow would cover if the person picks it.</summary>
public sealed record ItemCandidate
{
    /// <summary>The item, carrying its UUID; not for display, since it is not sanitized.</summary>
    public required EntryName Name { get; init; }

    /// <summary>Its UUID as hex, which its audit line names.</summary>
    public required string Item { get; init; }

    /// <summary>Its title, sanitized.</summary>
    public required string Title { get; init; }

    /// <summary>Its group, sanitized, or empty at the top level.</summary>
    public required string Group { get; init; }

    /// <summary>Its username, sanitized, or empty.</summary>
    public required string Username { get; init; }

    /// <summary>Its URL, sanitized, or empty.</summary>
    public required string Url { get; init; }

    /// <summary>Why picking it offers no Always allow, when it offers none.</summary>
    public OnceOnly OnceOnly { get; init; }

    /// <summary>What Always allow would ask the person to confirm for this item, or null when it is not offered.</summary>
    public AlwaysOffer? Always { get; init; }
}

/// <summary>The person's answer to a picker prompt.</summary>
/// <param name="Answer">What they said, or why they were not asked; only an allow with a pick releases.</param>
/// <param name="Picked">The candidate they picked, by its place in <see cref="ApprovalPrompt.Candidates"/>, or null.</param>
public readonly record struct ItemPick(ApprovalAnswer Answer, int? Picked)
{
    /// <summary>A picker nobody could be shown.</summary>
    public static ItemPick NoChannel { get; } = new(ApprovalAnswer.NoChannel, null);
}
