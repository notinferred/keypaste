using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>Which arguments <c>keypaste</c> hands to the MCP bridge rather than to its verbs (D-0418).</summary>
public sealed class ProgramDispatchTests
{
    [Theory]
    [InlineData("mcp")]
    [InlineData("mcp", "help")]
    [InlineData("mcp", "-h")]
    [InlineData("mcp", "--help")]
    [InlineData("mcp", "--vault", "vault.kdbx", "--client-label", "claude-code")]
    [InlineData("mcp", "--allow-run")]
    public void These_start_the_bridge(params string[] args) => Assert.True(Program.StartsBridge(args));

    [Theory]
    [InlineData("mcp", "serve")]
    [InlineData("mcp", "serve", "--help")]
    [InlineData("mcp", "setup", "--client", "claude-code")]
    [InlineData("mcp", "policy", "--json")]
    [InlineData("agent")]
    [InlineData("--help")]
    [InlineData("policy", "mcp")]
    public void These_reach_the_verbs(params string[] args) => Assert.False(Program.StartsBridge(args));

    [Fact]
    public void No_arguments_reach_the_verbs() => Assert.False(Program.StartsBridge([]));
}
