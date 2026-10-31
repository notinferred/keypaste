using System.CommandLine;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Finds items by name: <c>keypaste search &lt;text&gt;</c>.</summary>
/// <remarks>
/// Core matches titles, group paths, usernames, URLs and an item's own tags and reads no password,
/// note or protected field (D-0278). A row names which of the username and URL matched, in the app's
/// fixed words, and never the value it matched in, and names each tag that matched, since a tag is a
/// label; nothing echoes the query back.
/// </remarks>
internal static class SearchCommand
{
    private static readonly Argument<string> _text = Syntax.Operand("text", "what to look for; quote words that go together");

    /// <summary>The words after the first, which only a missing pair of quotes puts there.</summary>
    private static readonly Argument<string[]> _unquoted = new("unquoted") { Arity = ArgumentArity.ZeroOrMore, Hidden = true };

    internal static CliVerb Command { get; } = new("search", "find items by title, group, username or URL", Execute)
    {
        _text, _unquoted, CliOptions.Json, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        if (line.GetValue(_unquoted) is { Length: > 0 })
        {
            context.Stderr.WriteLine("keypaste search: expected one text to look for; quote words that go together");
            return CliApp.ExitUsageError;
        }

        var query = line.GetValue(_text)!;
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

        var json = line.GetValue(CliOptions.Json);

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
                    writer.WriteStartArray("tags");
                    foreach (var tag in found.Match.Tags)
                    {
                        writer.WriteStringValue(tag);
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

                var tags = match.Tags.Select(tag => EntryNameSanitizer.Sanitize(tag)).ToList();
                anyAltered |= tags.Any(tag => tag.WasAltered);

                var why = Why(match.Fields, tags);
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

        if (fields.HasFlag(MatchedFields.Tag))
        {
            yield return "tag";
        }
    }

    /// <summary>The matched fields a row's path does not already show, in the app's words for them, then each tag that matched.</summary>
    private static string Why(MatchedFields fields, List<SanitizedName> tags)
    {
        List<string> parts = [];

        if (fields.HasFlag(MatchedFields.Username))
        {
            parts.Add("username");
        }

        if (fields.HasFlag(MatchedFields.Url))
        {
            parts.Add("URL");
        }

        if (tags.Count > 0)
        {
            parts.Add((tags.Count == 1 ? "tag " : "tags ") + string.Join(", ", tags.Select(tag => tag.Text)));
        }

        return string.Join(", ", parts);
    }
}
