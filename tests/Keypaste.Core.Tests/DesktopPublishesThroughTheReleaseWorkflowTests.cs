using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Asserts that a published desktop package is the one app.yml built and attested for the tag
/// (docs/STEPS.md 4.7c).
/// </summary>
/// <remarks>
/// <para>
/// A tripwire for the same reason as <see cref="ReleaseDestinationIsCheckedTests"/>: the publish
/// job runs on tags only and is required by nothing, so the wiring below is first exercised
/// against a public, immutable prefix. <c>verify-release-matrix.sh</c> owns the claim that the
/// release workflow resolves the app run at all; what is here is the rest of the path that a
/// fixture cannot reach, because no fixture can run a GitHub job.
/// </para>
/// <para>
/// The claims are named one at a time. Dropping any one leaves a workflow that still publishes —
/// with bytes it rebuilt, or bytes nothing attested, or no permission to read the run it named.
/// </para>
/// </remarks>
public sealed class DesktopPublishesThroughTheReleaseWorkflowTests
{
    [Fact]
    public void ThePublishJob_TakesTheDesktopPackagesFromTheAppRunAndVerifiesThem()
    {
        var release = RepositoryFile(".github", "workflows", "release.yml");
        var publish = Code(JobBlock(release, "publish"));

        // The bytes come from the app run rather than a rebuild, so what a runner installed in 4.7b
        // and upgraded in 4.7d is what reaches the origin.
        Assert.Contains("run-id: ${{ needs.guard.outputs.app_run_id }}", publish, StringComparison.Ordinal);

        // Downloading another workflow run's artifacts needs this, and without it the step fails
        // only on a tag.
        Assert.Contains("actions: read", publish, StringComparison.Ordinal);

        // And every candidate is held to app.yml's own attestation for this tag before it is staged.
        Assert.Contains("scripts/verify-desktop-candidate.sh", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublishJob_NamesNoPackageExtensionOfItsOwn()
    {
        var release = RepositoryFile(".github", "workflows", "release.yml");
        var publish = Code(JobBlock(release, "publish"));

        // A literal .msi or .AppImage here makes verify-release-matrix.sh's validate_signing demand
        // that this workflow sign an installer it does not build, which it must never do. Every
        // desktop name comes from release-completion.sh instead. Asserted rather than remembered:
        // the obvious way to widen a glob is to write the extension.
        foreach (var extension in new[] { ".msi", ".AppImage" })
        {
            Assert.DoesNotContain(extension, publish, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheGuard_ResolvesTheAppRunThroughTheScriptAFixtureCanDrive()
    {
        var release = RepositoryFile(".github", "workflows", "release.yml");

        // The decision lives in the script verify-green-gates.sh drives with a fake gh, not in a
        // step body only a tag reaches (D-0109).
        Assert.Contains("require-green-gates.sh --run-id app", release, StringComparison.Ordinal);
        Assert.Contains("app_run_id: ${{ steps.gates.outputs.run_id }}", release, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefinition_SeparatesWhoBuildsTheDesktopFromWhoAttestsWhatIsPublished()
    {
        var definition = RepositoryFile("release-targets.json");
        using var document = System.Text.Json.JsonDocument.Parse(definition);
        var app = document.RootElement.GetProperty("components").GetProperty("app");

        // app.yml builds it; release.yml attests what it publishes. Collapsing the two would make
        // verify-provenance.sh check the published packages against the workflow that only built
        // them, and the release bundle would then attest nothing anyone could check.
        Assert.Equal(".github/workflows/app.yml", app.GetProperty("workflow").GetString());
        Assert.Equal(
            ".github/workflows/release.yml",
            app.GetProperty("provenance").GetProperty("workflow").GetString());

        // The two components publish into one prefix, so their manifests must not collide.
        var cli = document.RootElement.GetProperty("components").GetProperty("cli");
        Assert.NotEqual(
            cli.GetProperty("provenance").GetProperty("manifest_pattern").GetString(),
            app.GetProperty("provenance").GetProperty("manifest_pattern").GetString());
    }

    private static string Code(IEnumerable<string> lines) =>
        string.Join('\n', lines.Where(line => !line.TrimStart().StartsWith('#')));

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
                    $"Could not locate keypaste.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }

    /// <summary>
    /// The raw lines of one top-level job, from the line after its name to the next job.
    /// </summary>
    /// <remarks>
    /// A third copy of the helper <see cref="ReleaseDestinationIsCheckedTests"/> and
    /// <see cref="CompatGateIsPermanentTests"/> each keep, for the reason the second one records: a
    /// duplicated tool is not a duplicated claim, and lifting it out would make this change touch
    /// gates it has no business touching.
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
}
