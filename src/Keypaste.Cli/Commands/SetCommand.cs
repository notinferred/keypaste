using Keypaste.Cli.Styling;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Creates a secret or replaces its value: <c>keypaste set &lt;entry&gt;</c>.</summary>
/// <remarks>
/// <para>
/// An existing entry keeps everything but its password, and the old password stays in its KeePass
/// history (D-0014). The value is never an argument, for the reason <c>add</c> gives: it is prompted
/// for, read as one line from stdin, or generated, and it is never printed.
/// </para>
/// <para>
/// It holds the vault's claim while it runs, so it is refused while the app or an agent holds the
/// vault instead of saving under it (D-0317).
/// </para>
/// </remarks>
internal static class SetCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        .. GenerateOption.Specs,
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste set: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste set <entry> [--vault <path>] [--keyfile <path>]");
            context.Stdout.WriteLine($"       {GenerateOption.Usage}");
            context.Stdout.WriteLine();
            context.Stdout.WriteLine("creates the entry, or replaces the password of the one already there;");
            context.Stdout.WriteLine("the old password stays in its history. The value is asked for, read");
            context.Stdout.WriteLine("from stdin, or generated, and never printed.");
            return CliApp.ExitSuccess;
        }

        if (!GenerateOption.TryRead(line, out var recipe, out var recipeError))
        {
            context.Stderr.WriteLine($"keypaste set: {recipeError}");
            return CliApp.ExitUsageError;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste set: expected exactly one entry name");
            return CliApp.ExitUsageError;
        }

        var target = line.Operands[0];
        var slash = target.LastIndexOf('/');
        var name = new EntryName(WrittenGroup.Normalize(slash < 0 ? string.Empty : target[..slash]), target[(slash + 1)..]);
        var entryPath = name.GroupPath.Length == 0 ? name.Title : name.GroupPath + "/" + name.Title;

        if (name.Title.Length == 0)
        {
            context.Stderr.WriteLine("keypaste set: the entry name cannot be empty");
            return CliApp.ExitUsageError;
        }

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            context.Stderr.WriteLine($"keypaste set: {Shown(entryPath)} is keypaste's own group; it cannot be written here");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste set: {locateError}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            var existing = vault.Find(name);

            using var secret = recipe is { } wanted ? GenerateOption.Generate(wanted) : ReadValue(context);

            if (secret is null)
            {
                return CliApp.ExitUsageError;
            }

            var value = new string(secret.Value);

            if (existing is null)
            {
                vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = value });
            }
            else
            {
                vault.UpdateEntry(existing with { Password = value });
            }

            vault.Save();

            var style = context.ConsoleStyle;
            var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
            context.Stderr.WriteLine(existing is null
                ? $"  {done} created {Shown(entryPath)}"
                : $"  {done} updated {Shown(entryPath)} {style.Glyph(context.Stderr, Mark.Dot)} the old value stays in history");

            return CliApp.ExitSuccess;
        });
    }

    /// <summary>Reads the value from a hidden prompt, twice when a person is typing, or once from stdin.</summary>
    private static SecretBuffer? ReadValue(CliContext context)
    {
        var first = context.Prompt.ReadSecret("Value: ");

        if (first is null)
        {
            context.Stderr.WriteLine("keypaste set: no value given");
            return null;
        }

        if (!context.Prompt.IsInteractive)
        {
            return first;
        }

        using var confirmation = context.Prompt.ReadSecret("Confirm value: ");

        if (confirmation is not null && first.Value.SequenceEqual(confirmation.Value))
        {
            return first;
        }

        first.Dispose();
        context.Stderr.WriteLine("keypaste set: the two values did not match; nothing was saved");
        return null;
    }

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;
}
