using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;

namespace Keypaste.Browser;

/// <summary>Who started the relay, and where the vault's owner and the locked app are, as <c>keypaste mcp</c> with no arguments finds them.</summary>
/// <param name="Identity">The KeePassXC-Browser build the browser named.</param>
/// <param name="VaultPath">The vault, or empty when none is chosen, which answers every request as locked.</param>
/// <param name="PipeName">The owner's pipe, or null without a vault.</param>
/// <param name="LockedAppName">Where the app listens while it is open and locked on the vault, or null without a vault (D-0466).</param>
internal sealed record RelayOptions(BrowserIdentity Identity, string VaultPath, string? PipeName, string? LockedAppName)
{
    /// <summary>Finds the vault from <c>KEYPASTE_VAULT</c>, then the vault chosen in <c>app.toml</c>, and its pipes (D-0389); a browser passes no flags.</summary>
    /// <param name="identity">The build the browser named.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="options">The options, when the environment names nothing malformed.</param>
    /// <param name="problem">Why not, otherwise.</param>
    /// <returns>Whether the relay may start.</returns>
    internal static bool TryResolve(
        BrowserIdentity identity,
        Func<string, string?> environment,
        [NotNullWhen(true)] out RelayOptions? options,
        out string problem)
    {
        options = null;
        problem = string.Empty;

        var home = environment(KeypasteHome.EnvironmentVariable);

        // No vault is not fatal, as it is not for the bridge: each request is answered as locked, which the extension shows.
        VaultLocation.TryResolve(null, environment(VaultLocation.EnvironmentVariable), ChosenVault.Read(home), out var vaultPath, out _);
        var vault = vaultPath.Length > 0 ? VaultIdentity.Of(KeypasteHome.Resolve(home), vaultPath) : null;

        string? pipeName;

        try
        {
            pipeName = ApproverEndpoint.Resolve(null, environment(ApproverEndpoint.EnvironmentVariable), vault);
        }
        catch (ArgumentException ex)
        {
            problem = $"{ApproverEndpoint.EnvironmentVariable}: {ex.Message}";
            return false;
        }

        options = new RelayOptions(identity, vaultPath, pipeName, vault is null ? null : LockedAppEndpoint.NameFor(vault));
        return true;
    }
}
