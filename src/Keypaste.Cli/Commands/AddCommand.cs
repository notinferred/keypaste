using Keypaste.Core;
using Keypaste.Core.Editing;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Adds an entry: <c>keypaste add &lt;entry&gt;</c>.</summary>
/// <remarks>
/// Fields given as flags are used as-is; anything omitted is prompted for when interactive.
/// The flags exist so the whole verb is scriptable — which is what lets the KeePassXC
/// compatibility gate drive the shipped binary instead of a throwaway fixture generator.
/// </remarks>
internal static class AddCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("username", TakesValue: true),
        new("url", TakesValue: true),
        new("notes", TakesValue: true),
        new("group", TakesValue: true),
        .. GenerateOption.Specs,
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste add: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine(
                "usage: keypaste add <item> [--group G] [--username U] [--url X] [--notes N]");
            context.Stdout.WriteLine($"       {GenerateOption.Usage}");
            return CliApp.ExitSuccess;
        }

        if (!GenerateOption.TryRead(line, out var recipe, out var recipeError))
        {
            context.Stderr.WriteLine($"keypaste add: {recipeError}");
            return CliApp.ExitUsageError;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste add: expected exactly one item name");
            return CliApp.ExitUsageError;
        }

        var target = line.Operands[0];
        var groupFlag = line.Value("group");

        var pathNamesGroup = target.Contains('/', StringComparison.Ordinal);
        if (pathNamesGroup && groupFlag is not null)
        {
            context.Stderr.WriteLine(
                "keypaste add: give the group in the item path or with --group, not both");
            return CliApp.ExitUsageError;
        }

        var typed = EntryName.Parse(target);
        var title = typed.Title;
        var groupPath = WrittenGroup.Normalize(pathNamesGroup ? typed.GroupPath : groupFlag ?? string.Empty);

        if (title.Length == 0)
        {
            context.Stderr.WriteLine("keypaste add: the item name cannot be empty");
            return CliApp.ExitUsageError;
        }

        if (!Vault.IsCreatable(new EntryName(groupPath, title), out var refusal))
        {
            context.Stderr.WriteLine(ReservedGroups.IsReserved(groupPath)
                ? $"keypaste add: {EntryNameSanitizer.SanitizePath(groupPath + "/" + title).Text} is keypaste's own group; it cannot be written here"
                : $"keypaste add: {refusal}");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste add: {locateError}");
            return CliApp.ExitUsageError;
        }

        var edit = new AddItem(new EntryName(groupPath, title), string.Empty, string.Empty, string.Empty, null);

        // The entry password is never a flag: it would land in the shell history and in
        // /proc/<pid>/cmdline. It is prompted, read from stdin when piped, or generated.
        AddItem? Read(CliContext asked)
        {
            var username = line.Value("username") ?? Ask(asked, "Username: ");
            var url = line.Value("url") ?? Ask(asked, "URL: ");
            var notes = line.Value("notes") ?? Ask(asked, "Notes: ");

            if (recipe is { } wanted)
            {
                return edit with { Username = username, Url = url, Notes = notes, Password = NewSecret.Generated(wanted) };
            }

            using var secret = asked.Prompt.ReadSecret("Password: ");

            if (secret is null)
            {
                asked.Stderr.WriteLine("keypaste add: no password given");
                return null;
            }

            return edit with { Username = username, Url = url, Notes = notes, Password = NewSecret.Of(new string(secret.Value)) };
        }

        var route = new OwnerRoute("add", Read, (done, asked) => Done(done, recipe, asked));

        return VaultSession.OpenHeld(path, line, context, route, vault =>
        {
            // Asked before any prompt, so a refusal costs nobody a password.
            var check = TerminalEdits.Check(vault, edit, EditLines.Scene(context));
            if (check.Refusal is { } refused)
            {
                return EditLines.Refuse(context, "add", refused);
            }

            if (Read(context) is not { } full)
            {
                return CliApp.ExitUsageError;
            }

            var result = TerminalEdits.Apply(vault, TerminalEdits.Generate(full), EditLines.Scene(context), shown: null);

            if (result.Refusal is { } late)
            {
                return EditLines.Refuse(context, "add", late);
            }

            vault.Save();
            return Done(result.Done!, recipe, context);
        });
    }

    /// <summary>Says what was added: a count, never the value.</summary>
    internal static int Done(EditDone done, SecretRecipe? recipe, CliContext context)
    {
        // Somebody who wants the value runs `keypaste get`, which already has the clipboard, the
        // --reveal discipline and the auto-clear.
        context.Stderr.WriteLine(recipe is { } generated
            ? $"Added {done.Item} ({generated.Describe("password")} generated)"
            : $"Added {done.Item}");

        // KeePassXC would make this entry, so keypaste does — and says what it costs, because
        // from here on nothing can address either one by the path they share.
        if (done.SharesPath)
        {
            context.Stderr.WriteLine(
                $"note: '{done.Item}' now names two items. Address them by group and title; " +
                "keypaste get will refuse that path until one is renamed.");
        }

        return CliApp.ExitSuccess;
    }

    private static string Ask(CliContext context, string prompt)
    {
        return context.Prompt.IsInteractive ? context.Prompt.ReadLine(prompt) ?? string.Empty : string.Empty;
    }
}
