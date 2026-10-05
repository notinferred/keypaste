using Keypaste.Cli.Output;
using Keypaste.Core.Clients;
using Keypaste.Mcp;

namespace Keypaste.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (StartsBridge(args))
        {
            return await BridgeEntry.RunAsync(args[1..]).ConfigureAwait(false);
        }

        return CliApp.Run(args, StandardStreams.Output(), StandardStreams.Error());
    }

    /// <summary>Whether <paramref name="args"/> start the MCP bridge, decided from the arguments alone so no vault code runs first (D-0418).</summary>
    internal static bool StartsBridge(string[] args) =>
        args is [McpServerLocator.BridgeArgument, ..] && args is not [_, "serve" or "setup" or "policy", ..];
}
