using Keypaste.Cli.Styling;
using Keypaste.Core;

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
            context.Stdout.WriteLine("usage: keypaste set <entry> [--vault <path>] [--keyfile <path>]");
            context.Stdout.WriteLine($"       {GenerateOption.Usage}");
            context.Stdout.WriteLine("       keypaste set <entry> --field <name> [--field <name> ...] [--plain]");
            context.Stdout.WriteLine();
            context.Stdout.WriteLine("creates the entry, or replaces the password of the one already there;");
            context.Stdout.WriteLine("the old password stays in its history. The value is asked for, read");
            context.Stdout.WriteLine("from stdin, or generated, and never printed.");
            context.Stdout.WriteLine();
            context.Stdout.WriteLine("with --field it sets custom fields of an existing entry instead, asking");
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
            context.Stderr.WriteLine("keypaste set: expected exactly one entry name");
            return CliApp.ExitUsageError;
        }

        var fields = line.Values("field");
        var plain = line.HasFlag("plain");

        if (!TryCheckFields(fields, plain, recipe is not null, out var fieldError))
        {
            context.Stderr.WriteLine($"keypaste set: {fieldError}");
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
            if (fields.Count > 0)
            {
                return SetFields(vault, name, entryPath, fields, plain, context);
            }

            var existing = vault.Find(name);

            if (existing is null && !EnvNameRules.TryCheckNewEntry(name, SiblingTitles(vault, name.GroupPath), out var envError))
            {
                context.Stderr.WriteLine($"keypaste set: {EntryNameSanitizer.SanitizeProse(envError, 1024).Text}");
                context.Stderr.WriteLine("Nothing was written.");
                return CliApp.ExitUsageError;
            }

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

    private static int SetFields(Vault vault, EntryName name, string entryPath, IReadOnlyList<string> fields, bool plain, CliContext context)
    {
        if (vault.Fields(name) is not { } current)
        {
            context.Stderr.WriteLine($"keypaste set: no entry '{Shown(entryPath)}'; --field writes to an entry that already exists");
            return CliApp.ExitNotFound;
        }

        if (plain && fields.FirstOrDefault(field => current.Any(existing => string.Equals(existing.Name, field, StringComparison.Ordinal))) is { } kept)
        {
            context.Stderr.WriteLine($"keypaste set: '{Shown(kept)}' already exists and keeps its protection; --plain applies only to a new field");
            context.Stderr.WriteLine("Nothing was written.");
            return CliApp.ExitUsageError;
        }

        List<FieldWrite> writes = [];

        foreach (var field in fields)
        {
            var shown = Shown(field);
            using var secret = ReadValue(context, $"Value for {shown}: ", $"Confirm value for {shown}: ");

            if (secret is null)
            {
                return CliApp.ExitUsageError;
            }

            writes.Add(new FieldWrite(field, new string(secret.Value), plain ? false : null));
        }

        vault.SetFields(name, writes);
        vault.Save();

        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        context.Stderr.WriteLine(
            $"  {done} set {string.Join(", ", fields.Select(Shown))} on {Shown(entryPath)} {style.Glyph(context.Stderr, Mark.Dot)} the entry's earlier state stays in history");

        return CliApp.ExitSuccess;
    }

    internal static IReadOnlyList<string> SiblingTitles(Vault vault, string groupPath) =>
        [.. vault.ReadEntries().Where(entry => string.Equals(entry.GroupPath, groupPath, StringComparison.Ordinal)).Select(entry => entry.Title)];

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
