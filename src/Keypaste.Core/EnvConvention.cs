namespace Keypaste.Core;

/// <summary>
/// The names of keypaste's projects: the project-name rule, which custom fields a project releases,
/// which names a child process can receive, and the <c>env</c> group where keypaste creates an
/// environment's home entry (D-0413).
/// </summary>
/// <remarks>
/// Deliberately free of any dependency on <see cref="Vault"/>: it answers questions about names,
/// which the MCP bridge needs without opening a vault. Which entries are in a project is their own
/// tags' answer (D-0370), never their group's (D-0416).
/// </remarks>
public static class EnvConvention
{
    /// <summary>The top-level group holding the home entries keypaste creates, one group per project.</summary>
    public const string RootGroup = "env";

    /// <summary>The group holding one project's home entries, such as <c>env/billing-api</c>.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The group path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <remarks>Does not validate; <see cref="IsValidProject"/> is where the rules are enforced.</remarks>
    public static string GroupPath(string project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return RootGroup + "/" + project;
    }

    /// <summary>Whether a project name is one keypaste is willing to create.</summary>
    /// <param name="project">The project name to check.</param>
    /// <param name="error">A message naming the problem, or empty when the name is valid.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    /// <remarks>
    /// An empty name would resolve to the <c>env</c> group itself, because group-path resolution
    /// discards empty segments — the variable would be written to <c>env/KEY</c>, where no read
    /// path could ever find it again. A name containing a separator would nest a group one level
    /// deeper than the project listing looks, with the same result: a silent write to nowhere.
    /// </remarks>
    public static bool IsValidProject(string project, out string error) =>
        VaultNameRules.IsValidName(project, "project name", out error);

    /// <summary>
    /// Whether a custom field is named as a project variable: <c>[A-Z][A-Z0-9_]{0,127}</c>, not
    /// starting <c>KPEX_</c>, <c>KPXC_</c> or <c>KP2A_</c>, which KeePassXC and KeePass2Android keep.
    /// </summary>
    /// <param name="name">The field's name.</param>
    /// <returns><see langword="true"/> if a tagged entry's field of this name is one of its project's variables.</returns>
    public static bool IsEnvNamedField(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Length is > 0 and <= 128
            && char.IsAsciiLetterUpper(name[0])
            && name.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')
            && !name.StartsWith("KPEX_", StringComparison.Ordinal)
            && !name.StartsWith("KPXC_", StringComparison.Ordinal)
            && !name.StartsWith("KP2A_", StringComparison.Ordinal);
    }

    /// <summary>Whether a variable name is one keypaste is willing to create.</summary>
    /// <param name="key">The variable name to check.</param>
    /// <param name="error">A message naming the problem, or empty when the name is valid.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    /// <remarks>
    /// The rule is the POSIX one for environment variable names — <c>[A-Za-z_][A-Za-z0-9_]*</c> —
    /// because a name outside it cannot be exported to a child process, which is the entire point
    /// of storing it. It is enforced only on write: a name KeePassXC put in the file is always
    /// listed, never hidden, because keypaste and KeePassXC disagreeing about the contents of the
    /// same file is the failure docs/PRODUCT.md law 4.6 exists to prevent.
    /// </remarks>
    public static bool IsValidKey(string key, out string error)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
        {
            error = "the variable name cannot be empty";
            return false;
        }

        if (!IsNameStart(key[0]))
        {
            error = $"'{key}' is not a valid environment variable name: it must start with a letter or underscore";
            return false;
        }

        // Spelled out as a loop rather than a regular expression or a character-set array: the
        // former is a dependency-free but heavyweight way to say something this simple, and the
        // latter allocates on every call for no benefit.
        for (int i = 1; i < key.Length; i++)
        {
            if (!IsNameStart(key[i]) && !char.IsAsciiDigit(key[i]))
            {
                error = $"'{key}' is not a valid environment variable name: '{key[i]}' is not allowed";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';
}
