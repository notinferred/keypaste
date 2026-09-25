using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary><c>keypaste mcp policy</c>: lists and sets how each MCP client is asked, in <c>clients.toml</c> (D-0360).</summary>
public sealed class McpPolicyVerbTests : IDisposable
{
    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string ClientsPath => KeypasteHome.ClientsPath(_harness.Environment[KeypasteHome.EnvironmentVariable]);

    [Fact]
    public void List_ShowsTheDefault()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy"));

        Assert.Equal(
            "  CLIENT  POLICY" + Environment.NewLine + "  *       Session grants up to 1h   (default)" + Environment.NewLine,
            _harness.Out);
    }

    [Fact]
    public void Set_WritesTheFile_AndTheListShowsIt()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "cursor", "ask"));

        Assert.Contains("  ✓ cursor · Ask every time · applies to its next request", _harness.Err, StringComparison.Ordinal);
        Assert.True(ClientPolicies.TryLoad(ClientsPath, out var written, out _));
        Assert.Equal(ClientPolicy.AskEveryTime, written!.For("cursor"));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy"));
        Assert.Contains("  cursor  Ask every time", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("  *       Session grants up to 1h   (default)", _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_ListsTheRowsOnly()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "*", "inject-only"));
        _harness.Stdout.GetStringBuilder().Clear();

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--json"));

        Assert.Equal("""[{"label":"*","policy":"inject-only"}]""" + Environment.NewLine, _harness.Out);
    }

    [Theory]
    [InlineData("has/slash", "ask")]
    [InlineData("cursor", "sometimes")]
    [InlineData("cursor", null)]
    public void BadLabelOrPolicy_IsUsage(string label, string? policy)
    {
        string[] args = policy is null ? ["mcp", "policy", label] : ["mcp", "policy", label, policy];

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(args));

        Assert.False(File.Exists(ClientsPath));
    }

    [Fact]
    public void AMalformedFile_IsRefused_AndUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClientsPath)!);
        File.WriteAllText(ClientsPath, "[[client]]\nlabel = \"cursor\"\npolicy = \"sometimes\"\n");
        var before = File.ReadAllBytes(ClientsPath);

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("mcp", "policy", "cursor", "ask"));

        Assert.Contains("fix it or delete it", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(ClientsPath));
    }
}
