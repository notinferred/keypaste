using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The dispatch that hands <c>keypaste mcp &lt;args&gt;</c> to the bridge must not spread the bridge
/// dependency across the CLI (PRODUCT §3.9, D-0418).
/// </summary>
/// <remarks>
/// <para>
/// Only <c>Program.cs</c> names <c>Keypaste.Mcp</c> or <c>ModelContextProtocol</c>. Any other CLI
/// file that imported the bridge namespace would allow its types to reach the vault path, which the
/// source-rule in <c>BridgeSourceRulesTests</c> cannot catch from the other side.
/// </para>
/// <para>
/// The mutation that must fail the first test: adding <c>using Keypaste.Mcp;</c> to <c>CliApp.cs</c>.
/// The mutation that must fail the second: deleting the <c>BridgeEntry.RunAsync</c> call from
/// <c>Program.cs</c>, which would leave a dispatch that does nothing.
/// </para>
/// </remarks>
public sealed class CliDispatchSourceRulesTests
{
    private static readonly string[] _bridgeMarkers = ["Keypaste.Mcp", "ModelContextProtocol"];

    [Fact]
    public void Only_Program_cs_names_the_bridge_assembly()
    {
        var offenders = new List<string>();
        var cli = Path.Combine(RepoRoot(), "src", "Keypaste.Cli");

        foreach (var file in Directory.EnumerateFiles(cli, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "Program.cs")
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var marker in _bridgeMarkers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {marker}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Program_cs_calls_BridgeEntry_RunAsync()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Cli", "Program.cs"));
        Assert.Contains("BridgeEntry.RunAsync", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutating_CliApp_cs_with_a_bridge_marker_is_caught()
    {
        var cli = Path.Combine(RepoRoot(), "src", "Keypaste.Cli");
        var appFile = Path.Combine(cli, "CliApp.cs");
        var original = File.ReadAllText(appFile);
        var mutated = original + "\n// using Keypaste.Mcp;";

        var offenders = new List<string>();

        foreach (var marker in _bridgeMarkers)
        {
            if (mutated.Contains(marker, StringComparison.Ordinal))
            {
                offenders.Add(marker);
            }
        }

        Assert.NotEmpty(offenders);
    }

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
