namespace Keypaste.Core;

/// <summary>What importing a <c>.env</c> would do to one variable.</summary>
public enum EnvImportChange
{
    /// <summary>The project has no such variable, and it would be added.</summary>
    New = 0,

    /// <summary>The project has it with another value, which would be replaced and kept in history.</summary>
    Replaces = 1,

    /// <summary>The project already has it with the same value, and it would not be written.</summary>
    Unchanged = 2,
}

/// <summary>One variable of an import plan: its name and what would happen to it, never its value.</summary>
/// <param name="Key">The variable name.</param>
/// <param name="Change">What importing does to it.</param>
public sealed record EnvImportKey(string Key, EnvImportChange Change);

/// <summary>What importing a parsed <c>.env</c> into a project would do.</summary>
public sealed class EnvImportPlan
{
    private readonly IReadOnlyList<DotEnvVariable> _variables;

    internal EnvImportPlan(string project, string profile, IReadOnlyList<EnvImportKey> keys, IReadOnlyList<DotEnvVariable> variables, string? refusal)
    {
        Project = project;
        Profile = profile;
        Keys = keys;
        _variables = variables;
        Refusal = refusal;
    }

    /// <summary>The project imported into.</summary>
    public string Project { get; }

    /// <summary>The profile of the project imported into.</summary>
    public string Profile { get; }

    /// <summary>Every variable in the file, ordinal-sorted by name.</summary>
    public IReadOnlyList<EnvImportKey> Keys { get; }

    /// <summary>Why the file cannot be imported into this project, or null when it can.</summary>
    public string? Refusal { get; }

    /// <summary>The names that would be added, ordinal-sorted.</summary>
    public IReadOnlyList<string> Created => Named(EnvImportChange.New);

    /// <summary>The names whose value would be replaced, ordinal-sorted.</summary>
    public IReadOnlyList<string> Updated => Named(EnvImportChange.Replaces);

    /// <summary>How many variables already match the file.</summary>
    public int Unchanged => Keys.Count(key => key.Change == EnvImportChange.Unchanged);

    /// <summary>Whether importing would write anything.</summary>
    public bool WritesAnything => Refusal is null && Keys.Any(key => key.Change != EnvImportChange.Unchanged);

    internal IEnumerable<DotEnvVariable> ToWrite
    {
        get
        {
            var write = new HashSet<string>(Created.Concat(Updated), StringComparer.Ordinal);
            return _variables.Where(variable => write.Contains(variable.Key));
        }
    }

    private List<string> Named(EnvImportChange change) =>
        [.. Keys.Where(key => key.Change == change).Select(key => key.Key)];
}

/// <summary>
/// Imports a parsed <c>.env</c> into a project, all or nothing, the same way from the CLI's
/// <c>env pull</c> and the app's Env Sets screen.
/// </summary>
/// <remarks>
/// Everything that could refuse a variable is checked by <see cref="Plan(EnvStore, string, string, DotEnvDocument)"/>, before anybody is
/// asked to confirm, so a confirmed import either writes the whole plan or nothing.
/// </remarks>
public static class EnvImport
{
    /// <summary>Plans an import.</summary>
    /// <param name="store">The project's vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="document">A file <see cref="DotEnv.TryParse"/> read without problems.</param>
    /// <returns>What importing would do, or why it cannot.</returns>
    /// <exception cref="ArgumentException"><paramref name="document"/> has problems.</exception>
    /// <exception cref="VaultException">The project already contains a duplicate name.</exception>
    public static EnvImportPlan Plan(EnvStore store, string project, DotEnvDocument document) =>
        Plan(store, project, EnvProfileNames.Default, document);

