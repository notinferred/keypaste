using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Lists, adds, replaces, removes and saves an item's attachments: <c>keypaste attachment &lt;ls|add|replace|rm|save&gt;</c>.</summary>
/// <remarks>
/// Names and sizes only; no content reaches the terminal. Adding, replacing and removing each make a history revision, as
/// KeePassXC's own edit does. None of them is one of the edits sent to the app or <c>keypaste approve</c>, whose request
/// holds 64 KiB, so each is refused while either holds the vault (D-0535); listing and saving read the file and work then too.
/// </remarks>
internal static class AttachmentCommand
{
    /// <summary>What an edit says when the vault's history limits dropped the revision it made (D-0533).</summary>
    private const string _notKept = "too large for this vault's history to keep the item as it was";

    private static readonly OptionSpec[] _listOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(CliJson.Option, TakesValue: false),
    ];

    private static readonly OptionSpec[] _addOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("name", TakesValue: true),
    ];

    private static readonly OptionSpec[] _options =
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

            case "add":
                return Add(args, context);

            case "replace":
                return Replace(args, context);

            case "rm":
                return Remove(args, context);

            case "save":
                return Save(args, context);

            case "help":
            case "--help":
            case "-h":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste attachment: unknown subcommand '{args[1]}'");
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    private static int List(string[] args, CliContext context)
    {
        if (!TryParse(args, "ls", "<item> [--json]", _listOptions, 1, "exactly one item name", context, out var line, out var path, out var exit))
        {
            return exit;
        }

        var entryPath = line.Operands[0];
        var json = line.HasFlag(CliJson.Option);

        return VaultSession.Open(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste attachment ls: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            var attachments = vault.Attachments(EntryName.Of(entry)) ?? [];

            if (json)
            {
                CliJson.WriteArray(context.Stdout, attachments, (writer, attachment) =>
                {
                    writer.WriteString("name", attachment.Name);
                    writer.WriteNumber("size", attachment.Size);
                });

                return CliApp.ExitSuccess;
            }

            if (attachments.Count == 0)
            {
                context.Stderr.WriteLine($"'{Shown(entry.Path)}' has no attachments");
                return CliApp.ExitSuccess;
            }

            var names = attachments.Select(attachment => EntryNameSanitizer.Sanitize(attachment.Name)).ToList();
            var width = names.Max(name => name.Text.Length);

            for (var i = 0; i < attachments.Count; i++)
            {
                context.Stdout.WriteLine($"{names[i].Text.PadRight(width)}  {attachments[i].SizeText}");
            }

            if (names.Any(name => name.WasAltered))
            {
                context.Stderr.WriteLine("note: a displayed name is not what the vault holds. Use --json to see it.");
            }

            return CliApp.ExitSuccess;
        });
    }

    private static int Add(string[] args, CliContext context)
    {
        if (!TryParse(args, "add", "<item> <file> [--name <name>]", _addOptions, 2, "an item name and a file", context, out var line, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, file) = (line.Operands[0], Path.GetFullPath(line.Operands[1], context.WorkingDirectory));
        var attachment = line.Value("name") ?? Path.GetFileName(file);

        if (!VaultNameRules.IsValidAttachmentName(attachment, out var nameError))
        {
            context.Stderr.WriteLine($"keypaste attachment add: {nameError}");
            return CliApp.ExitUsageError;
        }

        if (!Readable(file, "add", context, out exit))
        {
            return exit;
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste attachment add: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            bool added;
            bool kept;

            try
            {
                using var content = File.OpenRead(file);
                added = vault.AddAttachment(EntryName.Of(entry), attachment, content, out kept);
            }
            catch (VaultException ex)
            {
                context.Stderr.WriteLine($"keypaste attachment add: {ex.Message}");
                return CliApp.ExitUsageError;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Stderr.WriteLine($"keypaste attachment add: could not read '{file}': {ex.Message}");
                return CliApp.ExitNotFound;
            }

            if (!added)
            {
                context.Stderr.WriteLine($"keypaste attachment add: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            vault.Save();
            return Done(context, $"attached {Named(attachment)} to {Shown(entry.Path)}", Size(vault, entry, attachment), kept ? null : _notKept);
        });
    }

    private static int Replace(string[] args, CliContext context)
    {
        if (!TryParse(args, "replace", "<item> <name> <file>", _options, 3, "an item name, an attachment name and a file", context, out var line, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, attachment, file) = (line.Operands[0], line.Operands[1], Path.GetFullPath(line.Operands[2], context.WorkingDirectory));

        if (!Readable(file, "replace", context, out exit))
        {
            return exit;
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste attachment replace: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            bool replaced;
            bool kept;

            try
            {
                using var content = File.OpenRead(file);
                replaced = vault.ReplaceAttachment(EntryName.Of(entry), attachment, content, out kept);
            }
            catch (VaultException ex)
            {
                context.Stderr.WriteLine($"keypaste attachment replace: {ex.Message}");
                return CliApp.ExitUsageError;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Stderr.WriteLine($"keypaste attachment replace: could not read '{file}': {ex.Message}");
                return CliApp.ExitNotFound;
            }

            if (!replaced)
            {
                context.Stderr.WriteLine($"keypaste attachment replace: '{Shown(entry.Path)}' has no attachment {Named(attachment)}");
                return CliApp.ExitNotFound;
            }

            vault.Save();
            return Done(context, $"replaced {Named(attachment)} on {Shown(entry.Path)}", Size(vault, entry, attachment), kept ? "what it held stays in history" : _notKept);
        });
    }

    private static int Remove(string[] args, CliContext context)
    {
        if (!TryParse(args, "rm", "<item> <name>", _options, 2, "an item name and an attachment name", context, out var line, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, attachment) = (line.Operands[0], line.Operands[1]);

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste attachment rm: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            bool removed;
            bool kept;

            try
            {
                removed = vault.RemoveAttachment(EntryName.Of(entry), attachment, out kept);
            }
            catch (VaultException ex)
            {
                context.Stderr.WriteLine($"keypaste attachment rm: {ex.Message}");
                return CliApp.ExitUsageError;
            }

            if (!removed)
            {
                context.Stderr.WriteLine($"keypaste attachment rm: '{Shown(entry.Path)}' has no attachment {Named(attachment)}");
                return CliApp.ExitNotFound;
            }

            vault.Save();
            return Done(context, $"removed {Named(attachment)} from {Shown(entry.Path)}", history: kept ? "it stays in history" : _notKept);
        });
    }

    private static int Save(string[] args, CliContext context)
    {
        if (!TryParse(args, "save", "<item> <name> <file>", _options, 3, "an item name, an attachment name and a file", context, out var line, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, attachment, file) = (line.Operands[0], line.Operands[1], Path.GetFullPath(line.Operands[2], context.WorkingDirectory));

        if (File.Exists(file) || Directory.Exists(file))
        {
            context.Stderr.WriteLine($"keypaste attachment save: something is already at '{file}'; keypaste writes only a new file. Choose another path.");
            return CliApp.ExitUsageError;
        }

        return VaultSession.Open(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste attachment save: no item '{Shown(entryPath)}'");
                return CliApp.ExitNotFound;
            }

            switch (vault.SaveAttachment(EntryName.Of(entry), attachment, file, out var failure))
            {
                case AttachmentSaveOutcome.Saved:
                    break;

                case AttachmentSaveOutcome.NotFound:
                    context.Stderr.WriteLine($"keypaste attachment save: '{Shown(entry.Path)}' has no attachment {Named(attachment)}");
                    return CliApp.ExitNotFound;

                case AttachmentSaveOutcome.PathAlreadyExists:
                    context.Stderr.WriteLine($"keypaste attachment save: something is already at '{file}'; keypaste writes only a new file. Choose another path.");
                    return CliApp.ExitUsageError;

                default:
                    context.Stderr.WriteLine($"keypaste attachment save: could not write '{file}': {failure}");
                    return CliApp.ExitInternalError;
            }

            Done(context, $"saved {Named(attachment)} from {Shown(entry.Path)} to {file}");
            context.Stderr.WriteLine(OperatingSystem.IsWindows()
                ? "note: the file is not encrypted, and it inherits its directory's permissions; keypaste does not restrict them on Windows."
                : "note: the file is not encrypted, and only you can read it (mode 600).");
            context.Stderr.WriteLine("      Delete it when you are done; keypaste cannot take it back.");
            return CliApp.ExitSuccess;
        });
    }

    /// <summary>Parses a subcommand's line and resolves its vault, answering <c>--help</c> and a wrong count of operands itself.</summary>
    private static bool TryParse(
        string[] args,
        string subcommand,
        string usage,
        OptionSpec[] options,
        int operands,
        string expected,
        CliContext context,
        out CommandLine line,
        out string path,
        out int exit)
    {
        path = string.Empty;

        if (!CommandLine.TryParse(args, 2, options, out line, out var error))
        {
            context.Stderr.WriteLine($"keypaste attachment {subcommand}: {error}");
            exit = CliApp.ExitUsageError;
            return false;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine($"usage: keypaste attachment {subcommand} {usage}");
            exit = CliApp.ExitSuccess;
            return false;
        }

        if (line.Operands.Count != operands)
        {
            context.Stderr.WriteLine($"keypaste attachment {subcommand}: expected {expected}");
            exit = CliApp.ExitUsageError;
            return false;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste attachment {subcommand}: {locateError}");
            exit = CliApp.ExitUsageError;
            return false;
        }

        exit = CliApp.ExitSuccess;
        return true;
    }

    /// <summary>Refuses a file that is missing or over the cap before the master password is asked for; the vault checks again as it reads.</summary>
    private static bool Readable(string file, string subcommand, CliContext context, out int exit)
    {
        if (!File.Exists(file))
        {
            context.Stderr.WriteLine($"keypaste attachment {subcommand}: no file at '{file}'");
            exit = CliApp.ExitNotFound;
            return false;
        }

        if (new FileInfo(file).Length > EntryAttachment.MaximumSize)
        {
            context.Stderr.WriteLine(
                $"keypaste attachment {subcommand}: '{file}' is larger than {EntryAttachment.MaximumSize / (1024 * 1024)} MiB, the most keypaste attaches");
            exit = CliApp.ExitUsageError;
            return false;
        }

        exit = CliApp.ExitSuccess;
        return true;
    }

    /// <summary>The attachment's size as it now stands on the item.</summary>
    private static string? Size(Vault vault, VaultEntry entry, string attachment) =>
        vault.Attachments(EntryName.Of(entry))?.FirstOrDefault(held => string.Equals(held.Name, attachment, StringComparison.Ordinal))?.SizeText;

    /// <summary>Says what was done, followed by each of the attachment's size and what history kept that is given.</summary>
    private static int Done(CliContext context, string what, string? size = null, string? history = null)
    {
        var style = context.ConsoleStyle;
        var mark = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        var dot = $" {style.Glyph(context.Stderr, Mark.Dot)} ";

        context.Stderr.WriteLine(string.Join(dot, new[] { $"  {mark} {what}", size, history }.OfType<string>()));
        return CliApp.ExitSuccess;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste attachment <command>");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  ls <item>                     list attachments by name and size");
        writer.WriteLine($"  add <item> <file>             attach a file of up to {EntryAttachment.MaximumSize / (1024 * 1024)} MiB");
        writer.WriteLine("  replace <item> <name> <file>  replace an attachment; history keeps the old one");
        writer.WriteLine("  rm <item> <name>              remove an attachment; history keeps it");
        writer.WriteLine("  save <item> <name> <file>     write an attachment to a new file, unencrypted");
        writer.WriteLine();
        writer.WriteLine("add names the attachment after its file, or --name <name>.");
    }

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;

    private static string Named(string attachment) => EntryNameSanitizer.Sanitize(attachment).Text;
}
