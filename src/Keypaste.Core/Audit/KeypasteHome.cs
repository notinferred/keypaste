namespace Keypaste.Core.Audit;

/// <summary>
/// Where keypaste keeps the state that belongs to a machine rather than to a vault.
/// </summary>
/// <remarks>
/// <para>
/// One rule, in one place, because the audit log (2.1), the policy file (2.3) and
/// <c>keypaste log</c> (2.4) all have to agree about it, and three frontends resolving a home
/// directory independently is exactly what CORE.md law 4.3 forbids.
/// </para>
/// <para>
/// <b>Deliberately not beside the vault.</b> The vault is a file the user syncs with their own
/// tooling — that is the whole local-first bargain in CORE.md §2. An append-only log that travels
/// with it would produce conflicted copies on every second machine, break the per-file hash chain,
/// and hand anyone with the synced folder a write path into another machine's record. The log
/// describes what happened <em>here</em>.
/// </para>
/// </remarks>
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

    /// <summary>Resolves keypaste's home directory.</summary>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <returns>An absolute path. The directory is not created.</returns>
    /// <remarks>
    /// <see cref="Environment.SpecialFolder.UserProfile"/> rather than <c>ApplicationData</c>
    /// because it is one path on all three operating systems, which keeps the documentation and the
    /// troubleshooting steps identical everywhere. An empty variable counts as unset, matching how
    /// <c>KEYPASTE_VAULT</c> already behaves.
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

    /// <summary>Resolves the audit log's path.</summary>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <returns>An absolute path. The file is not created.</returns>
    public static string AuditPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), AuditFileName);

    /// <summary>Resolves the policy file's path.</summary>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <returns>An absolute path. The file is not created — keypaste never writes it.</returns>
    /// <remarks>
    /// It sits beside the audit log rather than beside the vault for the reason above, and the
    /// reason is stronger here: the log is a <em>record</em> of this machine, but the policy file is
    /// an <em>authorization</em> over it. A synced directory would let another machine grant an
    /// agent silent access to this one's credentials (THREATS.md T-15).
    /// </remarks>
    public static string PolicyPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), PolicyFileName);

    /// <summary>Resolves the recent-vaults list.</summary>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <returns>An absolute path. The file is not created.</returns>
    /// <remarks>
    /// <para>
    /// Here rather than beside the vault for the reason at the top of this file, and the reason is
    /// the same one that put the audit log here: this describes what happened on <em>this</em>
    /// machine. A list of vault paths travelling in a synced folder would tell every one of a
    /// person's machines about the directory layout of all the others, which is information none of
    /// them needed and the sync was never asked to carry.
    /// </para>
    /// <para>
    /// <b>Unlike <see cref="PolicyPath"/>, keypaste writes this one.</b> The policy file is an
    /// authorization a human writes and keypaste only ever reads; this is a convenience keypaste
    /// maintains and a human is free to delete. Deleting it loses nothing but a shortcut.
    /// </para>
    /// </remarks>
    public static string RecentPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), RecentFileName);

    /// <summary>Resolves the desktop app's settings file.</summary>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <returns>An absolute path. The file is not created.</returns>
    /// <remarks>
    /// Written by keypaste, like <see cref="RecentPath"/> and unlike <see cref="PolicyPath"/>.
    /// Nothing in it is an authorization: the idle timeout it carries is a convenience over a
    /// default that already locks, so a missing or unreadable file costs a preference and never
    /// costs a lock (CORE.md law 3.7).
    /// </remarks>
    public static string SettingsPath(string? fromEnvironment) =>
        Path.Combine(Resolve(fromEnvironment), SettingsFileName);
}
