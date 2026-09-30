namespace Keypaste.Core;

/// <summary>One environment of a project, as a listing shows it.</summary>
/// <param name="Name">The environment's name.</param>
/// <param name="IsProtected">Whether every release through a session is asked about live (<see cref="EnvProfileNames.IsProtected"/>).</param>
/// <param name="Members">The entries whose own tag puts them in it, in ordinal order of path.</param>
public sealed record ProjectEnvironment(string Name, bool IsProtected, IReadOnlyList<EntryName> Members);

/// <summary>One project: the entries tagged for it, the legacy <c>env/&lt;project&gt;</c> group, or both.</summary>
/// <param name="Name">The project's name.</param>
/// <param name="IsLegacy">Whether it has an <c>env/&lt;project&gt;</c> group, the one-entry-per-variable layout of earlier releases.</param>
/// <param name="Environments">Its environments, <c>dev</c> first and protected ones last, as <see cref="EnvStore.Profiles"/> orders them.</param>
public sealed record ProjectListing(string Name, bool IsLegacy, IReadOnlyList<ProjectEnvironment> Environments);

/// <summary>A tag that starts like a project tag and is ignored.</summary>
/// <param name="Entry">The entry carrying it.</param>
/// <param name="Tag">The tag.</param>
/// <param name="Problem">Why it adds the entry to no project.</param>
/// <param name="Protects">Whether it still makes the entry's releases ask live (<see cref="ProjectTag.Protects"/>).</param>
public sealed record ProjectTagProblem(EntryName Entry, string Tag, string Problem, bool Protects);

/// <summary>
/// Every project in a vault: the tag projects together with the legacy <c>env/</c> groups (D-0367).
/// </summary>
/// <remarks>
/// Only an entry's own tags count; a group's tags are never read. Entries in the recycle bin and in
/// keypaste's own groups belong to no project. What this lists is membership, not variables: which
/// fields of a member leave for a child is <see cref="EnvResolution"/>'s.
/// </remarks>
public sealed class ProjectCatalog
{
    private ProjectCatalog(IReadOnlyList<ProjectListing> projects, IReadOnlyList<ProjectTagProblem> problems)
    {
        Projects = projects;
        Problems = problems;
    }

    /// <summary>The projects, in ordinal order of name.</summary>
    public IReadOnlyList<ProjectListing> Projects { get; }

    /// <summary>The malformed project tags, in ordinal order of entry path and tag.</summary>
    public IReadOnlyList<ProjectTagProblem> Problems { get; }

    /// <summary>Reads the projects an open vault holds.</summary>
    /// <param name="vault">The vault.</param>
    /// <returns>Its projects and the tags it ignored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vault"/> is null.</exception>
    public static ProjectCatalog Read(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        Dictionary<string, Dictionary<string, List<EntryName>>> tagged = new(StringComparer.Ordinal);
        List<ProjectTagProblem> problems = [];

        foreach (var (name, tags) in vault.ReadTags())
        {
            if (ReservedGroups.IsReserved(name.GroupPath))
            {
                continue;
            }

            foreach (var tag in tags.Select(ProjectTag.Read))
            {
                if (tag.Kind == ProjectTagKind.Malformed)
                {
                    problems.Add(new ProjectTagProblem(name, tag.Tag, tag.Problem, tag.Protects));
                }
                else if (tag.Kind == ProjectTagKind.Member)
                {
                    var environments = tagged.TryGetValue(tag.Project!, out var found) ? found : tagged[tag.Project!] = new(StringComparer.Ordinal);
                    (environments.TryGetValue(tag.Environment!, out var members) ? members : environments[tag.Environment!] = []).Add(name);
                }
            }
        }

        var store = new EnvStore(vault);
        var legacy = store.Projects();

        var projects = legacy.Union(tagged.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(project =>
            {
                var members = tagged.TryGetValue(project, out var found) ? found : [];
                var names = store.Profiles(project).Select(profile => profile.Name).Union(members.Keys, StringComparer.Ordinal);

                return new ProjectListing(
                    project,
                    legacy.Contains(project, StringComparer.Ordinal),
                    [
                        .. names
                            .Select(environment => new ProjectEnvironment(
                                environment,
                                EnvProfileNames.IsProtected(environment),
                                members.TryGetValue(environment, out var entries)
                                    ? [.. entries.Distinct().OrderBy(entry => Path(entry), StringComparer.Ordinal)]
                                    : []))
                            .OrderBy(environment => !string.Equals(environment.Name, EnvProfileNames.Default, StringComparison.Ordinal))
                            .ThenBy(environment => environment.IsProtected)
                            .ThenBy(environment => environment.Name, StringComparer.Ordinal),
                    ]);
            });

        return new ProjectCatalog(
            [.. projects],
            [.. problems.OrderBy(problem => Path(problem.Entry), StringComparer.Ordinal).ThenBy(problem => problem.Tag, StringComparer.Ordinal)]);
    }

    private static string Path(EntryName entry) => entry.GroupPath.Length == 0 ? entry.Title : entry.GroupPath + "/" + entry.Title;
}
