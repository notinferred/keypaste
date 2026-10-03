namespace Keypaste.Core.Tests;

/// <summary>
/// Writes a project's variables as <c>env set</c> does, protected fields of each environment's home
/// entry <c>env/&lt;project&gt;/.env</c> or <c>.env.&lt;environment&gt;</c>, for tests that need a
/// project as their fixture.
/// </summary>
internal static class ProjectVariables
{
    /// <summary>Adds or replaces a variable of a project's <c>dev</c> environment. The caller saves.</summary>
    internal static void Set(Vault vault, string project, string key, string value) =>
        Set(vault, project, EnvProfileNames.Default, key, value);

    /// <summary>Adds or replaces a variable of one environment where it lives, or on its home entry. The caller saves.</summary>
    internal static void Set(Vault vault, string project, string environment, string key, string value)
    {
        if (new EnvStore(vault).Set(project, environment, key, value).Refusal is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    /// <summary>The entry a new variable of the environment is written on.</summary>
    internal static EntryName Home(string project, string environment = EnvProfileNames.Default) =>
        EnvStore.HomeEntry(project, environment);
}
