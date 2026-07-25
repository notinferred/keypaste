using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Lists env sets: <c>keypaste env ls [project]</c>.</summary>
/// <remarks>
/// Names only, never values — exactly like <c>keypaste ls</c>. Reading a value is already a verb:
/// <c>keypaste get env/&lt;project&gt;/&lt;KEY&gt; --show</c>. Adding a second way to print a
/// secret would widen the surface that has to stay honest for no new capability.
/// </remarks>
internal static class EnvListCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste env ls: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste env ls [project]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 1)
        {
            context.Stderr.WriteLine("keypaste env ls: expected at most one project name");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste env ls: {locateError}");
            return CliApp.ExitUsageError;
        }

        var project = line.Operands.Count == 1 ? line.Operands[0] : null;

        return VaultSession.Open(path, context, vault =>
        {
            var store = new EnvStore(vault);

            if (project is null)
            {
                foreach (var name in store.Projects())
                {
                    context.Stdout.WriteLine(name);
                }

                return CliApp.ExitSuccess;
            }

            if (!store.ProjectExists(project))
            {
                context.Stderr.WriteLine($"keypaste env ls: no env set for '{project}'");
                return CliApp.ExitNotFound;
            }

            foreach (var variable in store.Read(project))
            {
                context.Stdout.WriteLine(variable.Key);

                // The name is still listed: keypaste does not get to pretend the file says
                // something other than what KeePassXC shows (CORE.md law 4.6). But it cannot be
                // exported to a child process, and the place to say so is where it is seen.
                if (!variable.IsUsableName)
                {
                    context.Stderr.WriteLine(
                        $"warning: '{variable.Key}' is not a usable environment variable name");
                }
            }

            return CliApp.ExitSuccess;
        });
    }
}
