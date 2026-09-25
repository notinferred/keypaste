using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Projects;

namespace Keypaste.Cli.Commands;

/// <summary>Dispatches the environment-variable subcommands: <c>keypaste env &lt;subcommand&gt;</c>.</summary>
/// <remarks>
/// The first verb group in the CLI. Subcommands parse their arguments from index 2, which
/// <see cref="CommandLine.TryParse"/> already supports, so grouping costs the parser nothing.
/// </remarks>
internal static class EnvCommand
{
    internal static int Execute(string[] args, CliContext context)
    {
        if (args.Length < 2)
        {
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        var subcommand = args[1];

        switch (subcommand)
        {
            case "ls":
                return EnvListCommand.Execute(args, context);

            case "set":
                return EnvSetCommand.Execute(args, context);

            case "rm":
                return EnvRemoveCommand.Execute(args, context);

            case "pull":
                return EnvPullCommand.Execute(args, context);

            case "export":
                return EnvExportCommand.Execute(args, context);

            case "diff":
                return EnvDiffCommand.Execute(args, context);

            // Handled here rather than left to the subcommand parsers: with no subcommand to
            // dispatch on, `keypaste env --help` would otherwise be reported as an unknown one.
            case "help":
            case "--help":
            case "-h":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste env: unknown subcommand '{subcommand}'");
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    /// <summary>The option every env verb takes to name a profile.</summary>
    internal static readonly OptionSpec ProfileOption = new("profile", TakesValue: true, Short: 'p');

    /// <summary>The profile a command line names, <c>dev</c> when it names none.</summary>
    /// <returns>False with the reason when the name is not one keypaste resolves.</returns>
    internal static bool TryProfile(CommandLine line, out string profile, out string error)
    {
        profile = line.Value(ProfileOption.Name) ?? EnvProfileNames.Default;
        return EnvProfileNames.IsValid(profile, out error);
    }

    /// <summary>The project <c>projects.json</c> maps the working directory to, for one vault.</summary>
    /// <param name="context">Where keypaste's home and the working directory come from.</param>
    /// <param name="vaultPath">The vault in use.</param>
    /// <param name="project">The project, when this returns true.</param>
    /// <param name="error">Why none, in words the verb prefixes.</param>
    /// <param name="notMapped">What to add when no mapping covers the directory.</param>
    /// <returns>False when there is not exactly one.</returns>
    internal static bool TryInferProject(
        CliContext context,
        string vaultPath,
        [NotNullWhen(true)] out string? project,
        out string error,
        string notMapped = NameAProject)
    {
        project = null;
        var path = KeypasteHome.ProjectsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (!ProjectMappings.TryLoad(path, out var mappings))
        {
            error = $"{path} could not be read, so no project could be found for this directory; name one";
            return false;
        }

        if (ProjectInference.TryInfer(mappings, vaultPath, context.WorkingDirectory, out project, out error))
        {
            return true;
        }

        if (string.Equals(error, ProjectInference.NotMapped, StringComparison.Ordinal))
        {
            error = $"{error}; {notMapped}";
        }

        return false;
    }

    /// <summary>What a verb that could not infer a project asks for.</summary>
    internal const string NameAProject = "name a project, as in: keypaste run -p dev acme-api -- npm start";

    /// <summary>How a command names where a variable of a profile lives.</summary>
    internal static string EntryPath(string project, string profile, string key) =>
        EnvProfileNames.GroupPath(project, profile) + "/" + key;

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste env <command> [-p <profile>]");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  ls [project]             list projects, or one profile's variable names");
        writer.WriteLine("  set <project> <KEY>      set a variable, prompting for the value");
        writer.WriteLine("  rm <project> <KEY>       remove a variable");
        writer.WriteLine("  pull <project> [file]    import a .env file, then offer to delete it");
        writer.WriteLine("  export [project] [file]  write kp:// references, safe to commit; --dotenv for plain text");
        writer.WriteLine("  diff [project] [a b]     compare profiles by name, never by value");
        writer.WriteLine();
        writer.WriteLine($"variables live in the '{EnvConvention.RootGroup}/<project>' group of the vault, which is");
        writer.WriteLine($"the {EnvProfileNames.Default} profile; -p <profile> uses the '{EnvConvention.RootGroup}/<project>/<profile>' group.");
        writer.WriteLine("one entry per variable, and fully editable in KeePassXC.");
        writer.WriteLine("to read a value: keypaste get env/<project>/<KEY> --show");
    }
}
