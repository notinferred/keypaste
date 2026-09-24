namespace Keypaste.Core.Launch;

/// <summary>
/// Builds the environment a child process is started with: everything the launching process
/// inherited, plus the project's variables on top.
/// </summary>
/// <remarks>
/// This is the code that decides which credential a program receives, so docs/PRODUCT.md law 4.5
/// attaches here more than anywhere else, and it is written to be asserted directly rather than
/// inferred from what a child printed.
/// </remarks>
public static class EnvironmentMerge
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
    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The environment this process would hand a child, before any project is added.</summary>
    /// <returns>Every variable of this process, with <c>TMP</c> and <c>TEMP</c> as it started with them.</returns>
    /// <remarks>
    /// Filled by indexer rather than through the dictionary copy constructor: on Windows the
    /// comparer is case-insensitive, and a parent block that holds two names differing only in
    /// case would make the constructor throw on a duplicate key.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Inherited()
    {
        var values = new Dictionary<string, string>(Comparer);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                values[name] = value;
            }
        }

        // A child is the user's own program, so it gets the user's temporary directory - not the
        // private one saves were redirected into, which is deleted when this process exits.
        foreach (var (name, value) in ProcessTemporaryDirectory.OriginalTemporaryVariables)
        {
            if (value is null)
            {
                values.Remove(name);
            }
            else
            {
                values[name] = value;
            }
        }

        return values;
    }

    /// <summary>Merges <paramref name="variables"/> over <paramref name="parent"/>.</summary>
    /// <param name="parent">The environment the child would otherwise inherit.</param>
    /// <param name="variables">A set <see cref="EnvResolution"/> released whole.</param>
    /// <returns>The child's complete environment.</returns>
    public static IReadOnlyDictionary<string, string> Build(
        IReadOnlyDictionary<string, string> parent,
        IReadOnlyList<EnvVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(variables);

        var result = new Dictionary<string, string>(parent, Comparer);

        foreach (var variable in variables)
        {
            // The project wins. That is the point of asking for it: a stale DATABASE_URL left in
            // your shell must not beat the one you deliberately stored. An empty value is a value.
            result[variable.Key] = variable.Value;
        }

        return result;
    }

    /// <summary>Whether the project sets <c>PATH</c>, which is worth saying out loud.</summary>
    /// <param name="variables">The project's variables.</param>
    /// <returns><see langword="true"/> when one of them is <c>PATH</c> in any case.</returns>
    /// <remarks>
    /// <see cref="System.Diagnostics.ProcessStartInfo.FileName"/> is resolved against the launching
    /// process's own <c>PATH</c>, not the one handed to the child: <c>CreateProcess</c> searches in
    /// the caller's context on Windows, and .NET's Unix path resolution reads the current process's
    /// variable. So the command is found one way and then runs with another — legitimate if you
    /// are pinning a per-project toolchain, and surprising if you are not.
    /// </remarks>
    public static bool OverridesPath(IReadOnlyList<EnvVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        return variables.Any(variable => string.Equals(variable.Key, "PATH", StringComparison.OrdinalIgnoreCase));
    }
}
