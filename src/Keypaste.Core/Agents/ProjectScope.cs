using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Agents;

/// <summary>A project, or one environment of it, as an agent's settings name it.</summary>
/// <remarks>
/// Named as the CLI, the app, <c>clients.toml</c> and ACAF records name it: <c>billing</c> is every
/// environment of <c>billing</c> and <c>billing:staging</c> is one. It is never written as an entry's
/// tag, since the tag <c>env:billing</c> puts an entry in <c>billing</c>'s <c>dev</c> alone.
/// </remarks>
public sealed record ProjectScope
{
    private ProjectScope(string project, string? environment)
    {
        Project = project;
        Environment = environment;
    }

    /// <summary>The project.</summary>
    public string Project { get; }

    /// <summary>The one environment, or null for every environment of <see cref="Project"/>.</summary>
    public string? Environment { get; }

    /// <summary>How a person, a record and <c>clients.toml</c> name it: <c>&lt;project&gt;</c> or <c>&lt;project&gt;:&lt;environment&gt;</c>.</summary>
    public string Name => Environment is null ? Project : Project + ":" + Environment;

    /// <summary>The whole project this scope is in: itself when it names no environment.</summary>
    internal ProjectScope WholeProject => Environment is null ? this : new ProjectScope(Project, null);

    /// <summary>Builds a scope.</summary>
    /// <param name="project">The project, as <see cref="EnvConvention.IsValidProject"/> accepts it, with no ':'.</param>
    /// <param name="environment">The environment, as <see cref="EnvProfileNames.IsValid"/> accepts it, or null for every one.</param>
    /// <param name="scope">The scope, when both are valid.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether the names make a scope.</returns>
    public static bool TryCreate(string project, string? environment, [NotNullWhen(true)] out ProjectScope? scope, out string error)
    {
        ArgumentNullException.ThrowIfNull(project);

        scope = null;

        if (!EnvConvention.IsValidProject(project, out error))
        {
            return false;
        }

        if (project.Contains(':', StringComparison.Ordinal))
        {
            error = "a project name in a tag cannot contain ':'";
            return false;
        }

        if (environment is not null && !EnvProfileNames.IsValid(environment, out error))
        {
            return false;
        }

        scope = new ProjectScope(project, environment);
        error = string.Empty;
        return true;
    }

    /// <summary>Reads <see cref="Name"/>'s form, <c>&lt;project&gt;[:&lt;environment&gt;]</c>.</summary>
    /// <param name="name">The text.</param>
    /// <param name="scope">The scope, when the text names one.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether the text names a scope.</returns>
    public static bool TryParseName(string name, [NotNullWhen(true)] out ProjectScope? scope, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        var parts = name.Split(':');

        if (parts.Length > 2)
        {
            scope = null;
            error = "a project is <project> or <project>:<environment>, and a project name has no ':'";
            return false;
        }

        return TryCreate(parts[0], parts.Length == 2 ? parts[1] : null, out scope, out error);
    }
}
