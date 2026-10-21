namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste mcp serve</c>, <c>setup</c> and <c>policy</c>: removed spellings, each refused with the verb that replaced it (D-0470).</summary>
/// <remarks>
/// Every other <c>keypaste mcp</c>, its help included, is the MCP bridge, which <see cref="Program.StartsBridge"/>
/// sends there before this class is reached.
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
                return CliApp.Renamed(context, "keypaste mcp serve", "keypaste approve");

            case "setup":
                return CliApp.Renamed(context, "keypaste mcp setup", "keypaste setup");

            case "policy":
                return CliApp.Renamed(context, "keypaste mcp policy", "keypaste clients");

            default:
                context.Stderr.WriteLine("keypaste mcp: your AI tools start it; keypaste setup connects them");
                return CliApp.ExitUsageError;
        }
    }
}
