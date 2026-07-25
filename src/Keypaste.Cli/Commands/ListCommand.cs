namespace Keypaste.Cli.Commands;

/// <summary>Lists the vault: <c>keypaste ls</c>.</summary>
/// <remarks>
/// Names only — never a username, never a password. The tree is drawn with spaces and slashes
/// rather than box-drawing characters, so the output stays ASCII and survives every terminal,
/// code page and CI log. <c>--flat</c> is the machine-readable form and matches the shape of
/// <c>keepassxc-cli ls -R -f</c>.
/// </remarks>
internal static class ListCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("flat", TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste ls: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste ls [--flat]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine("keypaste ls: unexpected argument");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste ls: {locateError}");
            return CliApp.ExitUsageError;
        }

        var flat = line.HasFlag("flat");

        return VaultSession.Open(path, context, vault =>
        {
            // Groups and entries are collected separately because a group holding no entries is
            // invisible in an entry listing, and KeePassXC lists it.
            List<string> paths = [];
            foreach (var group in vault.ReadGroupPaths())
            {
                paths.Add(group + "/");
            }

            foreach (var entry in vault.ReadEntries())
            {
                paths.Add(entry.Path);
            }

            paths.Sort(StringComparer.Ordinal);

            foreach (var item in paths)
            {
                context.Stdout.WriteLine(flat ? item : Indent(item));
            }

            return CliApp.ExitSuccess;
        });
    }

    private static string Indent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var depth = 0;
        foreach (var c in trimmed)
        {
            if (c == '/')
            {
                depth++;
            }
        }

        var name = trimmed[(trimmed.LastIndexOf('/') + 1)..];
        return new string(' ', depth * 2) + name + (path.EndsWith('/') ? "/" : string.Empty);
    }
}
