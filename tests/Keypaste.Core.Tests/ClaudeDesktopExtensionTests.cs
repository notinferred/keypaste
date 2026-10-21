using System.Text.RegularExpressions;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The Claude Desktop extension's launcher carries the bridge's arguments itself, so it must start the bridge exactly as
/// setup registers Claude Desktop, never offering an agent more than setup does (D-0446).
/// </summary>
public sealed class ClaudeDesktopExtensionTests
{
    [Fact]
    public void TheLauncherStartsTheBridgeAsSetupRegistersClaudeDesktop()
    {
        var launcher = File.ReadAllText(
            Path.Combine(VendoredWordList.RepoRoot(), "packaging", "claude-desktop", "server", "index.js"));
        var declared = Regex.Match(launcher, @"const bridgeArguments = \[(?<items>[^\]]*)\];");
        Assert.True(declared.Success, "the launcher declares no bridgeArguments");
        string[] started = [.. Regex.Matches(declared.Groups["items"].Value, "'(?<item>[^']*)'").Select(match => match.Groups["item"].Value)];

        var claudeDesktop = McpClientCatalog.Find("claude-desktop")!;
        Assert.True(
            McpServerRegistration.TryCreate(
                new McpServerCommand("keypaste", [McpServerLocator.BridgeArgument]), McpServerPins.None, claudeDesktop.Id, [], out var registration, out var error),
            error);

        Assert.Equal(registration.CommandLine().Skip(1), started);
    }
}
