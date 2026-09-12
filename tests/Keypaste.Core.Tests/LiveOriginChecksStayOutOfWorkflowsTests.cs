using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// No workflow asks keypaste.com anything (DECISIONS.md D-0127).
/// </summary>
/// <remarks>
/// <para>
/// keypaste.com deploys from Cloudflare's Git integration, so nothing in <c>.github/workflows</c>
/// publishes it and nothing there is positioned to check what was published.
/// <c>verify-site-disclosure.sh</c> against the live origin is a by-hand check, and a workflow that
/// ran it would go red for a Cloudflare outage no job in this repository can fix — on <c>main</c>,
/// or across a release.
/// </para>
/// <para>
/// The tempting edit is to add it somewhere "for coverage", which is why this is asserted over
/// every workflow rather than over the one that exists today. <c>--selftest</c> is the half that
/// belongs in CI: it needs no network and holds the script's reader to its job.
/// </para>
/// <para>
/// This replaces <c>SiteDeployVerifiesWhatItPublishedTests</c>, which guarded four edits to a
/// <c>site.yml</c> that deployed nothing: the <c>keypaste.com</c> environment never held a
/// <c>CLOUDFLARE_API_TOKEN</c>, so all four of its runs failed at the deploy step and every check
/// after it was skipped. A tripwire on a workflow that cannot run is not a gate.
/// </para>
/// </remarks>
public sealed class LiveOriginChecksStayOutOfWorkflowsTests
{
    [Fact]
    public void TheLiveOriginCheckRunsInNoWorkflow()
    {
        var workflows = Directory.GetFiles(
            Path.Combine(RepoRoot(), ".github", "workflows"),
            "*.yml");

        // Without this the sweep below passes on a directory that stopped naming the script at all,
        // which is the same state as having deleted the gate.
        var selftests = 0;
        List<string> live = [];

        foreach (var workflow in workflows)
        {
            foreach (var line in File.ReadAllLines(workflow))
            {
                if (line.TrimStart().StartsWith('#')
                    || !line.Contains("verify-site-disclosure.sh", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains("--selftest", StringComparison.Ordinal))
                {
                    selftests++;
                }
                else
                {
                    live.Add($"{Path.GetFileName(workflow)}:{line.Trim()}");
                }
            }
        }

        Assert.True(
            live.Count == 0,
            "a workflow asks the live origin, which goes red for weather rather than for a defect:\n  "
            + string.Join("\n  ", live));

        Assert.True(
            selftests > 0,
            "no workflow runs `verify-site-disclosure.sh --selftest`, so the half of the check that "
            + "needs no network is not running anywhere");
    }

    /// <summary>The checkout this test is running from.</summary>
    /// <remarks>
    /// A copy rather than a shared helper, for the reason ReleaseMatrixIsPinnedTests records beside
    /// its own: a duplicated tool is not a duplicated claim, and lifting it out would make a change
    /// here touch gates it has no business touching.
    /// </remarks>
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
