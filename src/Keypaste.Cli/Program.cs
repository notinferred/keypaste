using Keypaste.Cli.Output;

namespace Keypaste.Cli;

internal static class Program
{
    private static int Main(string[] args) => CliApp.Run(args, StandardStreams.Output(), StandardStreams.Error());
}