    /// <summary>Plans an import into one profile of a project.</summary>
    /// <param name="store">The project's vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The profile name.</param>
    /// <param name="document">A file <see cref="DotEnv.TryParse"/> read without problems.</param>
    /// <returns>What importing would do, or why it cannot.</returns>
    /// <exception cref="ArgumentException"><paramref name="document"/> has problems.</exception>
    /// <exception cref="VaultException">The profile already contains a duplicate name.</exception>
    public static EnvImportPlan Plan(EnvStore store, string project, string profile, DotEnvDocument document)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(document);

        if (document.Problems.Count > 0)
        {
            throw new ArgumentException("a file with problems is never imported", nameof(document));
        }

        if (!EnvConvention.IsValidProject(project, out var invalid) || !EnvProfileNames.IsValid(profile, out invalid))
        {
            return new EnvImportPlan(project, profile, [], [], invalid);
        }

        if (document.Variables.FirstOrDefault(v => v.Value.StartsWith(KpReferences.Scheme, StringComparison.Ordinal)) is { } reference)
        {
            return new EnvImportPlan(project, profile, [], [],
                $"{reference.Key} holds a {KpReferences.Scheme} reference: this is a reference file ({EnvReferenceFile.FileName}); use it with `run --env-file`, not import");
        }

        var existing = store.Read(project, profile).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

        if (Collision(document.Variables, existing) is { } collision)
        {
            return new EnvImportPlan(project, profile, [], [], collision);
        }

        var keys = document.Variables
            .Select(variable => new EnvImportKey(
                variable.Key,
                !existing.TryGetValue(variable.Key, out var current) ? EnvImportChange.New
                    : string.Equals(current, variable.Value, StringComparison.Ordinal) ? EnvImportChange.Unchanged
                    : EnvImportChange.Replaces))
            .OrderBy(key => key.Key, StringComparer.Ordinal)
            .ToList();

        return new EnvImportPlan(project, profile, keys, document.Variables, null);
    }

    /// <summary>Writes every new and replaced variable of a plan. The caller must <see cref="Vault.Save"/> to persist it.</summary>
    /// <param name="store">The vault the plan was made against.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="rejection">What the store refused, when this returns false.</param>
    /// <returns>Whether every variable was written. When false, the caller must not save.</returns>
    /// <remarks>
    /// Unchanged variables are skipped rather than rewritten: <see cref="EnvStore.TrySet(string, string, string, out string)"/> cannot
    /// tell that the new value equals the old one, so it would spend one of the ten history slots
    /// KDBX keeps on a change that did not happen.
    /// </remarks>
    public static bool TryApply(EnvStore store, EnvImportPlan plan, out string rejection)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Refusal is { } refusal)
        {
            rejection = refusal;
            return false;
        }

        foreach (var variable in plan.ToWrite)
        {
            if (store.TrySet(plan.Project, plan.Profile, variable.Key, variable.Value, out rejection) == EnvSetOutcome.Rejected)
            {
                return false;
            }
        }

        rejection = string.Empty;
        return true;
    }

    /// <summary>Finds a pair of names that differ only in case, in the file or against the vault.</summary>
    /// <remarks>
    /// Two such names are two variables on Linux and one on Windows, so there is no import that
    /// means the same thing everywhere. <see cref="EnvStore.TrySet(string, string, string, out string)"/> refuses the second one
    /// anyway; catching it here means the refusal arrives before the confirmation rather than
    /// halfway through the writes.
    /// </remarks>
    private static string? Collision(IReadOnlyList<DotEnvVariable> variables, Dictionary<string, string> existing)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables)
        {
            if (seen.TryGetValue(variable.Key, out var other) &&
                !string.Equals(other, variable.Key, StringComparison.Ordinal))
            {
                return $"the file sets both '{other}' and '{variable.Key}', which differ only in case";
            }

            seen[variable.Key] = variable.Key;

            foreach (var name in existing.Keys)
            {
                if (string.Equals(name, variable.Key, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, variable.Key, StringComparison.Ordinal))
                {
                    return $"the project already has '{name}', which differs from '{variable.Key}' only in case";
                }
            }
        }

        return null;
    }
}
