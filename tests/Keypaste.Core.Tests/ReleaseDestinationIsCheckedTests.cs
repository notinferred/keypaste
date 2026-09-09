using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Asserts that the release still refuses a destination it cannot positively verify
/// (docs/STEPS.md F.4a, docs/PRODUCT.md laws 3.7 and 4.7).
/// </summary>
/// <remarks>
/// <para>
/// A tripwire, like <see cref="CompatGateIsPermanentTests"/> — but it carries more weight than
/// that one, and the difference is worth stating. There, branch protection is the mechanism and
/// the test is only notice: the compat jobs are required checks, so a deleted job never reports
/// and the pull request cannot merge. <b>The publish job runs on tags only and is required by
/// nothing.</b> Nothing reports it missing, and a silent regression here is discovered by a real
/// tag against a public bucket, which is the one place this project cannot take a mistake back.
/// </para>
/// <para>
/// The claims are named one at a time rather than checked as a lump, because dropping any single
/// one leaves a script that still passes and a gate that has stopped gating. The positive control
/// is the likeliest to be removed as redundant — it is the only assertion that looks like it is
/// checking something unrelated to the destination, and it is the one that catches a listing that
/// answers "empty" to every question.
/// </para>
/// </remarks>
public sealed class ReleaseDestinationIsCheckedTests
{
    [Fact]
    public void TheReleaseWorkflow_ChecksTheDestinationThroughTheScriptAFixtureCanDrive()
    {
        var release = RepositoryFile(".github", "workflows", "release.yml");

        // The upload goes through the one implementation. Inlining it again would put the decision
        // back where no test can reach it, which is how it went unnoticed for two releases.
        Assert.Contains("scripts/publish-release.sh --publish", release, StringComparison.Ordinal);

        // And a dispatch exercises that implementation, which is the only run that can: the publish
        // job never runs on one, so an assumption checked only there is first tested by a tag.
        Assert.Contains(
            "scripts/verify-release-destination.sh --with-real-aws",
            release,
            StringComparison.Ordinal);

        var ci = RepositoryFile(".github", "workflows", "ci.yml");
        Assert.Contains(
            "scripts/verify-release-destination.sh --with-real-aws",
            ci,
            StringComparison.Ordinal);

        // --with-real-aws is what asks the runner's actual CLI how it fails. Without it the gate
        // still passes, against a fake that could by then be describing a CLI nobody has.
        Assert.DoesNotContain("continue-on-error:", release, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three suppressions that made F.4a, asserted absent from the job that writes to a public
    /// bucket — and only from that job.
    /// </summary>
    /// <remarks>
    /// Scoped to the block rather than the file on purpose. The build job legitimately carries
    /// <c>codesign … || true</c> and <c>--version 2&gt;/dev/null</c>, so a whole-file search would
    /// fail for reasons that have nothing to do with a release destination, and the obvious way to
    /// make it pass again would be to weaken it.
    /// </remarks>
    [Fact]
    public void ThePublishJob_SuppressesNothingOnTheWayToTheBucket()
    {
        var release = RepositoryFile(".github", "workflows", "release.yml");
        var publish = Code(JobBlock(release, "publish"));

        foreach (var suppression in new[] { "|| true", "2>/dev/null", "aws s3 " })
        {
            Assert.DoesNotContain(suppression, publish, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThePublisher_KeepsTheClaimsThatMakeItRefuse()
    {
        var publisher = RepositoryFile("scripts", "publish-release.sh");

        // Two modes, and no default: a mis-wired step must not be able to upload by accident.
        Assert.Contains("--check|--publish)", publisher, StringComparison.Ordinal);

        // The listing is read, not weighed. `aws s3 ls` cannot tell "matched nothing" from
        // "failed", which is what the deleted `|| true` was reached for. Asked of the code and not
        // the prose: the header quotes the line being replaced, and the same distinction is why
        // CompatGateIsPermanentTests matches `continue-on-error:` with its colon.
        Assert.Contains("s3api list-objects-v2", publisher, StringComparison.Ordinal);
        Assert.DoesNotContain("aws s3 ls", Code(Lines(publisher)), StringComparison.Ordinal);

        // The positive control. Without it the refusal rests on a broken read path failing loudly,
        // which is the assumption that failed the first time.
        Assert.Contains("PROBE_PREFIX", publisher, StringComparison.Ordinal);
        Assert.Contains("the positive control found nothing", publisher, StringComparison.Ordinal);

        // An empty VERSION is set-but-empty, so `set -u` never sees it and the prefix becomes `v/`,
        // which no published key matches — an honestly empty destination nothing documents.
        Assert.Contains("VERSION_SHAPE", publisher, StringComparison.Ordinal);

        // A bypass may not be added without a diff that says so (D-0092).
        Assert.DoesNotContain("--force", Code(Lines(publisher)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGate_KeepsItsNegativeControlAndEveryScenarioThatEarnsItsKeep()
    {
        var gate = RepositoryFile("scripts", "verify-release-destination.sh");

        Assert.Contains("NEGATIVE CONTROL", gate, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", gate, StringComparison.Ordinal);

        // The negative control is the pre-fix line itself. If it stops being able to reach an
        // upload, the fixture is no longer reproducing the condition it exists to catch (D-0043).
        Assert.Contains("2>/dev/null || true", gate, StringComparison.Ordinal);

        // The scenarios a checker could pass while still being wrong. Named individually because
        // each one is the whole reason a different assertion in the publisher exists.
        foreach (var scenario in new[]
                 {
                     "always-empty",   // every listing succeeds and every listing says nothing is there
                     "denied-quiet",   // nonzero, and not one word on stderr
                     "empty-stdout",   // exit 0, nothing to parse
                     "truncated-no-keys",
                     "wrong-prefix",   // an answer about a version nobody asked about
                     "wrong-bucket",
                 })
        {
            Assert.Contains(scenario, gate, StringComparison.Ordinal);
        }

        // Both halves of the measurement V-F.4a actually asks for.
        Assert.Contains("write call(s)", gate, StringComparison.Ordinal);
        Assert.Contains("byte-identical", gate, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r'));

    /// <summary>
    /// The lines that are instructions, with whole-line comments and blanks dropped.
    /// </summary>
    /// <remarks>
    /// Both YAML and bash mark a comment with <c>#</c>, and both files deliberately quote the very
    /// text asserted absent: release.yml's allowlist step explains why <c>aws s3 cp --recursive</c>
    /// needs dotglob, and publish-release.sh's header quotes the fail-open line it replaced. A
    /// search over the raw text fails on the documentation and passes once somebody deletes it,
    /// which is exactly backwards.
    /// </remarks>
    private static string Code(IEnumerable<string> lines) =>
        string.Join('\n', lines.Where(line => !line.TrimStart().StartsWith('#')));

    private static string RepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    /// <summary>
    /// The raw lines of one top-level job, from the line after its name to the next job.
    /// </summary>
    /// <remarks>
    /// Deliberately a second copy of <see cref="CompatGateIsPermanentTests"/>'s helper rather than
    /// a shared one. That file is a tripwire for a different law, and lifting a method out of it
    /// would make this change touch a gate it has no business touching. A duplicated tool is not a
    /// duplicated claim.
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
