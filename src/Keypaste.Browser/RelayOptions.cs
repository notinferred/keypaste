using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;

namespace Keypaste.Browser;

/// <summary>One vault whose owner the relay may reach: the vault, its owner's pipe and where the app locked on it listens.</summary>
/// <param name="Path">The vault, which every attach names.</param>
/// <param name="PipeName">Its owner's pipe.</param>
/// <param name="LockedAppName">Where the app listens while it is open and locked on the vault (D-0466).</param>
internal sealed record RelayVault(string Path, string PipeName, string LockedAppName);

/// <summary>Who started the relay, and the vaults whose owners it may reach, as <c>keypaste mcp</c> with no arguments finds the first.</summary>
/// <param name="Identity">The KeePassXC-Browser build the browser named.</param>
/// <param name="Vaults">
/// The vault <c>keypaste mcp</c> with no arguments would ask first, then each other vault of the local account; none when no vault is
/// chosen or held, which answers every request as locked.
/// </param>
/// <remarks>
/// One owner answers for every vault unlocked in its process, so the relay needs one that is unlocked, whichever: the app's
/// list spans its unlocked vaults and names its locked ones, and releases each login from its own vault. Trying each vault's pipe
/// in turn is what keeps one locked vault from making the extension say keypaste is locked.
/// </remarks>
internal sealed record RelayOptions(BrowserIdentity Identity, IReadOnlyList<RelayVault> Vaults)
{
    /// <summary>
    /// Finds the vault from <c>KEYPASTE_VAULT</c>, then the vault chosen in <c>app.toml</c>, then the local account's others, and their
    /// pipes (D-0389); a browser passes no flags. A set <c>KEYPASTE_APPROVER</c> names one owner's pipe, so it is the first vault's alone.
    /// </summary>
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
        var approver = environment(ApproverEndpoint.EnvironmentVariable);

        // No vault is not fatal, as it is not for the bridge: each request is answered as locked, which the extension shows.
        List<string> paths = [];

        if (VaultLocation.TryResolve(null, environment(VaultLocation.EnvironmentVariable), ChosenVault.Read(home), out var first, out _))
        {
            paths.Add(first);
        }

        if (approver is not { Length: > 0 })
        {
            paths.AddRange(LocalAccount.Vaults(home).Where(held => !paths.Any(path => ChosenVault.Same(path, held))));
        }

        List<RelayVault> vaults = [];

        try
        {
            // Checked with no vault to reach through it too, so a malformed name never waits for one.
            _ = ApproverEndpoint.Resolve(null, approver, null);

            foreach (var path in paths)
            {
                var vault = VaultIdentity.Of(KeypasteHome.Resolve(home), path);
                vaults.Add(new RelayVault(path, ApproverEndpoint.Resolve(null, approver, vault)!, LockedAppEndpoint.NameFor(vault)));
            }
        }
        catch (ArgumentException ex)
        {
            problem = $"{ApproverEndpoint.EnvironmentVariable}: {ex.Message}";
            return false;
        }

        options = new RelayOptions(identity, vaults);
        return true;
    }
}
