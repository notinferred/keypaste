using Keypaste.Core;

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
        new("username", TakesValue: true),
        new("url", TakesValue: true),
        new("notes", TakesValue: true),
        new("group", TakesValue: true),
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
                "usage: keypaste add <entry> [--group G] [--username U] [--url X] [--notes N]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste add: expected exactly one entry name");
            return CliApp.ExitUsageError;
        }

        var target = line.Operands[0];
        var groupFlag = line.Value("group");

        var slash = target.LastIndexOf('/');
        if (slash >= 0 && groupFlag is not null)
        {
            context.Stderr.WriteLine(
                "keypaste add: give the group in the entry path or with --group, not both");
            return CliApp.ExitUsageError;
        }

        var title = slash < 0 ? target : target[(slash + 1)..];
        var groupPath = slash < 0 ? groupFlag ?? string.Empty : target[..slash];

        if (title.Length == 0)
        {
            context.Stderr.WriteLine("keypaste add: the entry name cannot be empty");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste add: {locateError}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.Open(path, context, vault =>
        {
            var entryPath = groupPath.Length == 0 ? title : groupPath + "/" + title;
            if (vault.Find(entryPath) is not null)
            {
                context.Stderr.WriteLine($"keypaste add: '{entryPath}' already exists");
                return CliApp.ExitUsageError;
            }

            var username = line.Value("username") ?? Ask(context, "Username: ");
            var url = line.Value("url") ?? Ask(context, "URL: ");
            var notes = line.Value("notes") ?? Ask(context, "Notes: ");

            // The entry password is never a flag: it would land in the shell history and in
            // /proc/<pid>/cmdline. It is prompted, or read from stdin when piped.
            using var secret = context.Prompt.ReadSecret("Password: ");
            if (secret is null)
            {
                context.Stderr.WriteLine("keypaste add: no password given");
                return CliApp.ExitUsageError;
            }

            vault.AddEntry(new VaultEntry
            {
                Title = title,
                Username = username,
                Password = new string(secret.Value),
                Url = url,
                Notes = notes,
                GroupPath = groupPath,
            });

            vault.Save();
            context.Stderr.WriteLine($"Added {entryPath}");
            return CliApp.ExitSuccess;
        });
    }

    private static string Ask(CliContext context, string prompt)
    {
        return context.Prompt.IsInteractive ? context.Prompt.ReadLine(prompt) ?? string.Empty : string.Empty;
    }
}
