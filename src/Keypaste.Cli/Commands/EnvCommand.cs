namespace Keypaste.Cli.Commands;

/// <summary>Dispatches the environment-variable subcommands: <c>keypaste env &lt;ls|set|rm&gt;</c>.</summary>
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

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste env <command>");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  ls [project]           list projects, or one project's variable names");
        writer.WriteLine("  set <project> <KEY>    set a variable, prompting for the value");
        writer.WriteLine("  rm <project> <KEY>     remove a variable");
        writer.WriteLine("  pull <project> [file]  import a .env file, then offer to delete it");
        writer.WriteLine();
        writer.WriteLine($"variables live in the '{Core.EnvConvention.RootGroup}/<project>' group of the vault,");
        writer.WriteLine("one entry per variable, and stay fully editable in KeePassXC.");
        writer.WriteLine("to read a value: keypaste get env/<project>/<KEY> --show");
    }
}
