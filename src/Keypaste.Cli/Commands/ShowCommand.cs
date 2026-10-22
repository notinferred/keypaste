using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Shows one item's fields and tags: <c>keypaste show &lt;item&gt; [--reveal]</c>.</summary>
/// <remarks>
/// Without <c>--reveal</c> every value is masked, the username and URL included, and no custom field
/// is read (D-0463). Revealed text is drawn through <see cref="DisplayTextSanitizer"/>, so a value
/// that could misrepresent itself on a terminal is cleaned there and told on stderr; only
/// <c>--json</c> carries the values as the vault holds them. An item with a TOTP lists <c>totp</c> with
/// no value even then, since its code is computed only when <c>keypaste get &lt;item&gt; totp</c> asks.
/// </remarks>
internal static class ShowCommand
{
    private const string _mask = "********";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("reveal", TakesValue: false),
        new(CliJson.Option, TakesValue: false),
    ];

    /// <summary>One field as shown: its name in the vault, its label, whether it is custom, and its flags when it is.</summary>
    private sealed record ShownField(string Name, string Label, EntryField? Custom, string? Value);

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste show: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste show <item> [--reveal] [--json]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste show: expected exactly one item name");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste show: {locateError}");
            return CliApp.ExitUsageError;
        }

        var entryPath = line.Operands[0];
        var reveal = line.HasFlag("reveal");
        var json = line.HasFlag(CliJson.Option);

        return VaultSession.Open(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste show: no item '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            var name = EntryName.Of(entry);
            var tags = vault.Tags(name) ?? [];
            var fields = Fields(vault, entry, reveal);

            if (json)
            {
                WriteJson(context, entry, tags, fields);
            }
            else
            {
                WriteText(context, entry, tags, fields, reveal);
            }

            return CliApp.ExitSuccess;
        });
    }

    /// <summary>The standard fields that hold something, in the order agents name them, then <c>totp</c> with no value, then every custom field.</summary>
    /// <remarks>
    /// A custom field's value is read only when it will be printed. One KeePassXC made under a standard
    /// name in another case, such as <c>password</c>, has no value core reads (V.7a), so it stays masked.
    /// </remarks>
    private static List<ShownField> Fields(Vault vault, VaultEntry entry, bool reveal)
    {
        (string Name, string Label, string Value)[] standard =
        [
            ("username", "username", entry.Username),
            ("password", "password", entry.Password),
            ("url", "URL", entry.Url),
            ("notes", "notes", entry.Notes),
        ];

        var name = EntryName.Of(entry);

        return
        [
            .. standard
                .Where(field => field.Value.Length > 0)
                .Select(field => new ShownField(field.Name, field.Label, null, reveal ? field.Value : null)),
            .. vault.MayHoldTotp(name) ? [new ShownField(CredentialFields.Totp, CredentialFields.Totp, null, null)] : Array.Empty<ShownField>(),
            .. (vault.Fields(name) ?? [])
                .Select(field => new ShownField(
                    field.Name,
                    field.Name,
                    field,
                    reveal && !FieldNameRules.IsStandard(field.Name) ? vault.ReadField(name, field.Name) : null)),
        ];
    }

    private static void WriteJson(CliContext context, VaultEntry entry, IReadOnlyList<string> tags, List<ShownField> fields)
    {
        CliJson.WriteArray<VaultEntry>(context.Stdout, [entry], (writer, item) =>
        {
            writer.WriteString("path", item.Path);
            writer.WriteString("group", item.GroupPath);
            writer.WriteString("title", item.Title);

            writer.WriteStartArray("tags");
            foreach (var tag in tags)
            {
                writer.WriteStringValue(tag);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("fields");
            foreach (var field in fields)
            {
                writer.WriteStartObject();
                writer.WriteString("name", field.Name);
                writer.WriteBoolean("standard", field.Custom is null);

                if (field.Custom is { } custom)
                {
                    writer.WriteBoolean("protected", custom.IsProtected);
                    writer.WriteBoolean("readonly", custom.IsReadOnly);
                }

                if (field.Value is { } value)
                {
                    writer.WriteString("value", value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private static void WriteText(CliContext context, VaultEntry entry, IReadOnlyList<string> tags, List<ShownField> fields, bool reveal)
    {
        var writer = context.Stdout;
        var style = context.ConsoleStyle;

        var (shownPath, namesAltered) = ListCommand.Safe(entry.Path);
        writer.WriteLine(shownPath);

        if (tags.Count > 0)
        {
            var shownTags = tags.Select(tag => EntryNameSanitizer.Sanitize(tag)).ToList();
            namesAltered |= shownTags.Any(tag => tag.WasAltered);
            writer.WriteLine($"{style.Paint(writer, Tone.Muted, "tags:")} {string.Join(", ", shownTags.Select(tag => tag.Text))}");
        }

        var labels = fields.Select(field => field.Custom is null ? new SanitizedName(field.Label, false) : EntryNameSanitizer.Sanitize(field.Label)).ToList();
        namesAltered |= labels.Any(label => label.WasAltered);

        var width = labels.Count == 0 ? 0 : labels.Max(label => label.Text.Length);
        var indent = new string(' ', 2 + width + 2);
        var valuesAltered = false;

        for (var i = 0; i < fields.Count; i++)
        {
            var lines = new[] { _mask };

            if (fields[i].Value is { } value)
            {
                var shown = DisplayTextSanitizer.Sanitize(value, Math.Max(value.Length, 1));
                valuesAltered |= shown.WasAltered;
                lines = shown.Text.Split('\n');
            }

            var flags = fields[i].Custom is { } custom
                ? "  " + style.Paint(writer, Tone.Muted, (custom.IsProtected ? "protected" : "plain") + (custom.IsReadOnly ? ", read-only" : string.Empty))
                : string.Empty;

            writer.WriteLine($"  {labels[i].Text.PadRight(width)}  {lines[0]}{(lines.Length == 1 ? flags : string.Empty)}");

            for (var j = 1; j < lines.Length; j++)
            {
                writer.WriteLine($"{indent}{lines[j]}{(j == lines.Length - 1 ? flags : string.Empty)}");
            }
        }

        if (namesAltered)
        {
            context.Stderr.WriteLine("note: a displayed name is not what the vault holds. Use --json to see it.");
        }

        if (valuesAltered)
        {
            context.Stderr.WriteLine("note: a displayed value is not what the vault holds. Use --json --reveal to see it.");
        }

        if (!reveal && fields.Count > 0)
        {
            context.Stderr.WriteLine(style.Paint(context.Stderr, Tone.Muted, "values are hidden; --reveal prints them"));
        }

        if (fields.Any(field => field.Custom is null && field.Name == CredentialFields.Totp))
        {
            context.Stderr.WriteLine(style.Paint(context.Stderr, Tone.Muted, $"keypaste get {shownPath} totp gives the current TOTP code"));
        }
    }
}
