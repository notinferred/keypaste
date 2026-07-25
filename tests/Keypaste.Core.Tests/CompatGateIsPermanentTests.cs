using System.Runtime.CompilerServices;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Asserts that the KeePassXC compatibility gate is still wired into CI (CORE.md law 4.6,
/// DECISIONS.md D-0008).
/// </summary>
/// <remarks>
/// This is a tripwire, not a lock. Anyone deliberately removing the gate will see this test
/// go red and delete it too. What it buys is converting *silent* removal — a merge-conflict
/// resolution, an over-eager "let's slim CI" change, an agent tidying YAML — into deliberate
/// removal, with a failure message that states the stakes. The mechanism that actually
/// prevents removal is branch protection: the three `keepassxc compat (...)` checks are
/// required on main, so a deleted job never reports and the pull request can never merge.
/// </remarks>
public sealed class CompatGateIsPermanentTests
{
    [Fact]
    public void CiWorkflow_StillRunsTheKeePassXcCompatibilityGate()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

        Assert.Contains("scripts/verify-keepassxc-compat.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("keepassxc", workflow, StringComparison.OrdinalIgnoreCase);

        // The fixture must come from the shipped binary. Naming the throwaway generator again
        // would silently narrow the gate back to the vault writer alone (DECISIONS.md D-0012).
        Assert.Contains("scripts/make-compat-fixture.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Keypaste.CompatFixture", workflow, StringComparison.Ordinal);

        foreach (var os in new[] { "ubuntu-latest", "windows-latest", "macos-latest" })
        {
            Assert.Contains(os, workflow, StringComparison.Ordinal);
        }

        // A gate that is allowed to fail is not a gate. Matched with the trailing colon so
        // this looks for the YAML key and not for the word — the warning comment in ci.yml
        // names `continue-on-error` as one of the things not to add, and a bare substring
        // check would be tripped by that comment.
        Assert.DoesNotContain("continue-on-error:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-compat.sh");
        Assert.True(File.Exists(script), $"The compatibility gate script is missing: {script}");

        var fixture = Path.Combine(RepoRoot(), "scripts", "make-compat-fixture.sh");
        Assert.True(File.Exists(fixture), $"The fixture generator is missing: {fixture}");

        var text = File.ReadAllText(script);

        // Without the negative control, a gate that silently stopped testing anything would
        // report green forever — the most likely way this law dies is a no-op, not a delete.
        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);

        // Absent tooling must fail the build, never skip the gate.
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary's location to the directory holding the solution.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> <see cref="CallerFilePathAttribute"/>. The root props set
    /// <c>ContinuousIntegrationBuild</c> under GitHub Actions, which turns on deterministic
    /// source paths and rewrites every compile-time path to <c>/_/…</c> — so on CI, and only
    /// on CI, a <c>CallerFilePath</c> points at a directory that has never existed. The
    /// output directory is a runtime fact and survives that.
    /// <para>
    /// <c>UseArtifactsOutput</c> puts the binary at <c>artifacts/bin/&lt;project&gt;/&lt;config&gt;/</c>
    /// inside the repository, so walking up finds <c>keypaste.slnx</c>.
    /// </para>
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
                    $"Could not locate keypaste.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
