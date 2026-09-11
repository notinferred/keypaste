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
            Code(JobBlock(RepositoryFile(".github", "workflows", "release.yml"), "guard")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheGitHistoryCheck_IsGivenAHistoryToRead()
    {
        var ci = RepositoryFile(".github", "workflows", "ci.yml");
        var gate = Code(JobBlock(ci, "gate"));

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

        foreach (var workflow in new[] { "ci.yml", "release.yml", "app.yml", "site.yml" })
        {
            Assert.DoesNotContain(
                "--with-public-origin",
                Code(Lines(RepositoryFile(".github", "workflows", workflow))),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The job that emits a matrix refuses to emit an empty one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fail-open the whole derived-matrix design rests on, and nothing else catches it.
    /// <c>fromJSON</c> of an empty array expands to zero legs; GitHub then <em>skips</em> the build
    /// job, skips <c>publish</c> through <c>needs:</c>, and reports the workflow <b>green</b> — a
    /// release that built nothing and said so nowhere.
    /// </para>
    /// <para>
    /// <c>verify-release-matrix.sh</c> cannot hold this. It judges the definition, and a definition
    /// with no targets is exactly what it refuses; but the workflow step that reads a <em>valid</em>
    /// definition and emits its matrix is a step body, and no fixture can drive one. Deleting those
    /// four lines from <c>release.yml</c> leaves the gate at exit 0 and every other test passing,
    /// which is measured rather than assumed — see the R.0a evidence in docs/STEPS.md.
    /// </para>
    /// <para>
    /// Asserted on the error text rather than on the shell, because the shape of the check may
    /// reasonably change and its refusal may not. D-0106's rule is the thing being protected: the
    /// count must be digits, not a truthy exit status.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryJobThatEmitsAMatrix_RefusesToEmitAnEmptyOne()
    {
        foreach (var workflow in new[] { "release.yml", "app.yml" })
        {
            var text = RepositoryFile(".github", "workflows", workflow);

            Assert.True(
                text.Contains("fromJSON", StringComparison.Ordinal),
                $"{workflow} no longer derives its matrix; this test guards the derivation.");

            Assert.True(
                text.Contains("did not answer with a target count", StringComparison.Ordinal),
                $"{workflow} emits a matrix without requiring the count to be digits. An empty "
                + "matrix makes GitHub skip the job, skip what needs: it, and report the workflow "
                + "green. Restore the `case \"$n\" in ''|*[!0-9]*)` guard in its targets step.");
        }

        // release.yml alone also refuses a definition that parses but advertises nothing, because
        // it is the workflow that publishes.
        Assert.Contains(
            "advertises no cli targets",
            RepositoryFile(".github", "workflows", "release.yml"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The tag guard calls the script, and the script's fixture runs where a branch reaches it.
    /// </summary>
    /// <remarks>
    /// The guard step is <c>if: github.ref_type == 'tag'</c>, so no dispatch and no push executes
    /// it — the shape of defect F.4a cost two releases (D-0104). The decision therefore lives in
    /// <c>require-green-gates.sh</c> and <c>verify-green-gates.sh</c> drives it through a fake
    /// <c>gh</c> on every push. What only a test can hold is that both halves stay wired: an
    /// inlined loop here would be unreachable again, and a fixture nothing runs proves nothing.
    /// </remarks>
    [Fact]
    public void TheTagGuard_CallsTheScriptAndTheScriptIsFixtureDriven()
    {
        var guard = Code(JobBlock(RepositoryFile(".github", "workflows", "release.yml"), "guard"));

        Assert.Contains("scripts/require-green-gates.sh", guard, StringComparison.Ordinal);
        Assert.Contains("scripts/verify-green-gates.sh", guard, StringComparison.Ordinal);

        // ci.yml is where a branch meets the fixture. Without this the decision is once again a
        // thing only a tag can execute.
        Assert.Contains(
            "scripts/verify-green-gates.sh",
            RepositoryFile(".github", "workflows", "ci.yml"),
            StringComparison.Ordinal);

        // It fails the tag. A warning would leave the claim that every advertised target has a
        // matching check resting on a message nobody is obliged to read.
        Assert.DoesNotContain("continue-on-error", guard, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard asks R2 whether the credentials work, rather than whether three variables are set.
    /// </summary>
    /// <remarks>
    /// D-0111's sibling and the DECISIONS idea it closes: presence is not authentication, and a
    /// stale credential restored into a replaced repository looks identical to a working one until
    /// the publish job runs — four NativeAOT builds after the tag was spent. <c>--check</c> writes
    /// nothing and refuses unless its positive control sees objects at an already-published prefix,
    /// so it separates "reaches keypaste's bucket" from "reaches a bucket". Deleting the call puts
    /// that question back behind the tag.
    /// </remarks>
    [Fact]
    public void TheGuard_AsksR2WhetherTheCredentialsWorkBeforeAnythingIsBuilt()
    {
        var guard = Code(JobBlock(RepositoryFile(".github", "workflows", "release.yml"), "guard"));

        Assert.Contains("scripts/publish-release.sh --check", guard, StringComparison.Ordinal);

        // The credentials have to reach the step, or the check answers about an empty environment.
        Assert.Contains("R2_ACCESS_KEY_ID", guard, StringComparison.Ordinal);
        Assert.Contains("R2_SECRET_ACCESS_KEY", guard, StringComparison.Ordinal);
        Assert.Contains("R2_ACCOUNT_ID", guard, StringComparison.Ordinal);

        Assert.DoesNotContain("continue-on-error", guard, StringComparison.Ordinal);
    }


    /// <summary>
    /// The three decisions a tag used to be the first thing to execute call their scripts, and the
    /// fixture that drives those scripts runs where a push reaches it.
    /// </summary>
    /// <remarks>
    /// D-0109. Each of these was a step body behind a tag gate, so its refusing direction had never
    /// run. Inlining any of them again would put it back out of reach, and a fixture nothing runs
    /// would prove nothing — so both halves are asserted, the same way the green-gate pair is.
    /// </remarks>
    [Fact]
    public void TheTagOnlyDecisions_AreScriptsAndTheirFixtureRunsOnEveryPush()
    {
        var release = RepositoryFile(".github", "workflows", "release.yml");

        Assert.Contains("scripts/require-changelog-section.sh", release, StringComparison.Ordinal);
        Assert.Contains("scripts/require-tag-matches-source.sh", release, StringComparison.Ordinal);
        Assert.Contains("scripts/require-release-assets.sh", release, StringComparison.Ordinal);

        Assert.Contains(
            "scripts/verify-release-preflight.sh",
            RepositoryFile(".github", "workflows", "ci.yml"),
            StringComparison.Ordinal);

        // The publication allowlist is the one that decides what becomes world-readable, so it is
        // named here rather than left to the blanket check above.
        var publish = Code(JobBlock(release, "publish"));
        Assert.Contains("scripts/require-release-assets.sh", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", publish, StringComparison.Ordinal);
    }

    /// <summary>
    /// The required set is derived from the definition, and app.yml is in it today.
    /// </summary>
    [Fact]
    public void TheRequiredGates_AreReadFromTheDefinitionAndIncludeTheDesktopGate()
    {
        var script = RepositoryFile("scripts", "require-green-gates.sh");

        // Read, not written out: a component with its own gate is required without an edit.
        Assert.Contains(".components[] | select(.workflow != $self)", script, StringComparison.Ordinal);

        // And the release workflow never requires itself, which nothing could satisfy.
        Assert.Contains("KEYPASTE_RELEASE_WORKFLOW", script, StringComparison.Ordinal);

        // The negative control deletes exactly that line, so it has to stay on one line.
        var derived = Lines(script).Where(l => l.Contains("jq -r --arg self", StringComparison.Ordinal)).ToList();
        Assert.Single(derived);
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

        // A reader that exits early cannot decide whether a workflow contains a string: `grep -q`
        // returns at its first match, the upstream still writing takes SIGPIPE, and pipefail makes
        // a present string absent. Guard run 34400224237 refused this repository over a changelog
        // lookup release.yml has never stopped making. Restoring the pipe is the edit this catches.
        Assert.Contains("code_has", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("code_of \"$release\" | grep -q", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("code_of \"$root/$workflow\" | grep -q", gate, StringComparison.Ordinal);

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
                     "target-nothing-else-declares",
                     "race.yml",                          // the early-exit reader, put back
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
