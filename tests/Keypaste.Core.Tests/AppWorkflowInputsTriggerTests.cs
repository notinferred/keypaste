using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests;

public sealed class AppWorkflowInputsTriggerTests
{
    [Theory]
    [InlineData("global.json", true)]
    [InlineData(".editorconfig", true)]
    [InlineData("Directory.Build.props", true)]
    [InlineData("Directory.Packages.props", true)]
    [InlineData("tests/Directory.Build.props", true)]
    [InlineData("scripts/verify.sh", true)]
    [InlineData("scripts/verify-local-checks.sh", true)]
    [InlineData("src/Keypaste.App/ViewModels/MainViewModel.cs", true)]
    [InlineData("tests/Keypaste.App.Tests/ExampleTests.cs", true)]
    [InlineData("docs/STEPS.md", false)]
    [InlineData("src/Keypaste.Mcp/Program.cs", false)]
    [InlineData("src/Keypaste.App.Other/Example.cs", false)]
    public void PushFilterCoversBuildInputsAndSkipsUnrelatedPaths(string path, bool triggers)
    {
        Assert.Equal(triggers, PushPaths().Any(pattern => Matches(path, pattern)));
    }

    [Fact]
    public void LinkedDesktopTestSourcesAlsoTriggerTheGate()
    {
        var root = RepoRoot();
        var projectDirectory = Path.Combine(root, "tests", "Keypaste.App.Tests");
        var project = XDocument.Load(Path.Combine(projectDirectory, "Keypaste.App.Tests.csproj"));
        var linked = project.Descendants("Compile")
            .Where(element => element.Attribute("Link") is not null)
            .Select(element => (string)element.Attribute("Include")!)
            .ToArray();
        Assert.NotEmpty(linked);

        var paths = PushPaths();
        foreach (var include in linked)
        {
            var path = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(projectDirectory, include)))
                .Replace(Path.DirectorySeparatorChar, '/');
            Assert.True(paths.Any(pattern => Matches(path, pattern)),
                $"app.yml skips changes to linked desktop test source {path}");
        }
    }

    [Fact]
    public void PullRequestsRemainUnfiltered()
    {
        var pullRequest = Block(WorkflowLines(), "  pull_request:");
        Assert.DoesNotContain(pullRequest, line =>
            line.TrimStart().StartsWith("paths:", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("paths-ignore:", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("branches:", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("branches-ignore:", StringComparison.Ordinal));
    }

    private static string[] PushPaths()
    {
        var push = Block(WorkflowLines(), "  push:");
        var paths = Block(push, "    paths:")
            .Select(line => line.Trim()[2..].Trim('\'', '"'))
            .ToArray();
        Assert.NotEmpty(paths);
        Assert.All(paths, pattern => Assert.DoesNotContain('*',
            pattern.EndsWith("/**", StringComparison.Ordinal) ? pattern[..^3] : pattern));
        Assert.All(paths, pattern => Assert.DoesNotContain('?', pattern));
        Assert.All(paths, pattern => Assert.False(pattern.StartsWith('!')));
        return paths;
    }

    private static bool Matches(string path, string pattern) =>
        pattern.EndsWith("/**", StringComparison.Ordinal)
            ? path.StartsWith(pattern[..^2], StringComparison.Ordinal)
            : path.Equals(pattern, StringComparison.Ordinal);

    private static string[] Block(string[] lines, string header)
    {
        var start = Array.FindIndex(lines, line => line == header);
        Assert.True(start >= 0, $"app.yml is missing {header.Trim()}");
        var indent = header.Length - header.TrimStart().Length;
        return lines.Skip(start + 1)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .TakeWhile(line => line.Length - line.TrimStart().Length > indent)
            .ToArray();
    }

    private static string[] WorkflowLines() =>
        File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "app.yml"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "keypaste.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("no keypaste.slnx above " + AppContext.BaseDirectory);
    }
}
