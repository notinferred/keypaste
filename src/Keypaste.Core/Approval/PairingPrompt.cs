using Keypaste.Core.Browser;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown before a browser is paired, while they have armed pairing: the KeePassXC-Browser build the browser
/// named when it started keypaste, and the name the pairing will have, which they may change (D-0634).
/// </summary>
/// <remarks>
/// The build is what the browser claims, and any program running as the person can start keypaste claiming it (THREATS.md
/// T-48), so the prompt names it and never says the person's browser asked. It has no member for a key.
/// </remarks>
public sealed record PairingPrompt
{
    /// <summary>The build the browser named.</summary>
    public required BrowserIdentity Identity { get; init; }

    /// <summary>The name offered, which becomes the pairing's id unless the person changes it.</summary>
    public required string SuggestedName { get; init; }

    /// <summary>The names the vault's pairings already have, which a new one cannot take.</summary>
    public required IReadOnlyList<string> TakenNames { get; init; }

    /// <summary>Whether the person chose the name when they armed pairing, so the prompt shows it rather than asks for one.</summary>
    public bool NameChosen { get; init; }

    /// <summary>What the prompt says asked: <c>KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)</c>.</summary>
    public string IdentityText => Identity.Described;

    /// <summary>Why a name cannot be given to this pairing, or null when it can.</summary>
    /// <param name="name">The name typed.</param>
    /// <returns>keypaste's own words, or null.</returns>
    public string? NameProblem(string name) => PairingNames.Problem(name, TakenNames);
}

/// <summary>The person's answer to a <see cref="PairingPrompt"/>.</summary>
/// <param name="Answer">What they said, or why they were not asked; only <see cref="ApprovalAnswer.ApprovedOnce"/> pairs.</param>
/// <param name="Name">The name they gave the pairing, when they allowed it; otherwise null.</param>
public readonly record struct PairingAnswer(ApprovalAnswer Answer, string? Name)
{
    /// <summary>A prompt nobody could be shown.</summary>
    public static PairingAnswer NoChannel { get; } = new(ApprovalAnswer.NoChannel, null);
}

/// <summary>Tells the person that a browser asked to pair while pairing was not armed, which was refused unasked (D-0634).</summary>
/// <param name="Identity">The build the browser named.</param>
public sealed record PairingNotice(BrowserIdentity Identity)
{
    /// <summary>What the app says.</summary>
    public const string AppText = "A browser asked to pair. To pair it, press Pair a browser.";
}
