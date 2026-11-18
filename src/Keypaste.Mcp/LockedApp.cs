using Keypaste.Core.Ownership;

namespace Keypaste.Mcp;

/// <summary>
/// Tells the desktop app, while it is open and locked on a vault this bridge serves, that an agent asked, so it can
/// tell the person (D-0466).
/// </summary>
/// <remarks>
/// Called only where a call is about to be refused because nothing holds the vault, and it changes only what
/// the agent reads: the call is still refused, and recorded as finding nothing holding the vault.
/// </remarks>
internal static class LockedApp
{
    /// <summary>Tells the app listening for a vault this bridge serves, if one is, that an agent asked.</summary>
    /// <param name="vault">The vault the call was for, which names where the app listens, or null without one.</param>
    /// <param name="cancellationToken">The call's token; cancelled first, nothing is told.</param>
    /// <returns>Whether an app listening for this vault was reached. False without a vault.</returns>
    internal static Task<bool> TellAsync(BridgeVault? vault, CancellationToken cancellationToken) =>
        vault is not null
            ? LockedAppEndpoint.TellAsync(vault.LockedAppName, cancellationToken)
            : Task.FromResult(false);
}
