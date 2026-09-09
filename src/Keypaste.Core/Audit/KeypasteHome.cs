namespace Keypaste.Core.Audit;

/// <summary>Where keypaste keeps the state that belongs to a machine rather than to a vault.</summary>
/// <remarks>Deliberately not beside the vault, and why: DECISIONS.md D-0020.</remarks>
public static class KeypasteHome
{
    /// <summary>The variable that overrides the location, mostly so tests need no home directory.</summary>
    public const string EnvironmentVariable = "KEYPASTE_HOME";

    /// <summary>The directory keypaste keeps under the user's profile.</summary>
    public const string DirectoryName = ".keypaste";

    /// <summary>The audit log's file name.</summary>
    public const string AuditFileName = "audit.jsonl";

    /// <summary>The policy file's name.</summary>
    public const string PolicyFileName = "policy.toml";

    /// <summary>The desktop app's list of vaults it has opened on this machine.</summary>
    public const string RecentFileName = "recent.toml";

    /// <summary>The desktop app's settings.</summary>
    public const string SettingsFileName = "app.toml";

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

    /// <summary>Resolves the policy file's path. keypaste never writes it.</summary>
    /// <remarks>
    /// The reason it is not beside the vault is stronger here than for the log: the log is a
    /// <em>record</em> of this machine, the policy file an <em>authorization</em> over it, and a
    /// synced directory would let another machine grant an agent silent access to this one's
    /// credentials (THREATS.md T-15).
    /// </remarks>
    public static string PolicyPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), PolicyFileName);

    /// <summary>Resolves the recent-vaults list. The file is not created.</summary>
    /// <remarks>
    /// Unlike <see cref="PolicyPath"/>, keypaste writes this one: it is a convenience keypaste
    /// maintains rather than an authorization a human wrote, and deleting it loses only a shortcut.
    /// </remarks>
    public static string RecentPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), RecentFileName);

    /// <summary>Resolves the desktop app's settings file. The file is not created.</summary>
    /// <remarks>
    /// Nothing in it is an authorization: the idle timeout it carries is a convenience over a default
    /// that already locks, so a missing or unreadable file costs a preference and never costs a lock.
    /// </remarks>
    public static string SettingsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), SettingsFileName);
}
