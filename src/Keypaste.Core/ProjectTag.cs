namespace Keypaste.Core;

/// <summary>What one of an entry's own tags says about projects.</summary>
public enum ProjectTagKind
{
    /// <summary>Not a project tag: it does not start <c>env:</c> in any case.</summary>
    None = 0,

    /// <summary>A project tag: the entry belongs to <see cref="ProjectTag.Project"/>'s <see cref="ProjectTag.Environment"/>.</summary>
    Member = 1,

    /// <summary>Starts like a project tag and breaks its rules: reported, and adds the entry to no project.</summary>
    Malformed = 2,
}

/// <summary>
/// An entry's tag read as project membership: <c>env:&lt;project&gt;</c>, which is the project's
/// <c>dev</c> environment, or <c>env:&lt;project&gt;:&lt;environment&gt;</c>.
/// </summary>
/// <param name="Tag">The tag as the entry holds it.</param>
/// <param name="Kind">What it says.</param>
/// <param name="Project">The project, for a member.</param>
/// <param name="Environment">The environment, for a member.</param>
/// <param name="Protects">
/// Whether the entry must be asked about live every time because of this tag (D-0348). A malformed
/// tag protects when a segment after its project names a protected environment in any case, so
/// <c>env:billing:Prod</c> protects although it adds the entry to nothing.
/// </param>
/// <param name="Problem">Why a malformed tag is ignored, in words a listing prefixes; empty otherwise.</param>
/// <remarks>
/// The prefix is matched ordinally for membership. <c>Env:</c> and <c>ENV:</c> are reported as
/// malformed rather than ignored, and protect like any other malformed tag, because somebody who
/// wrote one meant a project.
/// </remarks>
public sealed record ProjectTag(string Tag, ProjectTagKind Kind, string? Project, string? Environment, bool Protects, string Problem)
{
    /// <summary>What every project tag starts with.</summary>
    public const string Prefix = "env:";

    /// <summary>Reads one tag.</summary>
    /// <param name="tag">The tag as the entry holds it.</param>
    /// <returns>What it says about projects.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    public static ProjectTag Read(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (!tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectTag(tag, ProjectTagKind.None, null, null, false, string.Empty);
        }

        var parts = tag[Prefix.Length..].Split(':');
        var protects = parts.Skip(1).Any(EnvProfileNames.IsProtected);

        ProjectTag Malformed(string problem) => new(tag, ProjectTagKind.Malformed, null, null, protects, problem);

        if (!tag.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return Malformed($"a project tag starts '{Prefix}' in lower case");
        }

        if (parts.Length > 2)
        {
            return Malformed("a project tag is env:<project> or env:<project>:<environment>, and a project name has no ':'");
        }

        if (!EnvConvention.IsValidProject(parts[0], out var projectError))
        {
            return Malformed(projectError);
        }

        if (parts.Length == 1)
        {
            return new ProjectTag(tag, ProjectTagKind.Member, parts[0], EnvProfileNames.Default, false, string.Empty);
        }

        return EnvProfileNames.IsValid(parts[1], out var environmentError)
            ? new ProjectTag(tag, ProjectTagKind.Member, parts[0], parts[1], EnvProfileNames.IsProtected(parts[1]), string.Empty)
            : Malformed(environmentError);
    }

    /// <summary>The tag that puts an entry in a project's environment, <c>dev</c> being the bare project.</summary>
    /// <param name="project">The project, which follows <see cref="EnvConvention.IsValidProject"/> and has no ':'.</param>
    /// <param name="environment">The environment, which follows <see cref="EnvProfileNames.IsValid"/>.</param>
    /// <param name="tag">The tag, when both names are valid.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether the names make a project tag.</returns>
    public static bool TryFor(string project, string environment, out string tag, out string error)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(environment);

        tag = string.Empty;

        if (!EnvConvention.IsValidProject(project, out error) || !EnvProfileNames.IsValid(environment, out error))
        {
            return false;
        }

        if (project.Contains(':', StringComparison.Ordinal))
        {
            error = "a project name in a tag cannot contain ':'";
            return false;
        }

        tag = string.Equals(environment, EnvProfileNames.Default, StringComparison.Ordinal)
            ? Prefix + project
            : Prefix + project + ":" + environment;
        return true;
    }
}
