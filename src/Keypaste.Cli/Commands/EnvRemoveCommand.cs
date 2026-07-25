using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Removes one variable: <c>keypaste env rm &lt;project&gt; &lt;KEY&gt;</c>.</summary>
/// <remarks>
/// The path is always built through <see cref="EnvConvention"/>, so this verb cannot reach an
/// entry outside the project's group however it is called.
/// </remarks>
internal static class EnvRemoveCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("yes", TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste env rm: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste env rm <project> <KEY> [--yes]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 2)
        {
            context.Stderr.WriteLine("keypaste env rm: expected a project and a variable");
            return CliApp.ExitUsageError;
        }

        var project = line.Operands[0];
        var key = line.Operands[1];
        var assumeYes = line.HasFlag("yes");

        // Same rule as `keypaste rm`: a piped run must ask for the deletion explicitly rather than
        // have a confirmation answered by whatever the next line of stdin happens to be.
        if (!assumeYes && !context.Prompt.IsInteractive)
        {
            context.Stderr.WriteLine("keypaste env rm: --yes is required when stdin is not a terminal");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste env rm: {locateError}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.Open(path, context, vault =>
        {
            var store = new EnvStore(vault);

            if (!store.ProjectExists(project))
            {
                context.Stderr.WriteLine($"keypaste env rm: no env set for '{project}'");
                return CliApp.ExitNotFound;
            }

            var entryPath = EnvConvention.EntryPath(project, key);
            if (vault.Find(entryPath) is null)
            {
                context.Stderr.WriteLine($"keypaste env rm: '{project}' has no variable '{key}'");
                return CliApp.ExitNotFound;
            }

            if (!assumeYes)
            {
                var answer = context.Prompt.ReadLine($"Remove {entryPath}? [y/N] ");
                if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            store.Remove(project, key);
            vault.Save();

            context.Stderr.WriteLine($"Removed {entryPath}");
            return CliApp.ExitSuccess;
        });
    }
}
