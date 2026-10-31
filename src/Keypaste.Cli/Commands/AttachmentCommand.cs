using System.CommandLine;
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

    private static readonly Argument<string> _item = Syntax.Operand("item", "the item's name");
    private static readonly Argument<string> _name = Syntax.Operand("name", "the attachment's name");
    private static readonly Argument<string> _file = Syntax.Operand("file", "the file");
    private static readonly Option<string> _named = Syntax.Value("--name", "name", "name the attachment this rather than after its file");

    internal static Verb Command { get; } = new("attachment", "list, add, replace, remove or save an item's attachments")
    {
        new CliVerb("ls", "list attachments by name and size", List) { _item, CliOptions.Json, CliOptions.Vault, CliOptions.Keyfile },
        new CliVerb("add", $"attach a file of up to {EntryAttachment.MaximumSize / (1024 * 1024)} MiB", Add) { _item, _file, _named, CliOptions.Vault, CliOptions.Keyfile },
        new CliVerb("replace", "replace an attachment; history keeps the old one", Replace) { _item, _name, _file, CliOptions.Vault, CliOptions.Keyfile },
        new CliVerb("rm", "remove an attachment; history keeps it", Remove) { _item, _name, CliOptions.Vault, CliOptions.Keyfile },
        new CliVerb("save", "write an attachment to a new file, unencrypted", Save) { _item, _name, _file, CliOptions.Vault, CliOptions.Keyfile },
    };

    private static int List(ParseResult line, CliContext context)
    {
        if (!TryLocate(line, "ls", context, out var path, out var exit))
        {
            return exit;
        }

        var entryPath = line.GetValue(_item)!;
        var json = line.GetValue(CliOptions.Json);

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

    private static int Add(ParseResult line, CliContext context)
    {
        if (!TryLocate(line, "add", context, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, file) = (line.GetValue(_item)!, Path.GetFullPath(line.GetValue(_file)!, context.WorkingDirectory));
        var attachment = line.GetValue(_named) ?? Path.GetFileName(file);

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

    private static int Replace(ParseResult line, CliContext context)
    {
        if (!TryLocate(line, "replace", context, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, attachment, file) = (line.GetValue(_item)!, line.GetValue(_name)!, Path.GetFullPath(line.GetValue(_file)!, context.WorkingDirectory));

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

    private static int Remove(ParseResult line, CliContext context)
    {
        if (!TryLocate(line, "rm", context, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, attachment) = (line.GetValue(_item)!, line.GetValue(_name)!);

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

    private static int Save(ParseResult line, CliContext context)
    {
        if (!TryLocate(line, "save", context, out var path, out var exit))
        {
            return exit;
        }

        var (entryPath, attachment, file) = (line.GetValue(_item)!, line.GetValue(_name)!, Path.GetFullPath(line.GetValue(_file)!, context.WorkingDirectory));

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

    /// <summary>Resolves a subcommand's vault.</summary>
    private static bool TryLocate(ParseResult line, string subcommand, CliContext context, out string path, out int exit)
    {
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

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;

    private static string Named(string attachment) => EntryNameSanitizer.Sanitize(attachment).Text;
}
