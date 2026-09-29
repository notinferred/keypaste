using Keypaste.Cli.Output;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Lists projects and env sets: <c>keypaste env ls [project] [-p &lt;profile&gt;] [--profiles] [--json]</c>.</summary>
/// <remarks>
/// <para>
/// Names only, never values — exactly like <c>keypaste ls</c>. Reading a value is already a verb:
/// <c>keypaste get env/&lt;project&gt;/&lt;KEY&gt; --show</c>. Adding a second way to print a
/// secret would widen the surface that has to stay honest for no new capability.
/// </para>
/// <para>
/// Without a project it lists every project (<see cref="ProjectCatalog"/>): its environments, the
/// protected ones marked, and the entries tagged into each, with a project that has an
/// <c>env/&lt;project&gt;</c> group marked legacy. A tag that starts like a project tag and breaks
/// the grammar is a warning. With a project that has a legacy group it lists that profile's
/// variable names as before, followed by any tagged entries; a project known only from tags lists
/// its environments and entries.
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

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
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
            var store = new EnvStore(vault);
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

            if (!listing.IsLegacy)
            {
                return Tagged(listing, line.Value(EnvCommand.ProfileOption.Name), profiles, json, context);
            }

            if (profiles)
            {
                return Profiles(store, project, json, context);
            }

            if (!store.ProfileExists(project, profile))
            {
                context.Stderr.WriteLine($"keypaste env ls: '{project}' has no '{profile}' profile");
                return CliApp.ExitNotFound;
            }

            if (json)
            {
                return KeysAsJson(vault, project, profile, context);
            }

            Keys(store, project, profile, context);

            if (listing.Environments.Any(environment => environment.Members.Count > 0))
            {
                context.Stdout.WriteLine();
                context.Stdout.WriteLine("tagged entries");
                Note(context, Environments(listing.Environments.Where(environment => environment.Members.Count > 0), context));
            }

            return CliApp.ExitSuccess;
        });
    }

    private static int Projects(IReadOnlyList<ProjectListing> projects, CliContext context)
    {
        var altered = false;

        foreach (var project in projects)
        {
            var safe = EntryNameSanitizer.Sanitize(project.Name, _displayLength);
            altered |= safe.WasAltered;
            context.Stdout.WriteLine(project.IsLegacy ? safe.Text + "  legacy" : safe.Text);
            altered |= Environments(project.Environments, context);
        }

        Note(context, altered);
        return CliApp.ExitSuccess;
    }

    /// <summary>A project known only from tags: its environments and their entries, or one environment's with <c>-p</c>.</summary>
    private static int Tagged(ProjectListing listing, string? profile, bool profiles, bool json, CliContext context)
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
            return ProjectsAsJson([listing with { Environments = environments }], context);
        }

        Note(context, Environments(environments, context));
        return CliApp.ExitSuccess;
    }

    /// <summary>Writes each environment, indented, with its tagged entries beneath it.</summary>
    /// <returns>Whether any drawn name is not what the vault holds.</returns>
    private static bool Environments(IEnumerable<ProjectEnvironment> environments, CliContext context)
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
            }
        }

        return altered;
    }

    private static int ProjectsAsJson(IReadOnlyList<ProjectListing> projects, CliContext context)
    {
        CliJson.WriteArray(context.Stdout, projects, (json, project) =>
        {
            json.WriteString("project", project.Name);
            json.WriteBoolean("legacy", project.IsLegacy);
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

    private static int Profiles(EnvStore store, string project, bool json, CliContext context)
    {
        var profiles = store.Profiles(project);

        if (json)
        {
            CliJson.WriteArray(context.Stdout, profiles, (writer, profile) =>
            {
                writer.WriteString("profile", profile.Name);
                writer.WriteBoolean("protected", profile.IsProtected);
            });
        }
        else
        {
            foreach (var profile in profiles)
            {
                context.Stdout.WriteLine(profile.Name);
            }
        }

        foreach (var problem in store.ProfileProblems(project))
        {
            context.Stderr.WriteLine($"warning: {EntryNameSanitizer.SanitizeProse(problem, _displayLength).Text}");
        }

        return CliApp.ExitSuccess;
    }

    private static void Keys(EnvStore store, string project, string profile, CliContext context)
    {
        var altered = false;

        foreach (var variable in store.Read(project, profile))
        {
            var safe = EntryNameSanitizer.Sanitize(variable.Key, _displayLength);
            altered |= safe.WasAltered;

            context.Stdout.WriteLine(safe.Text);

            // The name is still listed: keypaste does not get to pretend the file says
            // something other than what KeePassXC shows (docs/PRODUCT.md law 4.6). But it cannot be
            // exported to a child process, and the place to say so is where it is seen.
            if (!variable.IsUsableName)
            {
                context.Stderr.WriteLine(
                    $"warning: '{safe.Text}' is not a usable environment variable name");
            }
        }

        Note(context, altered);
    }

    private static int KeysAsJson(Vault vault, string project, string profile, CliContext context)
    {
        var matrix = EnvMatrix.Build(vault, project, context.Clock);
        var column = matrix.Profiles.Select(info => info.Name).ToList().IndexOf(profile);
        var rows = matrix.Rows.Where(row => row.Cells[column].State != EnvCellState.Missing);

        CliJson.WriteArray(context.Stdout, rows, (json, row) =>
        {
            json.WriteString("key", row.Key);
            json.WriteString("profile", profile);
            json.WriteBoolean("usable", row.Cells[column].State == EnvCellState.Set);
        });

        return CliApp.ExitSuccess;
    }

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
