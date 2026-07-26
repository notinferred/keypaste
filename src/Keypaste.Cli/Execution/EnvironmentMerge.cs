using Keypaste.Core;

namespace Keypaste.Cli.Execution;

/// <summary>
/// Builds the environment a child process is started with: everything keypaste inherited, plus
/// the project's variables on top.
/// </summary>
/// <remarks>
/// Pure — no vault, no process, no console. This is the code that decides which credential a
/// program receives, so CORE.md law 4.5 attaches here more than anywhere else in the CLI, and it
/// is written to be asserted directly rather than inferred from what a child printed.
/// </remarks>
internal static class EnvironmentMerge
{
    /// <summary>
    /// How environment variable names are compared: case-insensitively on Windows, exactly
    /// everywhere else.
    /// </summary>
    /// <remarks>
    /// Named and tested rather than left to a <see cref="Dictionary{TKey,TValue}"/> default,
    /// because it is the difference between a vault's <c>Path</c> replacing the inherited
    /// <c>PATH</c> and quietly sitting beside it.
    /// </remarks>
    internal static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Merges <paramref name="variables"/> over <paramref name="parent"/>, refusing anything that
    /// could not be injected the same way on every platform.
    /// </summary>
    /// <returns><see langword="false"/> with <paramref name="error"/> set, having built nothing.</returns>
    internal static bool TryBuild(
        IReadOnlyDictionary<string, string> parent,
        IReadOnlyList<EnvVariable> variables,
        out IReadOnlyDictionary<string, string> merged,
        out string error)
    {
        merged = null!;

        if (!TryCheck(variables, out error))
        {
            return false;
        }

        var result = new Dictionary<string, string>(parent, Comparer);

        foreach (var variable in variables)
        {
            // The project wins. That is the point of asking for it: a stale DATABASE_URL left in
            // your shell must not beat the one you deliberately stored. An empty value is a value.
            result[variable.Key] = variable.Value;
        }

        merged = result;
        return true;
    }

    /// <summary>Whether the project sets <c>PATH</c>, which is worth saying out loud.</summary>
    /// <remarks>
    /// <see cref="System.Diagnostics.ProcessStartInfo.FileName"/> is resolved against keypaste's
    /// own <c>PATH</c>, not the one handed to the child: <c>CreateProcess</c> searches in the
    /// caller's context on Windows, and .NET's Unix path resolution reads the current process's
    /// variable. So the command is found one way and then runs with another — legitimate if you
    /// are pinning a per-project toolchain, and surprising if you are not.
    /// </remarks>
    internal static bool OverridesPath(IReadOnlyList<EnvVariable> variables)
    {
        foreach (var variable in variables)
        {
            if (string.Equals(variable.Key, "PATH", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rejects names that cannot be exported, and pairs that mean different things on different
    /// platforms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both checks fail closed, and both live here rather than in <see cref="EnvStore"/>: reading
    /// stays permissive so that <c>env ls</c> and <c>env rm</c> can still show and clear whatever
    /// KeePassXC put in the file (CORE.md law 4.6), while injecting — where a wrong answer becomes
    /// a running program with the wrong credentials — does not.
    /// </para>
    /// <para>
    /// Every offending name is listed, not the first: somebody repairing a vault in KeePassXC
    /// should get the whole job in one run.
    /// </para>
    /// </remarks>
    private static bool TryCheck(IReadOnlyList<EnvVariable> variables, out string error)
    {
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

        // Ordinal, deliberately, so the collision is found on every platform rather than only on
        // the one where it happens to matter. A vault that runs on Linux and refuses on Windows
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
