namespace Keypaste.Core;

/// <summary>
/// The names a set of variables must have before it can leave the vault — into a child process,
/// or into a file.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not enforced by <see cref="EnvStore.Read"/>. Reading stays permissive so that
/// <c>env ls</c> and <c>env rm</c> can still show and clear whatever KeePassXC put in the file
/// (CORE.md law 4.6); it is the moment a name becomes a real environment variable, or a line in a
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

        // Ordinal-blind, deliberately, so the collision is found on every platform rather than only
        // on the one where it happens to matter. A vault that runs on Linux and refuses on Windows
        // is a failure a teammate cannot reproduce, which is worse than a rule that always holds.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables)
        {
            if (seen.TryGetValue(variable.Key, out var other)
                && !string.Equals(other, variable.Key, StringComparison.Ordinal))
            {
                error = $"contains '{other}' and '{variable.Key}', which differ only in case. " +
                    "They are two variables on Linux and one on Windows, so rename one in KeePassXC.";
                return false;
            }

            seen[variable.Key] = variable.Key;
        }

        error = string.Empty;
        return true;
    }
}
