namespace Keypaste.Core.Tests;

/// <summary>
/// Writes variables in the <c>env/&lt;project&gt;</c> layout of earlier releases, one entry per key,
/// which no keypaste writer creates since D-0413, for tests that need one as their fixture.
/// </summary>
internal static class LegacyVariables
{
    /// <summary>Adds or replaces a variable of a project's <c>dev</c> environment. The caller saves.</summary>
    internal static void Set(Vault vault, string project, string key, string value) =>
        Set(vault, project, EnvProfileNames.Default, key, value);

    /// <summary>Adds or replaces a variable of one environment: an entry titled by its key in the environment's group, holding the value as its password. The caller saves.</summary>
    internal static void Set(Vault vault, string project, string profile, string key, string value)
    {
        var name = new EntryName(EnvProfileNames.GroupPath(project, profile), key);

        if (vault.Find(name) is { } current)
        {
            vault.UpdateEntry(current with { Password = value });
        }
        else
        {
            vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = key, Password = value });
        }
    }
}
