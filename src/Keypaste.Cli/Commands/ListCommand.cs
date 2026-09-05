using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Lists the vault: <c>keypaste ls</c>.</summary>
/// <remarks>
/// <para>
/// Names only — never a username, never a password. The tree is drawn with spaces and slashes
/// rather than box-drawing characters, so the output stays ASCII and survives every terminal,
/// code page and CI log. <c>--flat</c> is the machine-readable form and matches the shape of
/// <c>keepassxc-cli ls -R -f</c>.
/// </para>
/// <para>
/// <b>Every name goes through <see cref="EntryNameSanitizer"/> before it is drawn.</b> Titles and
/// group paths are attacker-reachable — anything with write access to the vault chooses them, which
/// includes KeePassXC and a vault shared with a teammate — and the sanitizer's own documentation
/// names a listing as what <see cref="SanitizedName.WasAltered"/> is for. A KDBX title cannot carry
/// a C0 control character (the format stores it in XML, and U+001B is not a legal XML 1.0
/// character), so the threat here is not a repainted terminal: it is a name that reads as something
/// it is not, through a bidi override or an invisible code point.
/// </para>
/// <para>
/// <b>The mark and the note appear only when something was actually altered</b>, so a vault of
/// ordinary names prints exactly what it printed before. <c>--flat</c> never takes the mark, because
/// it is the form something else parses; it is told on stderr instead, which a parser does not read.
/// Nothing is ever hidden — a scrubbed row is still listed, because keypaste does not get to pretend
/// the vault holds something other than what KeePassXC shows (docs/PRODUCT.md law 4.6).
/// </para>
/// </remarks>
internal static class ListCommand
{
    /// <summary>The mark put in front of a row whose displayed name is not what the vault holds.</summary>
    private const string _alteredMark = "?";

    /// <summary>How deep a path may be before segments are dropped.</summary>
    /// <remarks>
    /// Well above <see cref="EntryNameSanitizer.SanitizePath"/>'s default. This is a listing of the
    /// reader's own vault, where silently dropping a real group would be the worse failure; the cap
    /// is here to bound one absurd name, not to trim ordinary ones.
    /// </remarks>
    private const int _displayDepth = 32;

    /// <summary>The longest path drawn, for the same reason.</summary>
    private const int _displayLength = 512;

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

            // Sorted on the stored name rather than the drawn one, so the order is the vault's and
            // two names that scrub to the same text keep a stable relative position.
            paths.Sort(StringComparer.Ordinal);

            var rows = new List<(string Text, bool Altered)>(paths.Count);
            var anyAltered = false;

            foreach (var item in paths)
            {
                var row = Safe(item);
                rows.Add(row);
                anyAltered |= row.Altered;
            }

            foreach (var (text, altered) in rows)
            {
                var body = flat ? text : Indent(text);

                context.Stdout.WriteLine(
                    flat || !anyAltered ? body : (altered ? _alteredMark : " ") + " " + body);
            }

            if (anyAltered)
            {
                context.Stderr.WriteLine(flat
                    ? "note: a displayed name is not what the vault holds. Run without --flat to see which."
                    : $"{_alteredMark}  the displayed name is not what the vault holds.");
            }

            return CliApp.ExitSuccess;
        });
    }

    /// <summary>Scrubs one path for display, keeping the trailing slash that marks a group.</summary>
    /// <remarks>
    /// The slash is removed before sanitizing and put back after, because an empty trailing segment
    /// is not something <see cref="EntryNameSanitizer.SanitizePath"/> should have to reason about.
    /// </remarks>
    private static (string Text, bool Altered) Safe(string path)
    {
        var isGroup = path.EndsWith('/');
        var body = isGroup ? path[..^1] : path;

        var sanitized = EntryNameSanitizer.SanitizePath(
            body,
            maximumDepth: _displayDepth,
            maximumLength: _displayLength);

        return (isGroup ? sanitized.Text + "/" : sanitized.Text, sanitized.WasAltered);
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
