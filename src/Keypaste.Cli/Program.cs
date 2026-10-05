using Keypaste.Cli.Output;
using Keypaste.Mcp;

namespace Keypaste.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "mcp" &&
            (args.Length < 2 || args[1] is not ("serve" or "setup" or "policy" or "help")))
        {
            return await BridgeEntry.RunAsync(args[1..]).ConfigureAwait(false);
        }

        return CliApp.Run(args, StandardStreams.Output(), StandardStreams.Error());
    }
}
