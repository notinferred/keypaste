using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Finds items by name: <c>keypaste search &lt;text&gt;</c>.</summary>
/// <remarks>
/// Core matches titles, group paths, usernames and URLs and reads no password, note or protected
/// field (D-0278). A row names which of the username and URL matched, in the app's fixed words, and
/// never the value it matched in; nothing echoes the query back.
/// </remarks>
internal static class SearchCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(CliJson.Option, TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste search: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste search <text> [--json]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste search: expected one text to look for; quote words that go together");
            return CliApp.ExitUsageError;
        }

        var query = line.Operands[0];
        if (string.IsNullOrWhiteSpace(query))
        {
            context.Stderr.WriteLine("keypaste search: the text cannot be empty; ls lists every item");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste search: {locateError}");
            return CliApp.ExitUsageError;
        }

        var json = line.HasFlag(CliJson.Option);

        return VaultSession.Open(path, line, context, vault =>
        {
            var matches = vault.Search(query)
                .Select(match => (Path: PathOf(match.Name), Match: match))
                .OrderBy(found => found.Path, StringComparer.Ordinal)
                .ToList();

            if (json)
            {
                CliJson.WriteArray(context.Stdout, matches, (writer, found) =>
                {
                    writer.WriteString("path", found.Path);
                    writer.WriteString("group", found.Match.Name.GroupPath);
                    writer.WriteString("title", found.Match.Name.Title);
                    writer.WriteStartArray("matched");
                    foreach (var field in Matched(found.Match.Fields))
                    {
                        writer.WriteStringValue(field);
                    }

                    writer.WriteEndArray();
                });

                return CliApp.ExitSuccess;
            }

            if (matches.Count == 0)
            {
                context.Stderr.WriteLine("no item matches");
                return CliApp.ExitSuccess;
            }

            var style = context.ConsoleStyle;
            var anyAltered = false;

            foreach (var (itemPath, match) in matches)
            {
                var (text, altered) = ListCommand.Safe(itemPath);
                anyAltered |= altered;

                var why = Why(match.Fields);
                context.Stdout.WriteLine(why.Length == 0 ? text : $"{text}  {style.Paint(context.Stdout, Tone.Muted, why)}");
            }

            if (anyAltered)
            {
                context.Stderr.WriteLine("note: a displayed name is not what the vault holds. Use --json to see it.");
            }

            return CliApp.ExitSuccess;
        });
    }

    private static string PathOf(EntryName name) => name.GroupPath.Length == 0 ? name.Title : name.GroupPath + "/" + name.Title;

    private static IEnumerable<string> Matched(MatchedFields fields)
    {
        if (fields.HasFlag(MatchedFields.Title))
        {
            yield return "title";
        }

        if (fields.HasFlag(MatchedFields.GroupPath))
        {
            yield return "group";
        }

        if (fields.HasFlag(MatchedFields.Username))
        {
            yield return "username";
        }

        if (fields.HasFlag(MatchedFields.Url))
        {
            yield return "url";
        }
    }

    /// <summary>The matched fields a row's path does not already show, in the app's words for them.</summary>
    private static string Why(MatchedFields fields) =>
        (fields.HasFlag(MatchedFields.Username), fields.HasFlag(MatchedFields.Url)) switch
        {
            (true, true) => "username, URL",
            (true, false) => "username",
            (false, true) => "URL",
            _ => string.Empty,
        };
}
