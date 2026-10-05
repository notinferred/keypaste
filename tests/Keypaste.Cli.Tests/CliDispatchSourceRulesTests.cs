using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Only the CLI's <c>Program.cs</c> names the bridge or its SDK, so no verb, and no verb that opens a vault,
/// can run the SDK's code (PRODUCT §3.9, D-0418, D-0419).
/// </summary>
/// <remarks>
/// A text scan, in which a name in a comment counts too. <c>Program.cs</c> is exempt by its path; what it
/// may do before it hands <c>keypaste mcp</c> to the bridge is held by the bridge's own source rule.
/// </remarks>
public sealed class CliDispatchSourceRulesTests
{
    private const string _dispatch = "Program.cs";

    private static readonly string[] _bridgeMarkers = ["Keypaste.Mcp", "ModelContextProtocol"];

    [Fact]
    public void Only_Program_cs_names_the_bridge_assembly()
    {
        var offenders = Sources()
            .Where(source => source.Name != _dispatch)
            .SelectMany(source => Offenders(source.Name, source.Text))
            .ToList();

        Assert.True(offenders.Count == 0, $"these CLI files name the bridge: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_reads_every_cli_file_and_exempts_only_the_dispatch()
    {
        var names = Sources().Select(source => source.Name).ToList();

        Assert.Contains(_dispatch, names);
        Assert.NotEmpty(Offenders(_dispatch, Read(_dispatch)));
        Assert.True(names.Count >= 60, $"only {names.Count} files were scanned");
    }

    [Fact]
    public void A_verb_that_names_the_bridge_is_found()
    {
        var app = Read("CliApp.cs");
        var agent = Read(Path.Combine("Commands", "AgentCommand.cs"));

        Assert.Empty(Offenders("CliApp.cs", app));
        Assert.NotEmpty(Offenders("CliApp.cs", "using Keypaste.Mcp;\n" + app));
        Assert.Empty(Offenders("AgentCommand.cs", agent));
        Assert.NotEmpty(Offenders("AgentCommand.cs", "using ModelContextProtocol.Server;\n" + agent));
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        _bridgeMarkers.Where(marker => text.Contains(marker, StringComparison.Ordinal)).Select(marker => $"{name}: {marker}");

    private static IEnumerable<(string Name, string Text)> Sources()
    {
        var cli = Path.Combine(RepoRoot(), "src", "Keypaste.Cli");

        return Directory.GetFiles(cli, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(cli, path), File.ReadAllText(path)));
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Cli", relative));

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new Xunit.Sdk.XunitException("keypaste.slnx not found above the test's base directory");
        }

        return directory;
    }
}
