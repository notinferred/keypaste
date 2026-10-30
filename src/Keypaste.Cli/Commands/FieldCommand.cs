using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Editing;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Lists, renames and removes an entry's custom fields: <c>keypaste field &lt;ls|mv|rm&gt;</c>.</summary>
/// <remarks>
/// Names and protection only; a value is read with <c>get --field</c> and written with
/// <c>set --field</c>. Renaming and removing each make a history revision, as KeePassXC's own edit
/// does, so the value is recoverable and nothing is asked first. Renaming is not one of the edits
/// sent to the app or <c>keypaste approve</c>, so it is refused while either holds the vault (D-0382).
/// </remarks>
internal static class FieldCommand
{
    private static readonly OptionSpec[] _listOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(CliJson.Option, TakesValue: false),
    ];

    private static readonly OptionSpec[] _removeOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
    ];

    private static readonly OptionSpec[] _renameOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (args.Length < 2)
        {
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        switch (args[1])
        {
            case "ls":
                return List(args, context);

            case "mv":
                return Rename(args, context);

            case "rm":
                return Remove(args, context);

            case "help":
            case "--help":
            case "-h":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste field: unknown subcommand '{args[1]}'");
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    private static int List(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _listOptions, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste field ls: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste field ls <item> [--json]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste field ls: expected exactly one item name");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste field ls: {locateError}");
            return CliApp.ExitUsageError;
        }

        var entryPath = line.Operands[0];
        var json = line.HasFlag(CliJson.Option);

        return VaultSession.Open(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste field ls: no item '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            var fields = vault.Fields(EntryName.Of(entry)) ?? [];

            if (json)
            {
                CliJson.WriteArray(context.Stdout, fields, (writer, field) =>
                {
                    writer.WriteString("name", field.Name);
                    writer.WriteBoolean("protected", field.IsProtected);
                    writer.WriteBoolean("readonly", field.IsReadOnly);
                });

                return CliApp.ExitSuccess;
            }

            if (fields.Count == 0)
            {
                context.Stderr.WriteLine($"'{Shown(entry.Path)}' has no custom fields");
                return CliApp.ExitSuccess;
            }

            var names = fields.Select(field => EntryNameSanitizer.Sanitize(field.Name)).ToList();
            var width = names.Max(name => name.Text.Length);

            for (var i = 0; i < fields.Count; i++)
            {
                var flags = fields[i].IsProtected ? "protected" : "plain";
                context.Stdout.WriteLine(
                    $"{names[i].Text.PadRight(width)}  {flags}{(fields[i].IsReadOnly ? ", read-only" : string.Empty)}");
            }

            if (names.Any(name => name.WasAltered))
            {
                context.Stderr.WriteLine("note: a displayed name is not what the vault holds. Use --json to see it.");
            }

            return CliApp.ExitSuccess;
        });
    }

    private static int Remove(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _removeOptions, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste field rm: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste field rm <item> <name>");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 2)
        {
            context.Stderr.WriteLine("keypaste field rm: expected an item name and a field name");
            return CliApp.ExitUsageError;
        }

        var (entryPath, field) = (line.Operands[0], line.Operands[1]);

        if (!FieldNameRules.IsWritable(field, out var nameError))
        {
            context.Stderr.WriteLine($"keypaste field rm: {nameError}");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste field rm: {locateError}");
            return CliApp.ExitUsageError;
        }

        var edit = new RemoveField(entryPath, field);
        var route = new OwnerRoute("field rm", _ => edit, Removed);

        return VaultSession.OpenHeld(path, line, context, route, vault =>
        {
            var result = TerminalEdits.Apply(vault, edit, EditLines.Scene(context), shown: null);
            if (result.Refusal is { } refused)
            {
                return EditLines.Refuse(context, "field rm", refused);
            }

            vault.Save();
            return Removed(result.Done!, context);
        });
    }

    private static int Rename(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _renameOptions, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste field mv: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste field mv <item> <name> <new-name>");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 3)
        {
            context.Stderr.WriteLine("keypaste field mv: expected an item name, a field name and its new name");
            return CliApp.ExitUsageError;
        }

        var (entryPath, field, newName) = (line.Operands[0], line.Operands[1], line.Operands[2]);

        if (!FieldNameRules.IsWritable(field, out var nameError) || !FieldNameRules.IsWritable(newName, out nameError))
        {
            context.Stderr.WriteLine($"keypaste field mv: {nameError}");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste field mv: {locateError}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste field mv: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            bool renamed;

            try
            {
                renamed = vault.RenameField(EntryName.Of(entry), field, newName);
            }
            catch (VaultException ex)
            {
                context.Stderr.WriteLine($"keypaste field mv: {ex.Message}");
                return CliApp.ExitUsageError;
            }

            if (!renamed)
            {
                context.Stderr.WriteLine($"keypaste field mv: '{Shown(entry.Path)}' has no field {EntryNameSanitizer.Sanitize(field).Text}");
                return CliApp.ExitNotFound;
            }

            vault.Save();

            var style = context.ConsoleStyle;
            var mark = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
            context.Stderr.WriteLine(
                $"  {mark} renamed {EntryNameSanitizer.Sanitize(field).Text} to {EntryNameSanitizer.Sanitize(newName).Text} on {Shown(entry.Path)} {style.Glyph(context.Stderr, Mark.Dot)} the old name stays in history");

            return CliApp.ExitSuccess;
        });
    }

    /// <summary>Says which field left which item, and that its value stays in history.</summary>
    internal static int Removed(EditDone done, CliContext context)
    {
        var style = context.ConsoleStyle;
        var mark = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        context.Stderr.WriteLine(
            $"  {mark} removed {EntryNameSanitizer.Sanitize(done.Names[0]).Text} from {Shown(done.Item)} {style.Glyph(context.Stderr, Mark.Dot)} its value stays in history");

        return CliApp.ExitSuccess;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste field <command>");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  ls <item>                  list custom fields by name and protection, never by value");
        writer.WriteLine("  mv <item> <name> <new>     rename a custom field; the old name stays in history");
        writer.WriteLine("  rm <item> <name>           remove a custom field; its value stays in history");
        writer.WriteLine();
        writer.WriteLine("to read one: keypaste get <item> --field <name>");
        writer.WriteLine("to set one:  keypaste set <item> --field <name> [--plain]");
    }

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;
}
