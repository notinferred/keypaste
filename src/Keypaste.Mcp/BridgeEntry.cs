using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Settings;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp;

/// <summary>
/// The MCP bridge, which <c>keypaste mcp</c> enters through <see cref="RunAsync"/> before any vault
/// access (D-0418): three tools, or four with <c>--allow-run</c>, over stdio, denying everything.
/// </summary>
/// <remarks>
/// <para>
/// Started by an MCP client rather than by a person — see <c>docs/mcp-setup.md</c>. While it serves,
/// its stdin and stdout are the JSON-RPC stream, which is why nothing but the help it prints instead
/// of serving may write to <see cref="Console.Out"/>: one stray line corrupts the protocol and the
/// failure looks like the client is broken. Diagnostics go to <see cref="Console.Error"/>, which
/// Claude Desktop captures in <c>mcp-server-keypaste.log</c>.
/// </para>
/// <para>
/// No <c>ILoggerFactory</c> is supplied, deliberately: the SDK's usual sample wires a console logger,
/// whose default target is standard output. Passing null removes the possibility rather than
/// configuring around it.
/// </para>
/// </remarks>
public static class BridgeEntry
{
    internal const int ExitSuccess = 0;
    internal const int ExitRefusedToStart = 1;

    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

        if (!ServerOptions.TryParse(
                args,
                Environment.GetEnvironmentVariable(VaultLocation.EnvironmentVariable),
                home,
                Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
                ChosenVault.Read(home),
                out var options,
                out var error))
        {
            await Console.Error.WriteLineAsync($"keypaste mcp: {error}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("see keypaste mcp --help").ConfigureAwait(false);
            return ExitRefusedToStart;
        }

        if (options.WantsHelp)
        {
            await Console.Out.WriteLineAsync(ServerOptions.Usage).ConfigureAwait(false);
            return ExitSuccess;
        }

        // The audit log is a precondition, so an unwritable one stops the server rather than
        // surfacing later as a mysterious per-call refusal (docs/PRODUCT.md laws 3.3 and 3.7).
        if (!AuditLog.TryOpen(options.AuditPath, TimeProvider.System, out var audit, out var auditError))
        {
            await Console.Error.WriteLineAsync($"keypaste mcp: {auditError}").ConfigureAwait(false);
            return ExitRefusedToStart;
        }

        using (audit)
        {
            if (audit.TightenedPermissions)
            {
                await Console.Error
                    .WriteLineAsync($"keypaste mcp: tightened permissions on {audit.Path} to owner-only.")
                    .ConfigureAwait(false);
            }

            await ServeAsync(options, audit).ConfigureAwait(false);
        }

        return ExitSuccess;
    }

    private static async Task ServeAsync(ServerOptions options, AuditLog audit)
    {
        using var inputEnded = new CancellationTokenSource();

        // Nothing connects here. The vault's owner is reached on the first call that needs an answer,
        // because a client spawns this server long before anybody unlocks the vault, and a bridge that
        // refused to start would look broken in the client's log rather than saying so in an
        // answer an agent can act on.
        await using var approver = new ApproverConnection(options.ApproverName, options.VaultPath) { InputEnded = inputEnded.Token };

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "keypaste", Version = CoreInfo.Version },
            ServerInstructions = options.AllowRun ? ToolText.ServerInstructions + ToolText.RunInstructions : ToolText.ServerInstructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = [],
        };

        // Registered explicitly rather than by scanning the assembly: three tools is the whole
        // surface, and run a fourth only when the person started this bridge with --allow-run, so
        // no existing configuration gains command execution on upgrade (D-0358).
        serverOptions.ToolCollection.Add(
            new ListEntryNamesTool(new ApproverEntryNameSource(approver, options), options, audit));
        serverOptions.ToolCollection.Add(new RequestCredentialTool(options, approver, audit));
        serverOptions.ToolCollection.Add(new RequestAccessChangeTool(options, approver, audit, TimeProvider.System));

        if (options.AllowRun)
        {
            // Every child of this process is a run's, so orphans a run leaves behind can be found.
            CapturedLaunch.AdoptOrphans();
            serverOptions.ToolCollection.Add(new RunTool(options, approver, audit));
        }

        await using var transport = new StdioServerTransport(serverOptions, loggerFactory: null);
        await using var server = McpServer.Create(transport, serverOptions, loggerFactory: null, serviceProvider: null);
        approver.Identity = () => McpAudit.AttachIdentity(server.ClientInfo, options);
        approver.Exposure = options.Exposure.Globs;

        // The server waits for the calls in flight once the client closes standard input, so a call
        // still waiting for the owner is withdrawn then, as one the client gives up on is.
        _ = EndWithInputAsync(transport.MessageReader.Completion, inputEnded);

        await server.RunAsync().ConfigureAwait(false);
    }

    /// <summary>Cancels <paramref name="inputEnded"/> once the transport has handed over the last message the client sent.</summary>
    /// <param name="read">Completes when the client's input has ended and every message in it was read.</param>
    /// <param name="inputEnded">What withdraws the exchanges still in flight.</param>
    private static async Task EndWithInputAsync(Task read, CancellationTokenSource inputEnded)
    {
        try
        {
            await read.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Input that ended on an error has ended all the same.
        }

        try
        {
            await inputEnded.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The bridge had already finished, so nothing was left in flight to withdraw.
        }
    }
}
