using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The site deploy is wired to check what it published (DECISIONS.md D-0121).
/// </summary>
/// <remarks>
/// <para>
/// A tripwire for the wiring, never a second copy of what the gates claim. `verify-site-disclosure.sh`
/// owns whether the page discloses anything and `verify-site-endpoint.sh` owns what the endpoint
/// refuses; restating either here would give two answers to one question, which is the mistake
/// `ReleaseDestinationIsCheckedTests` was written to avoid.
/// </para>
/// <para>
/// What it guards is the four edits that would leave <c>site.yml</c> looking finished and deploying
/// unchecked. Each one is a plausible thing to do on purpose, which is why none of them is caught by
/// reading the file once.
/// </para>
/// </remarks>
public sealed class SiteDeployVerifiesWhatItPublishedTests
{
    private static string Site() => RepositoryFile(".github", "workflows", "site.yml");

    /// <summary>
    /// The deploy asks the origin afterwards. Without this the workflow is a publish button, and the
    /// gap D-0121 exists to close reopens with the workflow still green.
    /// </summary>
    [Fact]
    public void TheDeployRunsTheLiveGatesAfterPublishing()
    {
        var site = Site();

        Assert.Contains("scripts/verify-site-disclosure.sh \"$SITE_URL/\"", site, StringComparison.Ordinal);
        Assert.Contains("scripts/verify-site-endpoint.sh \"$SITE_URL\"", site, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it checks the page BEFORE uploading it, so a page that does not disclose what the
    /// advertised download gets wrong never goes live in the first place.
    /// </summary>
    /// <remarks>
    /// The pre-deploy run is the one that prevents the defect; the post-deploy run only proves the
    /// bytes arrived. Losing this step would leave the workflow still red on a bad page, but only
    /// after publishing it.
    /// </remarks>
    [Fact]
    public void ThePageIsCheckedBeforeItIsUploaded()
    {
        var site = Site();

        var checkedAt = site.IndexOf("KEYPASTE_SITE_BODY", StringComparison.Ordinal);
        var deployedAt = site.IndexOf("wrangler deploy", StringComparison.Ordinal);

        Assert.True(checkedAt >= 0, "site.yml does not check the page it is about to deploy");
        Assert.True(deployedAt >= 0, "site.yml does not deploy");
        Assert.True(
            checkedAt < deployedAt,
            "site.yml checks the page after uploading it, so a non-compliant page goes live first");
    }

    /// <summary>
    /// A cancelled deploy leaves an account mid-upload, so this one is never cancelled — the
    /// deliberate inverse of <c>ci.yml</c>, where a superseded run costs nothing.
    /// </summary>
    [Fact]
    public void ADeployIsNeverCancelledMidFlight()
    {
        Assert.Contains("cancel-in-progress: false", Site(), StringComparison.Ordinal);
    }

    /// <summary>
    /// No pull-request trigger. A fork's pull request cannot read the deploy credential, and a
    /// proposed change must not reach production to be reviewed.
    /// </summary>
    /// <remarks>
    /// Asked of the code and not of the comments, which say the word in the course of explaining
    /// why the trigger is absent. A test that cannot tell the two apart makes the explanation
    /// unwritable.
    /// </remarks>
    [Fact]
    public void AProposedChangeCannotDeploy()
    {
        Assert.DoesNotContain("pull_request", Code(Site()), StringComparison.Ordinal);
    }

    /// <summary>The workflow with its comment lines removed.</summary>
    private static string Code(string workflow) =>
        string.Join(
            '\n',
            workflow.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>
    /// The live gate stays out of <c>ci.yml</c>, which deploys nothing. Wiring it there would redden
    /// <c>main</c> for a Cloudflare outage nobody in that workflow can fix — and <c>--selftest</c>,
    /// which needs no network, is what belongs there instead.
    /// </summary>
    [Fact]
    public void TheLiveGateIsNotInTheWorkflowThatCannotDeploy()
    {
        var ci = RepositoryFile(".github", "workflows", "ci.yml");

        Assert.Contains("scripts/verify-site-disclosure.sh --selftest", ci, StringComparison.Ordinal);

        foreach (var line in ci.Split('\n'))
        {
            if (line.Contains("verify-site-disclosure.sh", StringComparison.Ordinal))
            {
                Assert.Contains("--selftest", line, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Reads a repository file by path parts.</summary>
    /// <remarks>
    /// A third copy rather than a shared one, for the reason ReleaseMatrixIsPinnedTests records
    /// beside its own: a duplicated tool is not a duplicated claim, and lifting it out would make a
    /// change here touch gates it has no business touching.
    /// </remarks>
    private static string RepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    "no keypaste.slnx above " + AppContext.BaseDirectory);
            }

            directory = parent;
        }

        return directory;
    }
}
