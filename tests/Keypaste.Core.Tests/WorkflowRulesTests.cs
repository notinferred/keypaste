using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

public sealed class WorkflowRulesTests
{
    [Fact]
    public void EveryActionIsPinnedToACommitAndNotToATag()
    {
        var workflows = Directory.GetFiles(
            Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");

        Assert.NotEmpty(workflows);

        List<string> unpinned = [];
        foreach (var file in workflows)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"uses:\s*(?<ref>[^\s#]+)");
                if (!match.Success)
                {
                    continue;
                }

                var reference = match.Groups["ref"].Value;

                // A local action or reusable workflow is this repository's own file, versioned with it.
                if (reference.StartsWith('.'))
                {
                    continue;
                }

                var at = reference.LastIndexOf('@');
                var pin = at < 0 ? string.Empty : reference[(at + 1)..];

                if (!Regex.IsMatch(pin, "^[0-9a-f]{40}$"))
                {
                    unpinned.Add($"{Path.GetFileName(file)}:{i + 1} {reference}");
                }
            }
        }

        Assert.True(
            unpinned.Count == 0,
            "these actions are pinned to something mutable:\n  "
            + string.Join("\n  ", unpinned));
    }

    [Fact]
    public void EveryScriptAWorkflowRunsDirectlyIsCommittedExecutable()
    {
        var invocations = DirectScriptInvocations();

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

    [Fact]
    public void DiscoveryReadsRunCommandsAndIgnoresMetadataAndInterpretedScripts()
    {
        var workflow = """
            on:
              push:
                paths:
                  - 'scripts/path-filter.sh'
            env:
              EXAMPLE: scripts/environment.sh
            jobs:
              gate:
                steps:
                  - name: scripts/step-name.sh
                    run: scripts/inline.sh
                  - run: 'scripts/quoted-inline.sh --check'
                  - run: |
                      # scripts/comment.sh
                      scripts/block.sh --check
                      bash scripts/bash-block.sh
                      sh scripts/sh-block.sh
                      source scripts/source-block.sh
                    env:
                      EXAMPLE: scripts/after-block.sh
                  - run: >-
                      scripts/folded.sh
                      --check
                  - run: bash scripts/bash-inline.sh
                  - run: sh scripts/sh-inline.sh
                  - run: source scripts/source-inline.sh
            """;

        var found = DirectScriptInvocations(workflow.Split('\n')).Select(invocation => invocation.Script);
        Assert.Equal(
            ["scripts/inline.sh", "scripts/quoted-inline.sh", "scripts/block.sh", "scripts/folded.sh"],
            found);
    }

    [Fact]
    public void OnlyDevYmlCanNarrowCiOrAppToLinux()
    {
        var workflows = Directory.GetFiles(
            Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");

        Assert.NotEmpty(workflows);

        Dictionary<string, string[]> contents = new(StringComparer.Ordinal);
        foreach (var file in workflows)
        {
            contents[Path.GetFileName(file)] = File.ReadAllLines(file);
        }

        var misuses = LinuxOnlyMisuses(contents).ToList();

        Assert.True(
            misuses.Count == 0,
            "a counted ci or app run could run on Linux alone (D-0460):\n  "
            + string.Join("\n  ", misuses));
    }

    [Fact]
    public void LinuxOnlyIsAllowedOnlyAsACiOrAppCallInputOrInDevYml()
    {
        var dispatchInputs = """
            on:
              workflow_dispatch:
                inputs:
                  linux_only:
                    type: boolean
                  LINUX_ONLY:
                    type: boolean
            """;
        var aliasedCallInput = """
            on:
              workflow_call:
                inputs: &inputs
                  linux_only:
                    type: boolean
              workflow_dispatch:
                inputs: *inputs
              pull_request:
                <<: [*inputs]
            """;
        var callInputAndCaller = """
            on:
              workflow_call:
                inputs:
                  linux_only:
                    type: boolean
            jobs:
              app:
                uses: ./.github/workflows/app.yml
                with:
                  linux_only: true
            """;
        Dictionary<string, string[]> workflows = new(StringComparer.Ordinal)
        {
            ["ci.yml"] = dispatchInputs.Split('\n'),
            ["app.yml"] = aliasedCallInput.Split('\n'),
            ["release.yml"] = callInputAndCaller.Split('\n'),
            ["dev.yml"] = callInputAndCaller.Split('\n'),
        };

        Assert.Equal(
            [
                "app.yml:7 reuses a YAML node under on:, which could carry linux_only to another trigger",
                "app.yml:9 reuses a YAML node under on:, which could carry linux_only to another trigger",
                "ci.yml:4 sets linux_only outside a ci or app workflow_call input and dev.yml",
                "ci.yml:6 sets linux_only outside a ci or app workflow_call input and dev.yml",
                "release.yml:4 sets linux_only outside a ci or app workflow_call input and dev.yml",
                "release.yml:10 sets linux_only outside a ci or app workflow_call input and dev.yml",
            ],
            LinuxOnlyMisuses(workflows));
    }

    private static Dictionary<string, string> DirectScriptInvocations()
    {
        var workflows = Directory.GetFiles(
            Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");

        Assert.NotEmpty(workflows);

        Dictionary<string, string> found = new(StringComparer.Ordinal);
        foreach (var file in workflows)
        {
            foreach (var (script, line) in DirectScriptInvocations(File.ReadAllLines(file)))
            {
                found.TryAdd(script, $"{Path.GetFileName(file)}:{line}");
            }
        }

        return found;
    }

    private static IEnumerable<(string Script, int Line)> DirectScriptInvocations(string[] lines)
    {
        foreach (var (command, line) in RunCommands(lines))
        {
            foreach (Match match in Regex.Matches(command, @"scripts/[A-Za-z0-9._-]+\.sh"))
            {
                var before = command[..match.Index].TrimEnd();
                if (before.EndsWith("bash", StringComparison.Ordinal)
                    || before.EndsWith("sh", StringComparison.Ordinal)
                    || before.EndsWith("source", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (match.Value, line);
            }
        }
    }

    private static IEnumerable<(string Command, int Line)> RunCommands(string[] lines)
    {
        int? blockIndent = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (blockIndent is int indent && line.Length - trimmed.Length > indent)
            {
                yield return (trimmed, i + 1);
                continue;
            }

            blockIndent = null;
            var run = Regex.Match(line, @"^\s*(?:-\s+)?run:\s*(.*)$");
            if (!run.Success)
            {
                continue;
            }

            var value = run.Groups[1].Value.Trim();
            if (Regex.IsMatch(value, @"^[|>][+-]?(?:\s+#.*)?$"))
            {
                blockIndent = line.IndexOf("run:", StringComparison.Ordinal);
            }
            else
            {
                yield return (value.Trim('\'', '"'), i + 1);
            }
        }
    }

    private static IEnumerable<string> LinuxOnlyMisuses(Dictionary<string, string[]> workflows)
    {
        foreach (var (name, lines) in workflows.OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            var root = string.Empty;
            var trigger = string.Empty;
            var section = string.Empty;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var indent = line.Length - trimmed.Length;
                var key = Regex.Match(trimmed, @"^([A-Za-z0-9_-]+):").Groups[1].Value;
                if (indent == 0)
                {
                    root = key;
                    trigger = string.Empty;
                    section = string.Empty;
                }
                else if (indent == 2)
                {
                    trigger = key;
                    section = string.Empty;
                }
                else if (indent == 4)
                {
                    section = key;
                }

                var countedTriggers = (name == "ci.yml" || name == "app.yml") && root == "on";
                if (countedTriggers
                    && Regex.IsMatch(trimmed, @"^(?:-\s+)?\*[^\s,\[\]{}]|:\s+\*[^\s,\[\]{}]|(?:^|[{,]\s*)<<\s*:"))
                {
                    yield return $"{name}:{i + 1} reuses a YAML node under on:, which could carry linux_only to another trigger";
                    continue;
                }

                // GitHub matches input names in any case; outside on:, app.yml's step env LINUX_ONLY must not match.
                var options = countedTriggers ? RegexOptions.IgnoreCase : RegexOptions.None;
                if (!Regex.IsMatch(trimmed, @"(?<![\w.-])linux_only['""]?\s*:", options))
                {
                    continue;
                }

                var declaredByCiOrApp = countedTriggers
                    && trigger == "workflow_call" && section == "inputs"
                    && indent == 6 && key == "linux_only";
                if (!declaredByCiOrApp && name != "dev.yml")
                {
                    yield return $"{name}:{i + 1} sets linux_only outside a ci or app workflow_call input and dev.yml";
                }
            }
        }
    }

    private static Dictionary<string, string> IndexModes()
    {
        // Windows with core.filemode=false cannot report the executable bits a runner will receive.
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
            // git ls-files emits "<mode> <object> <stage>\t<path>".
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            Assert.True(tab > 0, $"unexpected git ls-files output: {line}");
            modes[line[(tab + 1)..].TrimEnd('\r')] = line[..line.IndexOf(' ', StringComparison.Ordinal)];
        }

        Assert.NotEmpty(modes);
        return modes;
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
