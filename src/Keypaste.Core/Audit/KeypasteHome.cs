namespace Keypaste.Core.Audit;

/// <summary>Where keypaste keeps the state that belongs to a machine rather than to a vault.</summary>
/// <remarks>Deliberately not beside the vault.</remarks>
public static class KeypasteHome
{
    /// <summary>The variable that overrides the location, mostly so tests need no home directory.</summary>
    public const string EnvironmentVariable = "KEYPASTE_HOME";

    /// <summary>The directory keypaste keeps under the user's profile.</summary>
    public const string DirectoryName = ".keypaste";

    /// <summary>The audit log's file name.</summary>
    public const string AuditFileName = "audit.jsonl";

    /// <summary>The retired policy file's name, which keypaste no longer reads.</summary>
    public const string PolicyFileName = "policy.toml";

    /// <summary>The desktop app's list of vaults it has opened on this machine.</summary>
    public const string RecentFileName = "recent.toml";

    /// <summary>The desktop app's settings, and the vault agents and the CLI use without <c>--vault</c>.</summary>
    public const string SettingsFileName = "app.toml";

    /// <summary>The desktop app's project mappings: a directory and a command for each env set.</summary>
    public const string ProjectsFileName = "projects.json";

    /// <summary>Resolves keypaste's home directory. The directory is not created.</summary>
    /// <remarks>
    /// <see cref="Environment.SpecialFolder.UserProfile"/> rather than <c>ApplicationData</c> because
    /// it is one path on all three operating systems, which keeps the documentation and the
    /// troubleshooting steps identical everywhere. An empty variable counts as unset, like
    /// <c>KEYPASTE_VAULT</c>.
    /// </remarks>
    public static string Resolve(string? fromEnvironment)
    {
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.GetFullPath(Path.Combine(profile, DirectoryName));
    }

    /// <summary>Resolves the audit log's path. The file is not created.</summary>
    public static string AuditPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), AuditFileName);

    /// <summary>Resolves where the retired policy file was kept, so <c>keypaste approve</c> can say it no longer reads one there.</summary>
    /// <remarks>Agents' rules are kept in <see cref="AgentsDirectory"/> now.</remarks>
    public static string PolicyPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), PolicyFileName);

    /// <summary>Resolves the recent-vaults list. The file is not created.</summary>
    /// <remarks>
    /// keypaste writes this one: it is a convenience keypaste maintains rather than an authorization,
    /// and deleting it loses only a shortcut.
    /// </remarks>
    public static string RecentPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), RecentFileName);

    /// <summary>Resolves the desktop app's settings file. The file is not created.</summary>
    /// <remarks>
    /// Nothing in it is an authorization: the idle timeout is a convenience over a default that
    /// already locks, and the chosen vault only says which vault's owner to ask.
    /// </remarks>
    public static string SettingsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), SettingsFileName);

    /// <summary>Resolves the desktop app's project mappings. The file is not created.</summary>
    /// <remarks>It names directories and commands on this machine and never holds a value.</remarks>
    public static string ProjectsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), ProjectsFileName);

    /// <summary>The recommendations dismissed on this machine.</summary>
    public const string RecommendationsFileName = "recommendations.json";

    /// <summary>Resolves the dismissed recommendations. The file is not created.</summary>
    /// <remarks>It names vaults, entries and keys by identifier and never holds a title or a value.</remarks>
    public static string RecommendationsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), RecommendationsFileName);

    /// <summary>The desktop app's crash reports waiting to be sent.</summary>
    public const string CrashReportsDirectoryName = "crash-reports";

    /// <summary>Resolves the folder of crash reports waiting to be sent. The folder is not created.</summary>
    /// <remarks>
    /// It holds only what <see cref="CrashReports.CrashScrubber"/> wrote and the run marker, which names the version and
    /// when the run started, never a name, path or value.
    /// </remarks>
    public static string CrashReportsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), CrashReportsDirectoryName);

    /// <summary>The one file every vault's agent rules were kept in before each vault kept its own.</summary>
    public const string AgentsFileName = "agents.toml";

    /// <summary>The folder of each vault's agent rules, one file per vault.</summary>
    public const string AgentsDirectoryName = "agents";

    /// <summary>Resolves a vault's agent rules: what every agent's request to that vault is decided by, and each known agent's run switch. The file is not created.</summary>
    /// <remarks>Not <c>app.toml</c>, which holds the desktop's preferences: <c>keypaste approve</c> decides by the same file.</remarks>
    public static string AgentsPath(string? fromEnvironment, Ownership.VaultIdentity vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return Path.Combine(Resolve(fromEnvironment), AgentsDirectoryName, vault.Key + ".toml");
    }

    /// <summary>Resolves the folder of each vault's agent rules. The folder is not created.</summary>
    public static string AgentsDirectory(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), AgentsDirectoryName);

    /// <summary>Resolves the one <c>agents.toml</c> every vault shared before, whose rules move to the vault whose key sealed them. The file is not created.</summary>
    public static string SharedAgentsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), AgentsFileName);

    /// <summary>The folder of the save count of the copy of each vault this home last opened or saved, one file per vault.</summary>
    public const string SaveCountsDirectoryName = "save-counts";

    /// <summary>Resolves where this home remembers a vault's save count. The file is not created.</summary>
    /// <remarks>It holds one number and is named by the vault's identity, never its path.</remarks>
    public static string SaveCountPath(string? fromEnvironment, Ownership.VaultIdentity vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return Path.Combine(Resolve(fromEnvironment), SaveCountsDirectoryName, vault.Key);
    }

    /// <summary>Resolves this home's random identity, which every vault's sealed rules cover. The file is not created.</summary>
    public static string HomeIdPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), "agents.id");
}
