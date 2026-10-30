using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Editing;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Creates a secret or replaces its value: <c>keypaste set &lt;entry&gt;</c>, or sets custom fields with <c>--field</c>.</summary>
/// <remarks>
/// <para>
/// An existing entry keeps everything but its password, and the old password stays in its KeePass
/// history (D-0014). The value is never an argument, for the reason <c>add</c> gives: it is prompted
/// for, read as one line from stdin, or generated, and it is never printed.
/// </para>
/// <para>
/// With <c>--field</c>, once or several times, it sets custom fields of an entry that already exists
/// instead, all in one edit with one revision. A new field is protected unless <c>--plain</c>; an
/// existing one keeps its protection, so <c>--plain</c> naming one is refused rather than ignored.
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
        new("field", TakesValue: true, Repeats: true),
        new("plain", TakesValue: false),
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
            context.Stdout.WriteLine("usage: keypaste set <item> [--vault <path>] [--keyfile <path>]");
            context.Stdout.WriteLine($"       {GenerateOption.Usage}");
            context.Stdout.WriteLine("       keypaste set <item> --field <name> [--field <name> ...] [--plain]");
            context.Stdout.WriteLine();
            context.Stdout.WriteLine("creates the item, or replaces the password of the one already there;");
            context.Stdout.WriteLine("the old password stays in its history. The value is asked for, read");
            context.Stdout.WriteLine("from stdin, or generated, and never printed.");
            context.Stdout.WriteLine();
            context.Stdout.WriteLine("with --field it sets custom fields of an existing item instead, asking");
            context.Stdout.WriteLine("for each value in turn. A new field is protected unless --plain; an");
            context.Stdout.WriteLine("existing one keeps its protection.");
            return CliApp.ExitSuccess;
        }

        if (!GenerateOption.TryRead(line, out var recipe, out var recipeError))
        {
            context.Stderr.WriteLine($"keypaste set: {recipeError}");
            return CliApp.ExitUsageError;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste set: expected exactly one item name");
            return CliApp.ExitUsageError;
        }

        var fields = line.Values("field");
        var plain = line.HasFlag("plain");

        if (!TryCheckFields(fields, plain, recipe is not null, out var fieldError))
        {
            context.Stderr.WriteLine($"keypaste set: {fieldError}");
            return CliApp.ExitUsageError;
        }

        var typed = EntryName.Parse(line.Operands[0]);
        var name = new EntryName(WrittenGroup.Normalize(typed.GroupPath), typed.Title);
        var entryPath = name.GroupPath.Length == 0 ? name.Title : name.GroupPath + "/" + name.Title;

        if (name.Title.Length == 0)
        {
            context.Stderr.WriteLine("keypaste set: the item name cannot be empty");
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

        TerminalEdit? Read(CliContext asked)
        {
            if (fields.Count > 0)
            {
                return ReadFields(asked, name, fields, plain);
            }

            if (recipe is { } wanted)
            {
                return new SetPassword(name, NewSecret.Generated(wanted));
            }

            using var secret = ReadValue(asked);
            return secret is null ? null : new SetPassword(name, NewSecret.Of(new string(secret.Value)));
        }

        var route = new OwnerRoute("set", Read, Done);

        return VaultSession.OpenHeld(path, line, context, route, vault =>
        {
            TerminalEdit named = fields.Count > 0
                ? new SetFields(name, [.. fields.Select(field => new FieldWrite(field, null, plain ? false : null))])
                : new SetPassword(name, null);

            var check = TerminalEdits.Check(vault, named, EditLines.Scene(context));
            if (check.Refusal is { } refused)
            {
                return EditLines.Refuse(context, "set", refused);
            }

            if (Read(context) is not { } full)
            {
                return CliApp.ExitUsageError;
            }

            var result = TerminalEdits.Apply(vault, TerminalEdits.Generate(full), EditLines.Scene(context), shown: null);
            if (result.Refusal is { } late)
            {
                return EditLines.Refuse(context, "set", late);
            }

            vault.Save();
            return Done(result.Done!, context);
        });
    }

    /// <summary>Says what was set, naming fields and never a value.</summary>
    internal static int Done(EditDone done, CliContext context)
    {
        var style = context.ConsoleStyle;
        var mark = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        var dot = style.Glyph(context.Stderr, Mark.Dot);

        context.Stderr.WriteLine(done.Outcome switch
        {
            EditOutcome.Created => $"  {mark} created {Shown(done.Item)}",
            EditOutcome.FieldsSet => $"  {mark} set {string.Join(", ", done.Names.Select(Shown))} on {Shown(done.Item)} {dot} the item's earlier state stays in history",
            _ => $"  {mark} updated {Shown(done.Item)} {dot} the old value stays in history",
        });

        return CliApp.ExitSuccess;
    }

    private static bool TryCheckFields(IReadOnlyList<string> fields, bool plain, bool generating, out string error)
    {
        error = string.Empty;

        if (fields.Count == 0)
        {
            error = plain ? "--plain applies to --field" : string.Empty;
            return !plain;
        }

        if (generating)
        {
            error = "--field values are typed or piped, not generated";
            return false;
        }

        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (!FieldNameRules.IsWritable(field, out error))
            {
                return false;
            }

            if (!named.Add(field))
            {
                error = $"--field '{field}' is given twice";
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads each field's value in turn, as the item's custom fields.</summary>
    private static SetFields? ReadFields(CliContext context, EntryName name, IReadOnlyList<string> fields, bool plain)
    {
        List<FieldWrite> writes = [];

        foreach (var field in fields)
        {
            var shown = Shown(field);
            using var secret = ReadValue(context, $"Value for {shown}: ", $"Confirm value for {shown}: ");

            if (secret is null)
            {
                return null;
            }

            writes.Add(new FieldWrite(field, new string(secret.Value), plain ? false : null));
        }

        return new SetFields(name, writes);
    }

    /// <summary>Reads the value from a hidden prompt, twice when a person is typing, or once from stdin.</summary>
    private static SecretBuffer? ReadValue(CliContext context, string prompt = "Value: ", string confirm = "Confirm value: ")
    {
        var first = context.Prompt.ReadSecret(prompt);

        if (first is null)
        {
            context.Stderr.WriteLine("keypaste set: no value given");
            return null;
        }

        if (!context.Prompt.IsInteractive)
        {
            return first;
        }

        using var confirmation = context.Prompt.ReadSecret(confirm);

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
