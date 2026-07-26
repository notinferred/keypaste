using Keypaste.Core;
using Keypaste.Mcp.Tools;

namespace Keypaste.Mcp;

/// <summary>Whether the vault could be read at all.</summary>
internal enum VaultAvailability
{
    /// <summary>No unlocked session exists. The only answer this version can give.</summary>
    Locked = 0,

    /// <summary>The names were read.</summary>
    Available = 1,

    /// <summary>Something went wrong. Treated exactly like <see cref="Locked"/>: deny.</summary>
    Failed = 2,
}

/// <summary>What the vault had to say when asked for its entry names.</summary>
/// <param name="Availability">Whether there was anything to say.</param>
/// <param name="Names">The names, unsanitized and unfiltered. Empty unless available.</param>
/// <param name="Reason">A human-readable explanation, used when the answer is a refusal.</param>
internal sealed record EntryNameListing(
    VaultAvailability Availability,
    IReadOnlyList<EntryName> Names,
    string Reason);

/// <summary>
/// The seam between the bridge and an unlocked vault.
/// </summary>
/// <remarks>
/// <para>
/// It yields <see cref="EntryName"/>, which holds a group path and a title and has no other members.
/// So no implementation — including the real one Stage 2.2 adds — can return a password through the
/// listing path even by mistake. That is a structural guarantee, not a promise (THREATS.md T-8).
/// </para>
/// <para>
/// <b><c>request_credential</c> deliberately does not use this.</b> Stage 2.2 adds a separate seam
/// for approval and retrieval. Fusing the two into one "vault access" abstraction would hand the
/// listing path the ability to return a secret, which is the single change most likely to turn
/// <c>list_entry_names</c> into an exfiltration tool.
/// </para>
/// </remarks>
internal interface IEntryNameSource
{
    /// <summary>Reads every entry name the vault holds.</summary>
    /// <returns>The names, or a refusal.</returns>
    EntryNameListing List();
}

/// <summary>The only implementation this version ships: there is no unlocked vault.</summary>
/// <remarks>
/// An MCP server's stdin and stdout <em>are</em> the protocol stream, and Claude Desktop starts it
/// with no terminal, so there is nowhere to ask for a master password. Putting one in the client's
/// configuration file would place the secret that protects every other secret into plaintext JSON,
/// which is what CORE.md law 3.1 exists to prevent; asking the client to collect it would route it
/// through the untrusted party, which is worse. Stage 2.2 builds a human channel, and whatever owns
/// that channel owns the unlocked session. THREATS.md T-7.
/// </remarks>
internal sealed class LockedEntryNameSource : IEntryNameSource
{
    /// <inheritdoc/>
    public EntryNameListing List() =>
        new(VaultAvailability.Locked, [], ToolText.VaultLocked);
}
