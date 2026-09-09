using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Every script a workflow runs directly is committed executable (docs/STEPS.md F.4a/F.4b).
/// </summary>
/// <remarks>
/// <para>
/// A tripwire for a defect that has now happened twice in one day. <c>verify-publisher-metadata.sh</c>
/// was committed <c>100644</c> while two workflows invoked it directly, so its first run on a runner
/// would have been <c>Permission denied</c> rather than a check; the commit that repaired it added
/// <c>publish-release.sh</c> and <c>verify-release-destination.sh</c> with the same mode. <b>A gate
/// that cannot execute is a gate that has never run</b>, and one of those three is the only thing
/// that writes to a public bucket.
/// </para>
/// <para>
/// It reads the <em>index</em> mode, not the working tree's. This repository is developed on Windows
/// with <c>core.filemode=false</c>, where the working-tree bit is not tracked and
/// <c>File.GetUnixFileMode</c> would answer about NTFS — so a test that asked the filesystem would
/// pass vacuously on the one machine where the defect is created. The index is what reaches a runner.
/// </para>
/// <para>
/// Discovery is by pattern over every workflow rather than a list of known scripts, because a list
/// is a thing somebody adds a script without. An interpreter in front of the path — <c>bash
/// scripts/…</c> — needs no bit and is not asserted on; the same script invoked directly somewhere
/// else still is.
/// </para>
/// </remarks>
public sealed class WorkflowScriptsAreExecutableTests
{
    [Fact]
    public void EveryScriptAWorkflowRunsDirectlyIsCommittedExecutable()
    {
        var invocations = DirectScriptInvocations();

        // A pattern that quietly stopped matching would leave this file green and asserting
        // nothing, which is the failure mode the gates it protects state out loud.
        Assert.NotEmpty(invocations);

        var modes = IndexModes();

        List<string> wrong = [];
        foreach (var (script, where) in invocations.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            if (!modes.TryGetValue(script, out var mode))
            {
                wrong.Add($"{script} is run by {where} and is not committed at all");
            }
            else if (mode != "100755")
            {
                wrong.Add($"{script} is run by {where} and is committed {mode}, not 100755");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "these scripts would be 'Permission denied' on a runner:\n  "
            + string.Join("\n  ", wrong)
            + "\n\nFix with: git update-index --chmod=+x <path>");
    }

    /// <summary>
    /// Each <c>scripts/*.sh</c> a workflow invokes with no interpreter in front of it, mapped to
    /// the first place it is invoked that way.
    /// </summary>
    private static Dictionary<string, string> DirectScriptInvocations()
    {
        var workflows = Directory.GetFiles(
            Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");

        Assert.NotEmpty(workflows);

        Dictionary<string, string> found = new(StringComparer.Ordinal);
        foreach (var file in workflows)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // A comment naming a script is prose about it, not a call to it.
                if (lines[i].TrimStart().StartsWith('#'))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(lines[i], @"scripts/[A-Za-z0-9._-]+\.sh"))
                {
                    var before = lines[i][..match.Index].TrimEnd();
                    if (before.EndsWith("bash", StringComparison.Ordinal)
                        || before.EndsWith("sh", StringComparison.Ordinal)
                        || before.EndsWith("source", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    found.TryAdd(match.Value, $"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        return found;
    }

    /// <summary>The mode git records for every tracked file under <c>scripts/</c>.</summary>
    /// <remarks>
    /// Asked of git rather than of the filesystem, and a git that cannot answer fails this test
    /// rather than skipping it: an unanswerable question here is indistinguishable from a
    /// non-executable script, and the whole point is that one of those two is never noticed.
    /// </remarks>
    private static Dictionary<string, string> IndexModes()
    {
        ProcessStartInfo start = new("git", "ls-files -s -- scripts")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        string output;
        string error;
        try
        {
            using var git = Process.Start(start)
                ?? throw new InvalidOperationException("git did not start");
            output = git.StandardOutput.ReadToEnd();
            error = git.StandardError.ReadToEnd();
            git.WaitForExit();
            Assert.True(
                git.ExitCode == 0,
                $"git ls-files exited {git.ExitCode}; this test cannot read the index: {error}");
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
        {
            throw new InvalidOperationException(
                "This test reads committed file modes and needs git on PATH inside a checkout. "
                + "It is never skipped: a mode nobody could read is how the defect it exists to "
                + "catch reached two releases.",
                exception);
        }

        Dictionary<string, string> modes = new(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> <object> <stage>\t<path>"
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            Assert.True(tab > 0, $"unexpected git ls-files output: {line}");
            modes[line[(tab + 1)..].TrimEnd('\r')] = line[..line.IndexOf(' ', StringComparison.Ordinal)];
        }

        Assert.NotEmpty(modes);
        return modes;
    }

    /// <summary>
    /// Walks up from the test binary's location to the directory holding the solution.
    /// </summary>
    /// <remarks>
    /// Deliberately a second copy of <see cref="CompatGateIsPermanentTests"/>'s helper rather than
    /// a shared one, for the reason recorded there: a duplicated tool is not a duplicated claim,
    /// and lifting it out would make this change touch a gate it has no business touching.
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
