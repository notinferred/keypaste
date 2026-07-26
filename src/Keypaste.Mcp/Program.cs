using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp;

/// <summary>
/// The MCP bridge: two tools, over stdio, denying everything.
/// </summary>
/// <remarks>
/// <para>
/// Started by an MCP client rather than by a person — see <c>docs/mcp-setup.md</c>. Its stdin and
/// stdout are the JSON-RPC stream, which is why nothing here may ever write to
/// <see cref="Console.Out"/>: one stray line corrupts the protocol and the failure looks like the
/// client is broken. Diagnostics go to <see cref="Console.Error"/>, which Claude Desktop captures
/// in <c>mcp-server-keypaste.log</c>.
/// </para>
/// <para>
/// No <c>ILoggerFactory</c> is supplied, deliberately: the SDK's usual sample wires a console logger,
/// whose default target is standard output. Passing null removes the possibility rather than
/// configuring around it.
/// </para>
/// </remarks>
internal static class Program
{
    internal const int ExitSuccess = 0;
    internal const int ExitRefusedToStart = 1;

    private static async Task<int> Main(string[] args)
    {
        if (!ServerOptions.TryParse(
                args,
                Environment.GetEnvironmentVariable(VaultLocation.EnvironmentVariable),
                Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable),
                out var options,
                out var error))
        {
            await Console.Error.WriteLineAsync($"keypaste-mcp: {error}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync().ConfigureAwait(false);
            await Console.Error.WriteLineAsync(ServerOptions.Usage).ConfigureAwait(false);
            return ExitRefusedToStart;
        }

        if (options.WantsHelp)
        {
            await Console.Error.WriteLineAsync(ServerOptions.Usage).ConfigureAwait(false);
            return ExitSuccess;
        }

        // The audit log is a precondition, so an unwritable one stops the server rather than
        // surfacing later as a mysterious per-call refusal (CORE.md laws 3.3 and 3.7).
        if (!AuditLog.TryOpen(options.AuditPath, TimeProvider.System, out var audit, out var auditError))
        {
            await Console.Error.WriteLineAsync($"keypaste-mcp: {auditError}").ConfigureAwait(false);
            return ExitRefusedToStart;
        }

        using (audit)
        {
            if (audit.TightenedPermissions)
            {
                await Console.Error
                    .WriteLineAsync($"keypaste-mcp: tightened permissions on {audit.Path} to owner-only.")
                    .ConfigureAwait(false);
            }

            await ServeAsync(options, audit).ConfigureAwait(false);
        }

        return ExitSuccess;
    }

    private static async Task ServeAsync(ServerOptions options, AuditLog audit)
    {
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "keypaste", Version = CoreInfo.Version },
            ServerInstructions = ToolText.ServerInstructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = [],
        };

        // Registered explicitly rather than by scanning the assembly: two tools is the whole
        // surface, and a bridge that could grow a third by accident is not one to build.
        serverOptions.ToolCollection.Add(
            new ListEntryNamesTool(new LockedEntryNameSource(), options, audit));
        serverOptions.ToolCollection.Add(new RequestCredentialTool(options, audit));

        await using var transport = new StdioServerTransport(serverOptions, loggerFactory: null);
        await using var server = McpServer.Create(transport, serverOptions, loggerFactory: null, serviceProvider: null);

        await server.RunAsync().ConfigureAwait(false);
    }
}
