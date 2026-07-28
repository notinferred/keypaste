namespace Keypaste.Core.Approval;

/// <summary>
/// Why a credential could not be produced. Every value except <see cref="None"/> means deny.
/// </summary>
/// <remarks>
/// The values are distinct so the audit line and the agent-facing refusal can say <em>why</em>, not
/// so any of them can be treated as recoverable. docs/PRODUCT.md law 3.7 makes every one of them a denial;
/// telling them apart is what stops an agent retrying a request that will never succeed.
/// </remarks>
public enum CredentialFailure
{
    /// <summary>Nothing failed.</summary>
    None = 0,

    /// <summary>No vault is unlocked, so nothing can be resolved or read.</summary>
    VaultLocked = 1,

    /// <summary>Nothing in the vault answers to that name.</summary>
    NotFound = 2,

    /// <summary>
    /// More than one entry answers to that name. Refused rather than guessed at: entry paths are
    /// <c>GroupPath + "/" + Title</c> with no escaping, so a title containing a slash can collide
    /// with a real group. <see cref="EntryHandle"/> exists so a colliding entry stays addressable.
    /// </summary>
    Ambiguous = 3,

    /// <summary>The field asked for is not one keypaste releases.</summary>
    NoSuchField = 4,

    /// <summary>
    /// The entry exists and the field is empty. Refused rather than released, because handing an
    /// agent an empty string as though it were a credential hides a misconfigured vault behind a
    /// successful-looking call.
    /// </summary>
    Empty = 5,

    /// <summary>The vault could not be read at all.</summary>
    Failed = 6,
}
