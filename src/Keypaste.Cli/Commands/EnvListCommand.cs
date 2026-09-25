using Keypaste.Cli.Output;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Lists env sets: <c>keypaste env ls [project] [-p &lt;profile&gt;] [--profiles] [--json]</c>.</summary>
/// <remarks>
/// Names only, never values — exactly like <c>keypaste ls</c>. Reading a value is already a verb:
/// <c>keypaste get env/&lt;project&gt;/&lt;KEY&gt; --show</c>. Adding a second way to print a
/// secret would widen the surface that has to stay honest for no new capability.
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

            if (project is null)
            {
                return json ? ProjectsAsJson(store, context) : Projects(store, context);
            }

            if (!store.ProjectExists(project))
            {
                context.Stderr.WriteLine($"keypaste env ls: no env set for '{project}'");
                return CliApp.ExitNotFound;
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

            return json ? KeysAsJson(vault, project, profile, context) : Keys(store, project, profile, context);
        });
    }

    private static int Projects(EnvStore store, CliContext context)
    {
        var altered = false;

        foreach (var name in store.Projects())
        {
            var safe = EntryNameSanitizer.Sanitize(name, _displayLength);
            altered |= safe.WasAltered;
            context.Stdout.WriteLine(safe.Text);
        }

        Note(context, altered);
        return CliApp.ExitSuccess;
    }

    private static int ProjectsAsJson(EnvStore store, CliContext context)
    {
        CliJson.WriteArray(context.Stdout, store.Projects(), (json, project) =>
        {
            json.WriteString("project", project);
            json.WriteStartArray("profiles");
            foreach (var profile in store.Profiles(project))
            {
                json.WriteStringValue(profile.Name);
            }

            json.WriteEndArray();
        });

        return CliApp.ExitSuccess;
    }

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

    private static int Keys(EnvStore store, string project, string profile, CliContext context)
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
        return CliApp.ExitSuccess;
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
