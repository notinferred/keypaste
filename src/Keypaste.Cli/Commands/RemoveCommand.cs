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

        // Deleting a secret is irreversible, so a piped run has to say so explicitly rather than
        // have a confirmation silently answered by whatever the next line of stdin happens to be.
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
                var answer = context.Prompt.ReadLine($"Remove '{entryPath}'? [y/N] ");
                if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            vault.RemoveEntry(entryPath);
            vault.Save();

            context.Stderr.WriteLine($"Removed {entryPath}");
            return CliApp.ExitSuccess;
        });
    }
}
