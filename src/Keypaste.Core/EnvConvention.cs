namespace Keypaste.Core;

/// <summary>
/// Where a project's environment variables live inside a KDBX file: the group
/// <c>env/&lt;project&gt;</c>, one entry per variable, title = <c>KEY</c>, password = value.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is keypaste-specific machinery — there are no marker attributes and no custom
/// string fields. The group path <em>is</em> the marker, which is what lets KeePassXC add, edit,
/// rename, and delete environment variables with no knowledge of keypaste at all. The rejected
/// alternative (one entry per project, custom string fields for KEY→value) is recorded in
/// DECISIONS.md D-0014: <c>keepassxc-cli</c> can read custom string fields but cannot write one,
/// so "a KeePassXC-edited value is picked up by keypaste" could never have been proven in CI.
/// </para>
/// <para>
/// This type is deliberately free of any dependency on <see cref="Vault"/>: it answers questions
/// about paths, which the MCP bridge needs in order to scope a policy to environment secrets
/// without opening a vault.
/// </para>
/// </remarks>
public static class EnvConvention
{
    /// <summary>The top-level group under which every project's variables are stored.</summary>
    public const string RootGroup = "env";

    /// <summary>The group path holding one project's variables, such as <c>env/billing-api</c>.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The group path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <remarks>
    /// Does not validate. Building a path has to work for anything already in the file so that a
    /// variable KeePassXC created under a name keypaste would refuse can still be listed and
    /// removed — see <see cref="IsValidProject"/> for where the rules are actually enforced.
    /// </remarks>
    public static string GroupPath(string project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return RootGroup + "/" + project;
    }

    /// <summary>The entry path of one variable, such as <c>env/billing-api/DATABASE_URL</c>.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="key">The variable name.</param>
    /// <returns>The entry path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> or <paramref name="key"/> is null.</exception>
    public static string EntryPath(string project, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return GroupPath(project) + "/" + key;
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
    public static bool IsValidProject(string project, out string error)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Length == 0)
        {
            error = "the project name cannot be empty";
            return false;
        }

        foreach (char c in project)
        {
            if (c is '/' or '\\')
            {
                error = $"the project name cannot contain '{c}'";
                return false;
            }

            if (char.IsControl(c))
            {
                error = "the project name cannot contain control characters";
                return false;
            }
        }

        if (project.Trim().Length != project.Length)
        {
            error = "the project name cannot begin or end with whitespace";
            return false;
        }

        error = string.Empty;
        return true;
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
