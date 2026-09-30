using Keypaste.Cli.Output;

namespace Keypaste.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["mcp", .. var bridgeArgs])
        {
            return Keypaste.Mcp.Program.Main(bridgeArgs).GetAwaiter().GetResult();
        }

        return CliApp.Run(args, StandardStreams.Output(), StandardStreams.Error());
    }
}
