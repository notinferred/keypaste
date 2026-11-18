namespace Keypaste.Mcp;

/// <summary>One vault a bridge serves: what results and requests call it, and where its owner and the app locked on it listen.</summary>
/// <param name="Name">The vault's name as an agent reads and gives it: its file's name, as the app's prompts show it, numbered when two share one.</param>
/// <param name="Path">The vault's absolute path, which every attach names.</param>
/// <param name="Key">The vault's identity key, for audit lines.</param>
/// <param name="PipeName">Where its owner listens.</param>
/// <param name="LockedAppName">Where the desktop app listens while it is open and locked on the vault, named from the vault alone, so <c>--approver</c> never moves it.</param>
internal sealed record BridgeVault(string Name, string Path, string Key, string PipeName, string LockedAppName)
{
    /// <summary>The name a vault at <paramref name="path"/> takes after <paramref name="named"/>.</summary>
    internal static string NameFor(string path, IReadOnlyList<BridgeVault> named)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        var name = stem;

        for (var number = 2; named.Any(vault => string.Equals(vault.Name, name, StringComparison.OrdinalIgnoreCase)); number++)
        {
            name = $"{stem} {number}";
        }

        return name;
    }
}
