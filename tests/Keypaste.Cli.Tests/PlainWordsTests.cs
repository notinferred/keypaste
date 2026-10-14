using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The CLI's help says item, environment, allowance, what an agent can see, KeePass database and
/// AI tool, never the words those replaced (D-0457).
/// </summary>
/// <remarks>
/// Command and subcommand names, flags, file names, environment variables, <c>kp://</c> references
/// and backticked names keep their spelling, since a person types them, so they are taken out
/// before looking.
/// </remarks>
public sealed partial class PlainWordsTests
{
    private static readonly string[][] _subcommands =
    [
        ["env", "ls"], ["env", "set"], ["env", "rm"], ["env", "pull"], ["env", "export"], ["env", "diff"], ["env", "tag"], ["env", "untag"],
        ["token", "create"], ["token", "ls"], ["token", "revoke"], ["token", "bundle"],
        ["field", "ls"], ["field", "rm"],
        ["grants", "revoke"],
        ["log", "verify"],
        ["mcp", "serve"], ["mcp", "setup"], ["mcp", "policy"],
        ["share", "ls"], ["share", "revoke"],
    ];

    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [GeneratedRegex(@"`[^`]*`|--[a-z][a-z-]*|\bkeypaste [a-z]+(?: (?:revoke|verify|policy|serve|setup))?|\bgrants(?=,| revoke| {2,})|\b[a-z]+(?:_[a-z]+)+\b|\b[\w-]+\.(?:toml|jsonl?|kdbx)\b|\bKEYPASTE_[A-Z_]+\b|kp://\S*")]
    private static partial Regex Interface();

    [Fact]
    public void EveryHelp_HoldsNoneOfTheRetiredWords()
    {
        var said = new List<(string Where, string Text)> { ("keypaste --help", Help("--help")) };

        foreach (var verb in Verbs().Where(verb => !Program.StartsBridge([verb, "--help"])))
        {
            said.Add(($"keypaste {verb} --help", Help(verb, "--help")));
        }

        foreach (var command in _subcommands)
        {
            said.Add(($"keypaste {string.Join(' ', command)} --help", Help([.. command, "--help"])));
        }

        Assert.All(said, item => Assert.NotEmpty(item.Text.Trim()));
        Assert.Empty(Offending(said));
    }

    private static string Help(params string[] args)
    {
        using var harness = new CliHarness();

        harness.Run(args);

        return harness.Out + harness.Err;
    }

    private static IEnumerable<string> Verbs() => Help("--help")
        .Split(Environment.NewLine)
        .SkipWhile(line => line != "SECRETS")
        .TakeWhile(line => line != "FLAGS")
        .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
        .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);

    private static List<string> Offending(IEnumerable<(string Where, string Text)> said) =>
        [.. said.SelectMany(item => Retired()
            .Matches(Interface().Replace(item.Text, " "))
            .Select(match => $"{item.Where}: '{match.Value}' in {item.Text}"))];
}
