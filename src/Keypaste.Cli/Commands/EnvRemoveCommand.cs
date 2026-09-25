using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Removes one variable: <c>keypaste env rm &lt;project&gt; &lt;KEY&gt; [-p &lt;profile&gt;]</c>.</summary>
/// <remarks>
/// The variable is addressed by its group and its title, never by the two joined, so this verb
/// cannot reach an entry outside the project's group however it is called.
/// </remarks>
internal static class EnvRemoveCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("yes", TakesValue: false),
        EnvCommand.ProfileOption,
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
            context.Stdout.WriteLine("usage: keypaste env rm <project> <KEY> [-p <profile>] [--yes]");
            return CliApp.ExitSuccess;
        }

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            context.Stderr.WriteLine($"keypaste env rm: {profileError}");
            return CliApp.ExitUsageError;
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

        return VaultSession.Open(path, line, context, vault =>
        {
            var store = new EnvStore(vault);

            if (!store.ProjectExists(project))
            {
                context.Stderr.WriteLine($"keypaste env rm: no env set for '{project}'");
                return CliApp.ExitNotFound;
            }

            if (!store.ProfileExists(project, profile))
            {
                context.Stderr.WriteLine($"keypaste env rm: '{project}' has no '{profile}' profile");
                return CliApp.ExitNotFound;
            }

            var name = new EntryName(EnvProfileNames.GroupPath(project, profile), key);
            var entryPath = EnvCommand.EntryPath(project, profile, key);

            if (vault.Find(name) is null)
            {
                context.Stderr.WriteLine(string.Equals(profile, EnvProfileNames.Default, StringComparison.Ordinal)
                    ? $"keypaste env rm: '{project}' has no variable '{key}'"
                    : $"keypaste env rm: '{project}' has no variable '{key}' in its '{profile}' profile");
                return CliApp.ExitNotFound;
            }

            if (!assumeYes)
            {
                var answer = context.Prompt.ReadLine(vault.RecyclesDeletedEntries
                    ? $"Move {entryPath} to the recycle bin? [y/N] "
                    : $"Remove {entryPath}? This vault has no recycle bin. [y/N] ");
                if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            // Nothing removed means nothing to save. Something wrote to the file between the
            // check above and here, and the honest answer is that this run did not do it.
            var outcome = store.Remove(project, profile, key);
            if (outcome == DeletionOutcome.NothingMatched)
            {
                context.Stderr.WriteLine(
                    $"keypaste env rm: '{entryPath}' was not removed; the vault is unchanged");
                return CliApp.ExitNotFound;
            }

            vault.Save();

            context.Stderr.WriteLine(outcome == DeletionOutcome.Recycled
                ? $"Moved {entryPath} to the recycle bin"
                : $"Removed {entryPath}");
            return CliApp.ExitSuccess;
        });
    }
}
