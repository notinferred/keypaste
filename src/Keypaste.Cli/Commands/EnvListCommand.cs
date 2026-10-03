using Keypaste.Cli.Output;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Lists projects and env sets: <c>keypaste env ls [project] [-p &lt;profile&gt;] [--profiles] [--json]</c>.</summary>
/// <remarks>
/// <para>
/// Names only, never values — exactly like <c>keypaste ls</c>. Reading a value is already a verb:
/// <c>keypaste get &lt;entry&gt; --field &lt;KEY&gt; --show</c>. Adding a second way to print a
/// secret would widen the surface that has to stay honest for no new capability.
/// </para>
/// <para>
/// Without a project it lists every project (<see cref="ProjectCatalog"/>): its environments, the
/// protected ones marked, and the entries tagged into each. A tag that starts like a project tag
/// and breaks the grammar is a warning. With a project it lists its environments, or one with
/// <c>-p</c>, their entries and each entry's variables.
/// </para>
/// </remarks>
internal static class EnvListCommand
{
    /// <summary>The longest name drawn. Generous: this is a listing of the reader's own vault.</summary>
    private const int _displayLength = 512;

    private const string _profilesOption = "profiles";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        EnvCommand.ProfileOption,
        new(_profilesOption, TakesValue: false),
        new(CliJson.Option, TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            return Fail(context, error);
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste env ls [project] [-p <profile>] [--profiles] [--json]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 1)
        {
            return Fail(context, "expected at most one project name");
        }

        if (!EnvCommand.TryProfile(line, out _, out var profileError))
        {
            return Fail(context, profileError);
        }

        var project = line.Operands.Count == 1 ? line.Operands[0] : null;
        var profiles = line.HasFlag(_profilesOption);
        var json = line.HasFlag(CliJson.Option);

