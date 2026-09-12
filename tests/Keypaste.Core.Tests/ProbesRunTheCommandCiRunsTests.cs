using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Every probe workflow runs the byte-identical test command <c>ci.yml</c> runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>CLAUDE.md says this is tested rather than assumed, and until F.9 it was assumed.</b> Both
/// probes carried a comment saying the command was verbatim and nothing compared the two files, so
/// the claim held only as long as somebody remembered it. A third probe arrived with F.9 and made
/// that worse rather than better.
/// </para>
/// <para>
/// <b>Why it matters more here than it looks.</b> A probe exists to reproduce a defect that CI sees
/// and this machine does not, and the only mechanism the code supports is the contention
/// <c>dotnet test keypaste.slnx</c> creates by running three assemblies at once. Narrow the command
/// to the suspect class and the condition goes away: F.8 reproduced in
/// <c>LargeVaultListingTests</c>, not in the <c>ListingSizeTests</c> class its probe was written
/// for, so a narrowed probe would have measured nothing and reported a green run as evidence.
/// </para>
/// <para>
/// Environment is deliberately not compared. F.9's arms differ only in environment — that is what
/// makes them the same load three ways — and a rule that forbade it would forbid the experiment
/// while doing nothing about the risk, which is the command drifting.
/// </para>
/// </remarks>
public sealed class ProbesRunTheCommandCiRunsTests
{
    /// <summary>The workflows that exist to measure what CI does, and must therefore do it.</summary>
    /// <remarks>
    /// Named rather than discovered. A probe is a workflow whose whole purpose is to reproduce the
    /// CI command, and no pattern separates one from a workflow that legitimately runs a narrower
    /// command — <c>app.yml</c> tests one csproj on purpose. Adding a probe means adding it here,
    /// and the empty-list assertion below is what stops this from silently becoming a no-op.
    /// </remarks>
    private static readonly string[] _probes = ["listing-probe.yml", "pool-probe.yml"];

    [Fact]
    public void EveryProbeRunsTheSuiteCommandCiRuns()
    {
        var ci = TestCommandsIn("ci.yml");

        // ci.yml's own command is the reference. A pattern that stopped matching would leave every
        // comparison below vacuously true, which is the failure mode this file exists to catch.
        var reference = Assert.Single(ci);

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

    /// <summary>
    /// Every distinct <c>dotnet test</c> invocation in one workflow, normalised for shell noise.
    /// </summary>
    /// <remarks>
    /// Redirections and the <c>if</c> around them belong to the probe's loop rather than to the
    /// command, so they are trimmed: what is compared is the executable and its arguments. A
    /// comment naming the command is prose about it, not a call to it.
    /// </remarks>
    private static List<string> TestCommandsIn(string workflow)
    {
        var path = Path.Combine(RepoRoot(), ".github", "workflows", workflow);

        Assert.True(File.Exists(path), $"{workflow} is named by this test and is not in .github/workflows");

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
