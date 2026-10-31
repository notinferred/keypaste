using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Build invariants of <c>.github/workflows</c>, which no compiler reads.</summary>
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
    public void AppYmlRunsOnEveryPushToMainThatChangesAFileItsProjectsCompile()
    {
        var root = RepoRoot();
        var triggers = PushPaths(File.ReadAllLines(Path.Combine(root, ".github", "workflows", "app.yml")));

        Assert.NotEmpty(triggers);

        // What verify.sh's desktop profile builds: the app solution and the consistency project.
        var solution = XDocument.Load(Path.Combine(root, "keypaste.app.slnx"));
        List<string> built = [.. solution.Descendants().Where(element => element.Name.LocalName == "Project").Select(project => project.Attribute("Path")?.Value ?? string.Empty)];
        built.Add("tests/Keypaste.Consistency.Tests/Keypaste.Consistency.Tests.csproj");

        var uncovered = Uncovered(
            CompiledSources(built, project => XDocument.Load(Path.Combine(root, project))),
            triggers);

        Assert.True(
            uncovered.Count == 0,
            "app.yml compiles these, and a push to main that changes only one of them runs no app.yml:\n  "
            + string.Join("\n  ", uncovered));
    }

    [Fact]
    public void CoverageFollowsReferencesAndLinksAndReadsOnlyPushPaths()
    {
        var workflow = """
            on:
              push:
                branches: [main]
                paths:
                  - 'src/App/**'
                  - 'tests/Shared/Covered.cs'
                tags:
                  - 'tests/Shared/Tagged.cs'
              pull_request:
                paths:
                  - 'src/Lib/**'
            """;
        Dictionary<string, string> projects = new(StringComparer.Ordinal)
        {
            ["src/App/App.csproj"] = """
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="../Lib/Lib.csproj" />
                    <Compile Include="Inside/Own.cs" />
                    <Compile Include="../../tests/Shared/Covered.cs" />
                    <Compile Include="..\..\tests\Shared\Tagged.cs" />
                  </ItemGroup>
                </Project>
                """,
            ["src/Lib/Lib.csproj"] = "<Project />",
        };

        Assert.Equal(
            ["src/Lib/", "tests/Shared/Tagged.cs"],
            Uncovered(
                CompiledSources(["src/App/App.csproj"], project => XDocument.Parse(projects[project])),
                PushPaths(workflow.Split('\n'))));
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

    private static List<string> PushPaths(string[] lines)
    {
        List<string> paths = [];
        var root = string.Empty;
        var trigger = string.Empty;
        var key = string.Empty;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - trimmed.Length;
            var name = Regex.Match(trimmed, @"^([A-Za-z0-9_-]+):").Groups[1].Value;
            if (indent == 0)
            {
                root = name;
                trigger = string.Empty;
                key = string.Empty;
            }
            else if (indent == 2)
            {
                trigger = name;
                key = string.Empty;
            }
            else if (indent == 4)
            {
                key = name;
            }
            else if (root == "on" && trigger == "push" && key == "paths" && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                paths.Add(trimmed[2..].Trim().Trim('\'', '"'));
            }
        }

        return paths;
    }

    // Each project's folder, ending in a slash, and each file it compiles from outside that folder, for the
    // projects named and every project they reference.
    private static List<string> CompiledSources(List<string> projects, Func<string, XDocument> load)
    {
        List<string> sources = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        Queue<string> pending = new(projects);
        while (pending.TryDequeue(out var project))
        {
            if (!seen.Add(project))
            {
                continue;
            }

            var folder = project[..(project.LastIndexOf('/') + 1)];
            sources.Add(folder);
            foreach (var item in load(project).Descendants())
            {
                if (item.Name.LocalName is not ("Compile" or "ProjectReference") || item.Attribute("Include")?.Value is not { } include)
                {
                    continue;
                }

                var path = Resolve(folder, include);
                if (item.Name.LocalName == "ProjectReference")
                {
                    pending.Enqueue(path);
                }
                else if (!path.StartsWith(folder, StringComparison.Ordinal))
                {
                    sources.Add(path);
                }
            }
        }

        return sources;
    }

    private static string Resolve(string folder, string include)
    {
        List<string> parts = [.. folder.Split('/', StringSplitOptions.RemoveEmptyEntries)];
        foreach (var part in include.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                Assert.True(parts.Count > 0, $"{include} leaves the repository from {folder}");
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join("/", parts);
    }

    private static List<string> Uncovered(List<string> sources, List<string> triggers) =>
        [.. sources.Where(source => !triggers.Any(trigger => Covers(trigger, source))).Distinct().Order(StringComparer.Ordinal)];

    // An entry ending in /** covers everything under its folder; any other entry covers only the path it names.
    private static bool Covers(string trigger, string source) =>
        trigger.EndsWith("/**", StringComparison.Ordinal)
            ? source.StartsWith(trigger[..^2], StringComparison.Ordinal)
            : trigger == source;

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
