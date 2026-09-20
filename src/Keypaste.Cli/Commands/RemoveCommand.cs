using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Removes an entry: <c>keypaste rm &lt;entry&gt;</c>.</summary>
internal static class RemoveCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("yes", TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste rm: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste rm <entry> [--yes]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste rm: expected exactly one entry name");
            return CliApp.ExitUsageError;
        }

        var entryPath = line.Operands[0];
        var assumeYes = line.HasFlag("yes");

        // A delete is not something to have answered by whatever the next line of stdin happens
        // to be, so a piped run says so explicitly. In a vault with no recycle bin it is still
        // irreversible; in one with a bin it is two thirds of the way there.
        if (!assumeYes && !context.Prompt.IsInteractive)
        {
            context.Stderr.WriteLine("keypaste rm: --yes is required when stdin is not a terminal");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste rm: {locateError}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.Open(path, context, vault =>
        {
            if (vault.Find(entryPath) is null)
            {
                var isGroup = false;
                foreach (var group in vault.ReadGroupPaths())
                {
                    if (string.Equals(group, entryPath, StringComparison.Ordinal))
                    {
                        isGroup = true;
                        break;
                    }
                }

                context.Stderr.WriteLine(isGroup
                    ? $"keypaste rm: '{entryPath}' is a group; only entries can be removed"
                    : $"keypaste rm: no entry '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            if (!assumeYes)
            {
                // Asked before the act, so it has to come from the vault rather than from what
                // keypaste would prefer to be true: one vault has a recycle bin and another does
                // not, and only one of those deletes can be undone.
                var answer = context.Prompt.ReadLine(vault.RecyclesDeletedEntries
                    ? $"Move '{entryPath}' to the recycle bin? [y/N] "
                    : $"Remove '{entryPath}'? This vault has no recycle bin. [y/N] ");
                if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            // Nothing removed means nothing to save. Something wrote to the file between the
            // check above and here, and the honest answer is that this run did not do it.
            var outcome = vault.RemoveEntry(entryPath);
            if (outcome == DeletionOutcome.NothingMatched)
            {
                context.Stderr.WriteLine(
                    $"keypaste rm: '{entryPath}' was not removed; the vault is unchanged");
                return CliApp.ExitNotFound;
            }

            vault.Save();

            // Reported after the act, so a vault edited between the question and the answer
            // cannot make this line wrong.
            context.Stderr.WriteLine(outcome == DeletionOutcome.Recycled
                ? $"Moved {entryPath} to the recycle bin"
                : $"Removed {entryPath}");
            return CliApp.ExitSuccess;
        });
    }
}
