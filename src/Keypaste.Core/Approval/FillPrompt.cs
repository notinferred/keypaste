using Keypaste.Core.Browser;

namespace Keypaste.Core.Approval;

/// <summary>One item a person may fill on a page, as a fill prompt lists it: never its password.</summary>
/// <param name="Uuid">The item's UUID, which the person's answer names.</param>
/// <param name="Title">Its title, sanitized.</param>
/// <param name="Username">Its username as stored, sanitized, or empty.</param>
/// <param name="Group">Its group's path, sanitized, or empty at the top level.</param>
public sealed record FillCandidate(string Uuid, string Title, string Username, string Group);

/// <summary>What a person is shown before keypaste fills a login in a browser that asked for one on their click.</summary>
/// <remarks>
/// The browser and the page are what the extension claims (THREATS.md T-48, T-58), so the prompt names the build and the pairing and
/// never says the person's browser asked. It has no member for a password.
/// </remarks>
public sealed record FillPrompt
{
    /// <summary>The most logins one prompt lists, each picked in a terminal by one digit; any more are not offered.</summary>
    public const int MaximumCandidates = 9;

    /// <summary>The build the browser named.</summary>
    public required BrowserIdentity Identity { get; init; }

    /// <summary>The pairing's name.</summary>
    public required string Pairing { get; init; }

    /// <summary>The page's origin, as <c>scheme://host:port</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>The page's site, its registrable domain or its host, which the prompt emphasises within <see cref="Origin"/>.</summary>
    public required string Site { get; init; }

    /// <summary>Where the page's form posts when that is another site, or null.</summary>
    public string? FormOrigin { get; init; }

    /// <summary>The items the person may fill, in the vault's order, at least one.</summary>
    public required IReadOnlyList<FillCandidate> Candidates { get; init; }

    /// <summary>Whether the prompt offers "Always fill this item on" <see cref="Origin"/>.</summary>
    public bool OffersAlways { get; init; }

    /// <summary>What the prompt says asked: <c>keypaste's extension from the Chrome Web Store (…)</c>.</summary>
    public string IdentityText => Identity.Described;

    /// <summary>The "Always" choice's words, saying how long it lasts.</summary>
    public string AlwaysText => $"Always fill this item on {Origin}, until you remove it in Settings";
}

/// <summary>The person's answer to a <see cref="FillPrompt"/>.</summary>
/// <param name="Answer">What they said, or why they were not asked; only <see cref="ApprovalAnswer.ApprovedOnce"/> fills.</param>
/// <param name="Uuid">The item they chose to fill, when they allowed it; otherwise null.</param>
/// <param name="Always">Whether they ticked "Always fill this item on" the origin.</param>
public readonly record struct FillAnswer(ApprovalAnswer Answer, string? Uuid, bool Always)
{
    /// <summary>A prompt nobody could be shown.</summary>
    public static FillAnswer NoChannel { get; } = new(ApprovalAnswer.NoChannel, null, false);
}
