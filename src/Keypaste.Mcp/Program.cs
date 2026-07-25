using Keypaste.Core;

namespace Keypaste.Mcp;

internal static class Program
{
    /// <summary>
    /// Placeholder for the Stage 2 MCP server. It exits non-zero on purpose: a bridge
    /// that cannot enforce approval must never look like it succeeded (CORE.md law 3.7).
    /// </summary>
    private static int Main()
    {
        Console.Error.WriteLine($"keypaste-mcp: not implemented yet (Stage 2). Linked against {CoreInfo.Hello()}");
        return 1;
    }
}
