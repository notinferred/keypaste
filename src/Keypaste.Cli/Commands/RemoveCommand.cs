using Keypaste.Core.Editing;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Removes an entry: <c>keypaste rm &lt;entry&gt;</c>.</summary>
internal static class RemoveCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
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
            context.Stdout.WriteLine("usage: keypaste rm <item> [--yes]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste rm: expected exactly one item name");
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

        var edit = new RemoveItem(entryPath);

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            var check = TerminalEdits.Check(vault, edit, EditLines.Scene(context));
            if (check.Refusal is { } refused)
            {
                return EditLines.Refuse(context, "rm", refused);
            }

            if (!assumeYes)
            {
                // Asked before the act, so it has to come from the vault rather than from what
                // keypaste would prefer to be true: one vault has a recycle bin and another does
                // not, and only one of those deletes can be undone.
                var answer = context.Prompt.ReadLine(check.Preview!.Recycles
                    ? $"Move '{entryPath}' to the recycle bin? [y/N] "
                    : $"Remove '{entryPath}'? This vault has no recycle bin. [y/N] ");
                if (!EditLines.Confirmed(answer))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            var result = TerminalEdits.Apply(vault, edit, EditLines.Scene(context), shown: null);
            if (result.Refusal is { } late)
            {
                return EditLines.Refuse(context, "rm", late);
            }

            vault.Save();
            return Done(result.Done!, context);
        });
    }

    /// <summary>Says where the item went, after the act, so a vault edited between the question and the answer cannot make it wrong.</summary>
    internal static int Done(EditDone done, CliContext context)
    {
        context.Stderr.WriteLine(done.Outcome == EditOutcome.Recycled
            ? $"Moved {done.Item} to the recycle bin"
            : $"Removed {done.Item}");
        return CliApp.ExitSuccess;
    }
}
