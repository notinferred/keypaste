using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

public sealed class ProbesRunTheCommandCiRunsTests
{
    // These probes must preserve the cross-assembly contention of CI's full-suite command.
    private static readonly string[] _probes = ["listing-probe.yml", "pool-probe.yml"];

    [Fact]
    public void EveryProbeRunsTheSuiteCommandCiRuns()
    {
        var ci = TestCommandsIn("ci.yml");

        var reference = Assert.Single(ci);
        var local = TestCommandsInFile(Path.Combine(RepoRoot(), "scripts", "verify.sh"))
            .Where(command => command.StartsWith("dotnet test keypaste.slnx ", StringComparison.Ordinal));
        Assert.Equal(reference, Assert.Single(local));

        List<string> wrong = [];
        foreach (var probe in _probes)
        {
            var commands = TestCommandsIn(probe);

            if (commands.Count == 0)
            {
                wrong.Add($"{probe} runs no `dotnet test` command at all");
                continue;
            }

            foreach (var command in commands.Where(command => command != reference))
            {
                wrong.Add($"{probe} runs:\n      {command}\n    ci.yml runs:\n      {reference}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "a probe that does not run CI's command is not measuring CI:\n  " + string.Join("\n  ", wrong));
    }

    private static List<string> TestCommandsIn(string workflow)
    {
        var path = Path.Combine(RepoRoot(), ".github", "workflows", workflow);

        Assert.True(File.Exists(path), $"{workflow} is named by this test and is not in .github/workflows");

        return TestCommandsInFile(path);
    }

    private static List<string> TestCommandsInFile(string path)
    {
        HashSet<string> commands = new(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = Regex.Match(line, @"dotnet test [^>|&;]*");
            if (match.Success)
            {
                commands.Add(Regex.Replace(match.Value.Trim(), @"\s+", " "));
            }
        }

        return [.. commands];
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Could not locate keypaste.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
