namespace Keypaste.Mcp;

/// <summary>The one entry the CLI calls to run the bridge, dispatched before any vault access (D-0418).</summary>
internal static class BridgeEntry
{
    internal static Task<int> RunAsync(string[] args) => Program.Main(args);
}
