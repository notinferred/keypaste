namespace Keypaste.Core;

/// <summary>
/// Imports a parsed <c>.env</c> into a project's environment, all or nothing, the same way from the
/// CLI's <c>env pull</c> and the app's Env profiles screen.
/// </summary>
/// <remarks>
/// Everything that could refuse a variable is checked by <see cref="Plan(EnvStore, string, string, DotEnvDocument, EntryName)"/>,
/// before anybody is asked to confirm, so a confirmed import either writes the whole plan through
/// <see cref="EnvStore.TryApply"/> or nothing.
/// </remarks>
public static class EnvImport
{
    /// <summary>Plans an import into a project's <c>dev</c> environment.</summary>
    /// <param name="store">The project's vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="document">A file <see cref="DotEnv.TryParse"/> read without problems.</param>
    /// <returns>What importing would do, or why it cannot.</returns>
    /// <exception cref="ArgumentException"><paramref name="document"/> has problems.</exception>
    public static EnvWritePlan Plan(EnvStore store, string project, DotEnvDocument document) =>
        Plan(store, project, EnvProfileNames.Default, document);

    /// <summary>Plans an import into one environment of a project.</summary>
    /// <param name="store">The project's vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The environment's name.</param>
    /// <param name="document">A file <see cref="DotEnv.TryParse"/> read without problems.</param>
    /// <param name="entry">The entry the file's new keys go on, which must be tagged into the environment; null for the home entry.</param>
    /// <returns>What importing would do, or why it cannot.</returns>
    /// <exception cref="ArgumentException"><paramref name="document"/> has problems.</exception>
    public static EnvWritePlan Plan(EnvStore store, string project, string profile, DotEnvDocument document, EntryName? entry = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(document);

        if (document.Problems.Count > 0)
        {
            throw new ArgumentException("a file with problems is never imported", nameof(document));
        }

        if (!ProjectTag.TryFor(project, profile, out _, out var invalid))
        {
            return EnvWritePlan.Refused(project, profile, invalid);
        }

        if (document.Variables.FirstOrDefault(v => v.Value.StartsWith(KpReferences.Scheme, StringComparison.Ordinal)) is { } reference)
        {
            return EnvWritePlan.Refused(project, profile,
                $"{reference.Key} holds a {KpReferences.Scheme} reference: this is a reference file ({EnvReferenceFile.FileName}); use it with `run --env-file`, not import");
        }

        if (Collision(document.Variables) is { } collision)
        {
            return EnvWritePlan.Refused(project, profile, collision);
        }

        return store.Plan(project, profile, [.. document.Variables.Select(variable => KeyValuePair.Create(variable.Key, variable.Value))], entry);
    }

    /// <summary>Finds a pair of names in the file that differ only in case.</summary>
    /// <remarks>
    /// Two such names are two variables on Linux and one on Windows, so there is no import that
    /// means the same thing everywhere. <see cref="EnvStore.Plan"/> refuses one that collides with
    /// the vault the same way.
    /// </remarks>
    private static string? Collision(IReadOnlyList<DotEnvVariable> variables)
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
        }

        return null;
    }
}
