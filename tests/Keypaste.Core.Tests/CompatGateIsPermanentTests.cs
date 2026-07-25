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

        var text = File.ReadAllText(script);

        // Without the negative control, a gate that silently stopped testing anything would
        // report green forever — the most likely way this law dies is a no-op, not a delete.
        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);

        // Absent tooling must fail the build, never skip the gate.
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from this source file to the directory holding the solution.
    /// </summary>
    /// <remarks>
    /// <see cref="CallerFilePathAttribute"/> rather than the working directory: the test
    /// platform runs the test executable from <c>artifacts/bin/</c>, which is nowhere near
    /// the repository root. This bakes in the build machine's path, which is fine for a
    /// repository-integrity test and meaningless anywhere the sources do not exist.
    /// </remarks>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var directory = Path.GetDirectoryName(thisFile)!;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory)
                ?? throw new InvalidOperationException("Could not locate the repository root.");
        }

        return directory;
    }
}
