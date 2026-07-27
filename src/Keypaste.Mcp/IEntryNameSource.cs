using Keypaste.Core;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;

namespace Keypaste.Mcp;

/// <summary>Whether the vault could be read at all.</summary>
internal enum VaultAvailability
{
    /// <summary>
    /// An approver answered and had no vault open. <see cref="ToolText.VaultLocked"/> is what the
    /// agent is told.
    /// </summary>
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
/// So no implementation — including the one that reads a real unlocked vault — can return a password through the
/// listing path even by mistake. That is a structural guarantee, not a promise (THREATS.md T-8).
/// </para>
/// <para>
/// <b><c>request_credential</c> deliberately does not use this.</b> It is served by a separate seam
/// for approval and retrieval. Fusing the two into one "vault access" abstraction would hand the
/// listing path the ability to return a secret, which is the single change most likely to turn
/// <c>list_entry_names</c> into an exfiltration tool.
/// </para>
/// </remarks>
internal interface IEntryNameSource
{
    /// <summary>Reads every entry name the vault holds.</summary>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The names, or a refusal.</returns>
    ValueTask<EntryNameListing> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Asks <c>keypaste agent</c> which names may be shown.</summary>
/// <remarks>
/// <para>
/// This is what closes THREATS.md T-7. The bridge still cannot unlock anything — its stdin and
/// stdout <em>are</em> the protocol stream and Claude Desktop starts it with no terminal — so it
/// asks the process that a human unlocked in their own terminal. The listing, exposure and
/// sanitization code that used to be unreachable in the shipped binary is on the live path now.
/// </para>
/// <para>
/// <b>The separation D-0022 asked for survives the move onto a socket.</b> What comes back over the
/// wire is a group path and a title per entry and nothing else, decoded into <see cref="EntryName"/>
/// — a type with two members and nowhere to put a secret. Sharing one pipe with the credential
/// path does not fuse them; sharing an interface would have.
/// </para>
/// </remarks>
internal sealed class ApproverEntryNameSource(ApproverConnection approver, ServerOptions options)
    : IEntryNameSource
{
    /// <inheritdoc/>
    public async ValueTask<EntryNameListing> ListAsync(CancellationToken cancellationToken)
    {
        var (reply, reachable) = await approver
            .ListAsync(new NamesRequest(options.Exposure.Globs), cancellationToken)
            .ConfigureAwait(false);

        if (reply is null)
        {
            return new EntryNameListing(
                VaultAvailability.Failed,
                [],
                reachable ? ToolText.ApproverFailed : ToolText.NoApproverForListing);
        }

        return reply.VaultUnlocked
            ? new EntryNameListing(VaultAvailability.Available, reply.Names, string.Empty)
            : new EntryNameListing(VaultAvailability.Locked, [], ToolText.VaultLocked);
    }
}