        if (project is null && (profiles || line.Value(EnvCommand.ProfileOption.Name) is not null))
        {
            return Fail(context, "-p and --profiles are about one project; name it");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, locateError);
        }

        return VaultSession.Open(path, line, context, vault =>
        {
            var catalog = ProjectCatalog.Read(vault);

            if (project is null)
            {
                Warn(catalog.Problems, context);
                return json ? ProjectsAsJson(catalog.Projects, context) : Projects(catalog.Projects, context);
            }

            if (catalog.Projects.FirstOrDefault(listed => string.Equals(listed.Name, project, StringComparison.Ordinal)) is not { } listing)
            {
                context.Stderr.WriteLine($"keypaste env ls: no env set for '{project}'");
                return CliApp.ExitNotFound;
            }

            return Project(listing, Keys(vault, listing), line.Value(EnvCommand.ProfileOption.Name), profiles, json, context);
        });
    }

    private static int Projects(IReadOnlyList<ProjectListing> projects, CliContext context)
    {
        var altered = false;

        foreach (var project in projects)
        {
            var safe = EntryNameSanitizer.Sanitize(project.Name, _displayLength);
            altered |= safe.WasAltered;
            context.Stdout.WriteLine(safe.Text);
            altered |= Environments(project.Environments, context);
        }

        Note(context, altered);
        return CliApp.ExitSuccess;
    }

    /// <summary>One project: its environments, their entries and each entry's variables, or one environment's with <c>-p</c>.</summary>
    private static int Project(ProjectListing listing, ILookup<(string, EntryName), string> keys, string? profile, bool profiles, bool json, CliContext context)
    {
        var environments = listing.Environments
            .Where(environment => profile is null || string.Equals(environment.Name, profile, StringComparison.Ordinal))
            .ToList();

        if (environments.Count == 0)
        {
            context.Stderr.WriteLine($"keypaste env ls: '{listing.Name}' has no '{profile}' environment");
            return CliApp.ExitNotFound;
        }

        if (profiles)
        {
            if (json)
            {
                CliJson.WriteArray(context.Stdout, environments, (writer, environment) =>
                {
                    writer.WriteString("profile", environment.Name);
                    writer.WriteBoolean("protected", environment.IsProtected);
                });
            }
            else
            {
                foreach (var environment in environments)
                {
                    context.Stdout.WriteLine(environment.Name);
                }
            }

            return CliApp.ExitSuccess;
        }

        if (json)
        {
            return ProjectsAsJson([listing with { Environments = environments }], context, keys);
        }

        Note(context, Environments(environments, context, keys));
        return CliApp.ExitSuccess;
    }

    /// <summary>The variable fields each tagged entry gives each environment of a project, by environment and entry; names only.</summary>
    private static ILookup<(string, EntryName), string> Keys(Vault vault, ProjectListing listing) =>
        listing.Environments
            .SelectMany(environment => EnvResolution.List(vault, listing.Name, environment.Name).Sources
                .Select(source => (Place: (environment.Name, source.Entry), Name: source.Key)))
            .ToLookup(pair => pair.Place, pair => pair.Name);

    /// <summary>Writes each environment, indented, with its tagged entries beneath it and, when given, each entry's variables beneath that.</summary>
    /// <returns>Whether any drawn name is not what the vault holds.</returns>
    private static bool Environments(IEnumerable<ProjectEnvironment> environments, CliContext context, ILookup<(string, EntryName), string>? keys = null)
    {
        var altered = false;

        foreach (var environment in environments)
        {
            context.Stdout.WriteLine(environment.IsProtected ? $"  {environment.Name}  protected" : $"  {environment.Name}");

            foreach (var member in environment.Members)
            {
                var safe = EntryNameSanitizer.SanitizePath(Path(member), maximumLength: _displayLength);
                altered |= safe.WasAltered;
                context.Stdout.WriteLine("    " + safe.Text);

                foreach (var key in keys?[(environment.Name, member)] ?? [])
                {
                    var safeKey = EntryNameSanitizer.Sanitize(key, _displayLength);
                    altered |= safeKey.WasAltered;
                    context.Stdout.WriteLine("      " + safeKey.Text);
                }
            }
        }

        return altered;
    }

    private static int ProjectsAsJson(IReadOnlyList<ProjectListing> projects, CliContext context, ILookup<(string, EntryName), string>? keys = null)
    {
        CliJson.WriteArray(context.Stdout, projects, (json, project) =>
        {
            json.WriteString("project", project.Name);
            json.WriteStartArray("environments");

            foreach (var environment in project.Environments)
            {
                json.WriteStartObject();
                json.WriteString("name", environment.Name);
                json.WriteBoolean("protected", environment.IsProtected);
                json.WriteStartArray("members");

                foreach (var member in environment.Members)
                {
                    json.WriteStartObject();
                    json.WriteString("path", Path(member));
                    json.WriteString("group", member.GroupPath);
                    json.WriteString("title", member.Title);

                    if (keys is not null)
                    {
                        json.WriteStartArray("keys");

                        foreach (var key in keys[(environment.Name, member)])
                        {
                            json.WriteStringValue(key);
                        }

                        json.WriteEndArray();
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });

        return CliApp.ExitSuccess;
    }

    /// <summary>Names each tag that starts like a project tag and is ignored, and whether it still protects its entry.</summary>
    private static void Warn(IReadOnlyList<ProjectTagProblem> problems, CliContext context)
    {
        foreach (var problem in problems)
        {
            var sentence = $"'{Path(problem.Entry)}' has the tag '{problem.Tag}', which puts it in no project: {problem.Problem}"
                + (problem.Protects ? "; every release of the entry is still asked about live" : string.Empty);
            context.Stderr.WriteLine($"warning: {EntryNameSanitizer.SanitizeProse(sentence, _displayLength).Text}");
        }
    }

    private static string Path(EntryName entry) => entry.GroupPath.Length == 0 ? entry.Title : entry.GroupPath + "/" + entry.Title;

    /// <summary>Says on stderr that at least one drawn name is not the name the vault holds.</summary>
    /// <remarks>
    /// stderr rather than stdout, and no mark in the listing, because this output is parsed:
    /// <c>scripts/verify-keepassxc-writeback.sh</c> compares it against what KeePassXC reports. A
    /// parser does not read stderr, and a person does.
    /// </remarks>
    private static void Note(CliContext context, bool altered)
    {
        if (altered)
        {
            context.Stderr.WriteLine("note: a displayed name is not what the vault holds.");
        }
    }

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste env ls: {message}");
        return CliApp.ExitUsageError;
    }
}
