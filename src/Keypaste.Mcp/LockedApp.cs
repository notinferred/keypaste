using Keypaste.Core.Ownership;

namespace Keypaste.Mcp;

/// <summary>
/// Tells the desktop app, while it is open and locked on this bridge's vault, that an agent asked, so it can
/// tell the person (D-0466).
/// </summary>
/// <remarks>
/// Called only where a call is about to be refused because nothing holds the vault, and it changes only what
/// the agent reads: the call is still refused, and recorded as finding nothing holding the vault.
/// </remarks>
internal static class LockedApp
{
    /// <summary>Tells the app listening for this bridge's vault, if one is, that an agent asked.</summary>
    /// <param name="options">The bridge's options, which name where the app listens.</param>
    /// <param name="cancellationToken">The call's token; cancelled first, nothing is told.</param>
    /// <returns>Whether an app listening for this vault was reached. False without a vault.</returns>
    internal static Task<bool> TellAsync(ServerOptions options, CancellationToken cancellationToken) =>
        options.LockedAppName is { } name
            ? LockedAppEndpoint.TellAsync(name, cancellationToken)
            : Task.FromResult(false);
}
