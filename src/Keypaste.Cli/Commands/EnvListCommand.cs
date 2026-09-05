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
    /// <summary>The longest name drawn. Generous: this is a listing of the reader's own vault.</summary>
    private const int DisplayLength = 512;

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
                var alteredProject = false;

                foreach (var name in store.Projects())
                {
                    var safe = EntryNameSanitizer.Sanitize(name, DisplayLength);
                    alteredProject |= safe.WasAltered;
                    context.Stdout.WriteLine(safe.Text);
                }

                Note(context, alteredProject);
                return CliApp.ExitSuccess;
            }

            if (!store.ProjectExists(project))
            {
                context.Stderr.WriteLine($"keypaste env ls: no env set for '{project}'");
                return CliApp.ExitNotFound;
            }

            var altered = false;

            foreach (var variable in store.Read(project))
            {
                var safe = EntryNameSanitizer.Sanitize(variable.Key, DisplayLength);
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
        });
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
}
