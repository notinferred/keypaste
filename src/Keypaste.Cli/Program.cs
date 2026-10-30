using Keypaste.Browser;
using Keypaste.Cli.Output;
using Keypaste.Core.Clients;
using Keypaste.Mcp;

namespace Keypaste.Cli;

internal static class Program
{
    /// <summary>The argument that runs the NaCl box's known answers in this binary and exits (D-0613).</summary>
    internal const string SelfTestFlag = "--selftest";

    private static async Task<int> Main(string[] args)
    {
        if (StartsBridge(args))
        {
            return await BridgeEntry.RunAsync(args[1..]).ConfigureAwait(false);
        }

        if (BrowserEntry.StartsRelay(args))
        {
            return await BrowserEntry.RunAsync(args).ConfigureAwait(false);
        }

        if (args is [SelfTestFlag])
        {
            return BrowserEntry.SelfTest(StandardStreams.Output(), StandardStreams.Error());
        }

        return CliApp.Run(args, StandardStreams.Output(), StandardStreams.Error());
    }

    /// <summary>
    /// Whether <paramref name="args"/> start the MCP bridge, decided from the arguments alone so no vault code runs first
    /// (D-0418). The removed <c>mcp serve</c>, <c>setup</c> and <c>policy</c> never do, so the CLI refuses them (D-0470).
    /// </summary>
    internal static bool StartsBridge(string[] args) =>
        args is [McpServerLocator.BridgeArgument, ..] && args is not [_, "serve" or "setup" or "policy", ..];
}
