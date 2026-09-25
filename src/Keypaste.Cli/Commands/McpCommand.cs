namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste mcp serve</c> and <c>keypaste mcp setup</c>: the agent verbs under the name the MCP world looks for.</summary>
/// <remarks>
/// Aliases, not new behaviour: the MCP server itself is <c>keypaste-mcp</c>, which the client starts,
/// and <c>serve</c> is the terminal approver it talks to.
/// </remarks>
internal static class McpCommand
{
    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        switch (args.Length >= 2 ? args[1] : null)
        {
            case "serve":
                return AgentCommand.Execute(["agent", .. args[2..]], context);

            case "setup":
                return SetupCommand.Execute(["setup", .. args[2..]], context);

            case "-h" or "--help" or "help":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste mcp <serve|setup> [options]");
        writer.WriteLine("  serve   approve agents' requests in this terminal (same as keypaste agent)");
        writer.WriteLine("  setup   point the AI clients on this machine at your vault (same as keypaste setup)");
        writer.WriteLine("the MCP server itself is keypaste-mcp; your MCP client starts it.");
    }
}
