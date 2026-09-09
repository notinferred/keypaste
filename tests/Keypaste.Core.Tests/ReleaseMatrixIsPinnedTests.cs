using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Asserts that the release still reads one definition of what keypaste ships, and that the gate
/// holding it is actually wired somewhere it will run (docs/STEPS.md R.0a).
/// </summary>
/// <remarks>
/// <para>
/// A tripwire in the shape of <see cref="ReleaseDestinationIsCheckedTests"/>, and scoped the same
/// way: it guards the gate's <em>wiring</em> and never restates the gate's own claims. Whether the
/// definition and the repository agree is <c>verify-release-matrix.sh</c>'s question and it answers
/// it against fixtures; asking it again here would be a second author of one claim, which is the
/// failure D-0102 describes.
/// </para>
/// <para>
/// The one thing only a test can hold is <c>fetch-depth: 0</c>. The gate reads prior revisions of
/// <c>release-targets.json</c> to hold <c>published</c> append-only, and
/// <c>actions/checkout</c> defaults to depth 1 — so without it the history is simply absent. The
/// gate dies rather than passing when it cannot read history, which is the right direction, but a
/// reverted checkout setting would then turn every run red for a reason nobody would connect to
/// this. Catching it here names the cause.
/// </para>
/// </remarks>
public sealed class ReleaseMatrixIsPinnedTests
{
    [Fact]
    public void TheGate_RunsWhereABranchAndATagBothReachIt()
    {
        // ci.yml is where a branch meets it. Without this the definition is a file nothing reads.
        Assert.Contains(
            "scripts/verify-release-matrix.sh",
            RepositoryFile(".github", "workflows", "ci.yml"),
            StringComparison.Ordinal);

        // release.yml's guard is where a tag meets it, so a publication cannot start while the
        // download pages and the definition already disagree about what is being published.
        Assert.Contains(
            "scripts/verify-release-matrix.sh",
            Code(Lines(JobBlock(RepositoryFile(".github", "workflows", "release.yml"), "guard"))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheGitHistoryCheck_IsGivenAHistoryToRead()
    {
        var ci = RepositoryFile(".github", "workflows", "ci.yml");
        var gate = Code(Lines(JobBlock(ci, "gate")));

        Assert.Contains("scripts/verify-release-matrix.sh", gate, StringComparison.Ordinal);
        Assert.True(
            gate.Contains("fetch-depth: 0", StringComparison.Ordinal),
            "ci.yml's gate job runs verify-release-matrix.sh, which reads prior revisions of "
            + "release-targets.json to hold `published` append-only. actions/checkout defaults to "
            + "fetch-depth: 1, so without this the history is absent and the gate dies. "
            + "Restore `fetch-depth: 0` on that job's checkout.");
    }

    /// <summary>
    /// The public-origin mode belongs to <c>install.yml</c> and to nothing on a branch's or a tag's
    /// path.
    /// </summary>
    /// <remarks>
    /// It fetches published assets from <c>dl.keypaste.com</c>. An origin that cannot be reached is
    /// not evidence about the definition, so a Cloudflare failure must not redden <c>main</c> or
    /// block a release. Asserted in both directions, because the tempting edit is to add it to
    /// <c>ci.yml</c> for coverage and the cost of that is a branch that goes red for weather.
    /// </remarks>
    [Fact]
    public void ThePublicOriginMode_RunsWeeklyAndNotOnEveryPush()
    {
        Assert.Contains(
            "scripts/verify-release-matrix.sh --with-public-origin",
            RepositoryFile(".github", "workflows", "install.yml"),
            StringComparison.Ordinal);

        foreach (var workflow in new[] { "ci.yml", "release.yml", "app.yml" })
        {
            Assert.DoesNotContain(
                "--with-public-origin",
                Code(Lines(RepositoryFile(".github", "workflows", workflow))),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheGate_KeepsTheChecksThatMakeItRefuse()
    {
        var gate = RepositoryFile("scripts", "verify-release-matrix.sh");

        Assert.Contains("NEGATIVE CONTROL", gate, StringComparison.Ordinal);

        // The count must be a number rather than a truthy jq exit, which is D-0106's defect one
        // layer over: a definition that reads as an empty matrix makes GitHub skip the build job,
        // skip publish through needs:, and report the workflow green.
        Assert.Contains("count_of", gate, StringComparison.Ordinal);
        Assert.Contains("target count is not a number", gate, StringComparison.Ordinal);

        // A history it cannot read must never read as a clean one.
        Assert.Contains("fetch-depth: 0", gate, StringComparison.Ordinal);

        // The cases that each earn their keep, named individually: every one of them is the whole
        // reason a different rule in the definition exists.
        foreach (var fixture in new[]
                 {
                     "targets-null",                      // an empty matrix reporting success
                     "source-only-collision",             // a real RID moved out of source_only
                     "published-rid-dropped",             // a shipped target quietly unadvertised
                     "declared-not-packaged-unexplained", // a declared RID nobody builds
                     "unverified-without-caveat",         // an untested floor stated flatly
                     "doc-claims-a-floor-nothing-holds",  // a page promising an OS nobody checked
                     "signing-disclosure-deleted",        // "unsigned" removed while it is true
                     "csproj-and-definition-disagree",
                     "target-with-no-package-job",
                 })
        {
            Assert.Contains(fixture, gate, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r'));

    /// <summary>The lines that are instructions, with whole-line comments and blanks dropped.</summary>
    /// <remarks>
    /// Both YAML and bash mark a comment with <c>#</c>, and this gate's header deliberately explains
    /// why <c>--with-public-origin</c> is not run on every push — so a search over raw text would
    /// fail on the documentation and pass once somebody deleted it, which is exactly backwards.
    /// </remarks>
    private static string Code(IEnumerable<string> lines) =>
        string.Join('\n', lines.Where(line => !line.TrimStart().StartsWith('#')));

    private static string RepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    /// <summary>The raw lines of one top-level job, from the line after its name to the next job.</summary>
    /// <remarks>
    /// Deliberately a second copy of <see cref="ReleaseDestinationIsCheckedTests"/>'s helper rather
    /// than a shared one, for the reason recorded there: a duplicated tool is not a duplicated
    /// claim, and lifting it out would make this change touch a gate it has no business touching.
    /// </remarks>
    private static List<string> JobBlock(string workflow, string job)
    {
        var lines = workflow.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(line => line == $"  {job}:");
        Assert.True(start >= 0, $"the workflow has no job named '{job}'");

        var block = new List<string>();
        for (var i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var isNextJob = line.Length > 2 && line[0] == ' ' && line[1] == ' ' && line[2] != ' ' && line[2] != '#';
            if (isNextJob)
            {
                break;
            }

            block.Add(line);
        }

        return block;
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
