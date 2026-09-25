namespace Keypaste.Core;

/// <summary>
/// The names a set of variables must have before it can be written into a file. A child process
/// gets its set through <see cref="EnvResolution"/>, which applies the same rules and expiry.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not enforced by <see cref="EnvStore.Read(string)"/>. Reading stays permissive so that
/// <c>env ls</c> and <c>env rm</c> can still show and clear whatever KeePassXC put in the file
/// (docs/PRODUCT.md law 4.6); it is the moment a name becomes a real environment variable, or a line in a
/// <c>.env</c>, that a wrong answer turns into a program running with the wrong credentials.
/// </para>
/// <para>
/// Messages are fragments, so a caller prefixes them with the verb and the group it is talking
/// about.
/// </para>
/// </remarks>
public static class EnvNameRules
{
    /// <summary>
    /// Rejects names that cannot be exported, and pairs that would mean different things on
    /// different platforms.
    /// </summary>
    /// <param name="variables">The project's variables.</param>
    /// <param name="error">The reason, or an empty string when there is none.</param>
    /// <returns><see langword="true"/> when every name is usable and unambiguous.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variables"/> is null.</exception>
    /// <remarks>
    /// Every offending name is listed rather than the first: somebody repairing a vault in
    /// KeePassXC should get the whole job in one run.
    /// </remarks>
    public static bool TryCheck(IReadOnlyList<EnvVariable> variables, out string error)
    {
        ArgumentNullException.ThrowIfNull(variables);

        List<string> unusable = [];
        foreach (var variable in variables)
        {
            if (!variable.IsUsableName)
            {
                unusable.Add(variable.Key);
            }
        }

        if (unusable.Count > 0)
        {
            // Skipping these with a warning was the alternative. It was rejected because a child
            // booted with a silently incomplete environment does not fail here — it fails later,
            // somewhere else, as "connected to the wrong database".
            error = $"cannot be exported: {string.Join(", ", unusable)}. " +
                "Rename them in KeePassXC, or remove them.";
            return false;
        }

        return TryCheckCase([.. variables.Select(variable => variable.Key)], out error);
    }

    /// <summary>
    /// Rejects a set holding two names that differ only in case.
    /// </summary>
    /// <param name="keys">The variable names.</param>
    /// <param name="error">The reason, or an empty string when there is none.</param>
    /// <returns><see langword="true"/> when no two names collide.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    /// <remarks>
    /// Shared with the write path, which refuses a rename or a move that would create the pair
    /// rather than leaving it for whoever next runs the project to discover. That caller words its
    /// own refusal from <see cref="OrganizeOutcome"/>: the message here ends in advice to repair
    /// the vault in KeePassXC, which is export's problem and not a rename's.
    /// </remarks>
    internal static bool TryCheckCase(IReadOnlyList<string> keys, out string error)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Ordinal-blind, deliberately, so the collision is found on every platform rather than only
        // on the one where it happens to matter. A vault that runs on Linux and refuses on Windows
        // is a failure a teammate cannot reproduce, which is worse than a rule that always holds.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (seen.TryGetValue(key, out var other) && !string.Equals(other, key, StringComparison.Ordinal))
            {
                error = $"contains '{other}' and '{key}', which differ only in case. " +
                    "They are two variables on Linux and one on Windows, so rename one in KeePassXC.";
                return false;
            }

            seen[key] = key;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether a new entry may be created at <paramref name="target"/>: outside <c>env</c> always,
    /// inside it only as a valid key in a valid project and profile that no sibling differs from
    /// only in case, the rules <c>env set</c> applies.
    /// </summary>
    /// <param name="target">The entry about to be created.</param>
    /// <param name="siblingTitles">The titles already in <paramref name="target"/>'s group.</param>
    /// <param name="error">The reason, or an empty string when there is none.</param>
    /// <returns><see langword="true"/> when the entry may be created.</returns>
    /// <remarks>
    /// A profile is judged whole when it runs, so one bad name written here would refuse every run
    /// of the profile later, somewhere else.
    /// </remarks>
    public static bool TryCheckNewEntry(EntryName target, IReadOnlyList<string> siblingTitles, out string error)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(siblingTitles);

        error = string.Empty;
        string root = EnvConvention.RootGroup;

        if (!string.Equals(target.GroupPath, root, StringComparison.Ordinal)
            && !target.GroupPath.StartsWith(root + "/", StringComparison.Ordinal))
        {
            return true;
        }

        string[] segments = target.GroupPath.Split('/');

        if (segments.Length == 1)
        {
            error = $"an entry directly in '{root}' belongs to no project; name it {root}/PROJECT/KEY";
            return false;
        }

        if (!EnvConvention.IsValidProject(segments[1], out error)
            || (segments.Length > 2 && !EnvProfileNames.IsValid(segments[2], out error)))
        {
            return false;
        }

        // D-0347: a group below a profile, or a subgroup named for the default profile, is never read.
        if (segments.Length > 3)
        {
            error = $"'{target.GroupPath}' is never read: a set is {root}/PROJECT or {root}/PROJECT/PROFILE";
            return false;
        }

        if (segments.Length == 3 && string.Equals(segments[2], EnvProfileNames.Default, StringComparison.Ordinal))
        {
            error = $"the {EnvProfileNames.Default} profile is {root}/{segments[1]} itself; name it {root}/{segments[1]}/{target.Title}";
            return false;
        }

        if (!EnvConvention.IsValidKey(target.Title, out error))
        {
            return false;
        }

        if (!TryCheckCase([.. siblingTitles, target.Title], out var collision))
        {
            error = $"'{target.GroupPath}' {collision}";
            return false;
        }

        return true;
    }
}
