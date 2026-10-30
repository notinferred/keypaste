using Keypaste.Browser;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>Which arguments <c>keypaste</c> hands to the MCP bridge or the browser relay rather than to its verbs (D-0418, D-0624).</summary>
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
    [InlineData("approve")]
    [InlineData("clients", "--json")]
    [InlineData("agent")]
    [InlineData("--help")]
    [InlineData("policy", "mcp")]
    public void These_reach_the_verbs(params string[] args) => Assert.False(Program.StartsBridge(args));

    /// <summary>The removed <c>mcp serve</c>, <c>setup</c> and <c>policy</c> reach the verbs, which refuse them, and never start the bridge (D-0470).</summary>
    [Theory]
    [InlineData("mcp", "serve")]
    [InlineData("mcp", "serve", "--help")]
    [InlineData("mcp", "setup")]
    [InlineData("mcp", "setup", "--client", "claude-code")]
    [InlineData("mcp", "policy")]
    [InlineData("mcp", "policy", "--json")]
    [InlineData("mcp", "policy", "cursor", "--mode", "ask")]
    public void The_removed_mcp_spellings_never_start_the_bridge(params string[] args) => Assert.False(Program.StartsBridge(args));

    [Fact]
    public void No_arguments_reach_the_verbs()
    {
        Assert.False(Program.StartsBridge([]));
        Assert.False(BrowserEntry.StartsRelay([]));
    }

    /// <summary>A browser starting its native messaging host starts the relay, never the bridge or a verb (D-0624).</summary>
    [Fact]
    public void A_browser_starting_its_host_starts_the_relay()
    {
        string[] chrome = OperatingSystem.IsWindows()
            ? ["chrome-extension://oboonakemofpalcgghocfoadofidjkkk/", "--parent-window=0"]
            : ["chrome-extension://oboonakemofpalcgghocfoadofidjkkk/"];
        string[] firefox = [Path.Combine(Path.GetTempPath(), "org.keepassxc.keepassxc_browser.json"), "keepassxc-browser@keepassxc.org"];

        Assert.True(BrowserEntry.StartsRelay(chrome));
        Assert.True(BrowserEntry.StartsRelay(firefox));
        Assert.False(Program.StartsBridge(chrome));
        Assert.False(Program.StartsBridge(firefox));
    }

    [Theory]
    [InlineData("mcp")]
    [InlineData("mcp", "--vault", "vault.kdbx")]
    [InlineData("approve")]
    [InlineData("--selftest")]
    [InlineData("get", "github")]
    public void The_bridge_and_the_verbs_never_start_the_relay(params string[] args) => Assert.False(BrowserEntry.StartsRelay(args));
}
