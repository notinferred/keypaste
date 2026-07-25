namespace Keypaste.Core;

/// <summary>What a call to <see cref="EnvStore.TrySet"/> did.</summary>
public enum EnvSetOutcome
{
    /// <summary>The variable was refused; the reason is in the call's error output.</summary>
    Rejected = 0,

    /// <summary>The variable did not exist and was added.</summary>
    Created = 1,

    /// <summary>The variable existed and its value was replaced.</summary>
    Updated = 2,
}

/// <summary>One environment variable read out of a vault.</summary>
/// <param name="Key">The variable's name, taken verbatim from the entry title.</param>
/// <param name="Value">The variable's value, taken from the entry password.</param>
public sealed record EnvVariable(string Key, string Value)
{
    /// <summary>
    /// Whether <see cref="Key"/> is a name that can actually be exported to a child process.
    /// </summary>
    /// <remarks>
    /// False only for variables written by something other than keypaste, since
    /// <see cref="EnvStore.TrySet"/> refuses to create one. Reading them anyway is deliberate:
    /// hiding a variable that KeePassXC displays would make the two tools disagree about the
    /// contents of one file (CORE.md law 4.6).
    /// </remarks>
    public bool IsUsableName => EnvConvention.IsValidKey(Key, out _);
}

/// <summary>
/// Reads and writes environment-variable sets in a <see cref="Vault"/>, following
/// <see cref="EnvConvention"/>.
/// </summary>
/// <remarks>
/// <para>
/// The convention lives here rather than in the CLI because the MCP bridge and the GUI will store
/// env sets in exactly the same shape, and CORE.md law 4.3 does not allow that rule to be written
/// down three times. Nothing above this type should know that the group is called <c>env</c>.
/// </para>
/// <para>
/// The governing rule throughout is <b>permissive on read, strict on write</b>. Anything KeePassXC
/// can put in the file is listed; only what keypaste itself creates is validated.
/// </para>
/// <para>
/// Deliberately not <see cref="IDisposable"/>: it borrows a vault rather than owning one, so it
/// adds no lifetime of its own to manage.
/// </para>
/// </remarks>
/// <param name="vault">The open vault to work in. The caller retains ownership of it.</param>
public sealed class EnvStore(Vault vault)
{
    private readonly Vault _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    /// <summary>Lists the projects that have an environment set, ordinal-sorted.</summary>
    /// <returns>The project names.</returns>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <remarks>
    /// Only the immediate children of the <c>env</c> group count as projects. A group nested more
    /// deeply is not reported, because <see cref="Read"/> could not find its variables either.
    /// </remarks>
    public IReadOnlyList<string> Projects()
    {
        string prefix = EnvConvention.RootGroup + "/";
        List<string> projects = [];

        foreach (string groupPath in _vault.ReadGroupPaths())
        {
            if (!groupPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = groupPath[prefix.Length..];
            if (name.Length > 0 && !name.Contains('/'))
            {
                projects.Add(name);
            }
        }

        projects.Sort(StringComparer.Ordinal);
        return projects;
    }

    /// <summary>Whether the given project has a group, even an empty one.</summary>
    /// <param name="project">The project name.</param>
    /// <returns><see langword="true"/> if the group exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <remarks>
    /// Distinguishes "this project has no variables" from "there is no such project", which are
    /// different answers to <c>keypaste env ls</c> and deserve different exit codes.
    /// </remarks>
    public bool ProjectExists(string project)
    {
        string groupPath = EnvConvention.GroupPath(project);

        foreach (string candidate in _vault.ReadGroupPaths())
        {
            if (string.Equals(candidate, groupPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads one project's variables, ordinal-sorted by name.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The variables, empty if the project has none or does not exist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <exception cref="VaultException">
    /// Two entries in the project share a name. KDBX allows it and KeePassXC will create it, but
    /// there is no correct answer to "what is the value of that variable", so it fails closed
    /// rather than silently picking one (CORE.md law 3.7).
    /// </exception>
    public IReadOnlyList<EnvVariable> Read(string project)
    {
        string groupPath = EnvConvention.GroupPath(project);
        List<EnvVariable> variables = [];

        foreach (VaultEntry entry in _vault.ReadEntries())
        {
            // An entry with no title has no name to be exported under, and its path collides with
            // its own group's. It is left to KeePassXC to display and to fix.
            if (!string.Equals(entry.GroupPath, groupPath, StringComparison.Ordinal)
                || entry.Title.Length == 0)
            {
                continue;
            }

            variables.Add(new EnvVariable(entry.Title, entry.Password));
        }

        variables.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        for (int i = 1; i < variables.Count; i++)
        {
            if (string.Equals(variables[i].Key, variables[i - 1].Key, StringComparison.Ordinal))
            {
                throw new VaultException(
                    $"'{EnvConvention.GroupPath(project)}' contains more than one entry named " +
                    $"'{variables[i].Key}'. Remove the duplicate in KeePassXC.");
            }
        }

        return variables;
    }

    /// <summary>
    /// Sets a variable, creating the project group if it is missing. The caller must
    /// <see cref="Vault.Save"/> to persist it.
    /// </summary>
    /// <param name="project">The project name.</param>
    /// <param name="key">The variable name.</param>
    /// <param name="value">The value. An empty value is allowed, matching <c>KEY=</c> in a .env file.</param>
    /// <param name="error">A message naming the problem when the result is
    /// <see cref="EnvSetOutcome.Rejected"/>, otherwise empty.</param>
    /// <returns>Whether the variable was created, updated, or refused.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <exception cref="VaultException">The project already contains a duplicate name.</exception>
    /// <remarks>
    /// Updating keeps the entry's other fields, its identity, and its history — see
    /// <see cref="Vault.UpdateEntry"/> for what that means for a rotated secret.
    /// </remarks>
    public EnvSetOutcome TrySet(string project, string key, string value, out string error)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!EnvConvention.IsValidProject(project, out error)
            || !EnvConvention.IsValidKey(key, out error))
        {
            return EnvSetOutcome.Rejected;
        }

        IReadOnlyList<EnvVariable> existing = Read(project);

        foreach (EnvVariable variable in existing)
        {
            // Two variables differing only in case are distinct on Linux and macOS but collide on
            // Windows, where the survivor is whichever the runtime happens to apply last. Refusing
            // to create the second one keeps every vault injectable everywhere.
            if (string.Equals(variable.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(variable.Key, key, StringComparison.Ordinal))
            {
                error = $"'{project}' already has '{variable.Key}', which differs from '{key}' only in case";
                return EnvSetOutcome.Rejected;
            }
        }

        string entryPath = EnvConvention.EntryPath(project, key);
        VaultEntry? current = _vault.Find(entryPath);

        if (current is null)
        {
            _vault.AddEntry(new VaultEntry
            {
                Title = key,
                Password = value,
                GroupPath = EnvConvention.GroupPath(project),
            });

            return EnvSetOutcome.Created;
        }

        _vault.UpdateEntry(current with { Password = value });
        return EnvSetOutcome.Updated;
    }

    /// <summary>
    /// Removes a variable. The caller must <see cref="Vault.Save"/> to persist it.
    /// </summary>
    /// <param name="project">The project name.</param>
    /// <param name="key">The variable name.</param>
    /// <returns>
    /// <see langword="true"/> if a variable was removed, <see langword="false"/> if the project or
    /// the variable does not exist.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> or <paramref name="key"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <remarks>
    /// The name is not validated: a variable KeePassXC created under a name keypaste would refuse
    /// still has to be removable, or the vault would contain something only another tool can
    /// clear. The path is always built through <see cref="EnvConvention"/>, so this can never
    /// reach an entry outside the project's group.
    /// </remarks>
    public bool Remove(string project, string key)
    {
        return _vault.RemoveEntry(EnvConvention.EntryPath(project, key));
    }
}
