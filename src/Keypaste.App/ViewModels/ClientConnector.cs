using Keypaste.Core.Clients;
using Keypaste.Core.Processes;

namespace Keypaste.App.ViewModels;

/// <summary>What connecting a client reaches outside the app: the client's own command, the bridge and a check.</summary>
/// <param name="Runner">Runs each client's own command.</param>
/// <param name="FindServer">Finds the <c>keypaste-mcp</c> a client should start, or null.</param>
/// <param name="Places">Where <paramref name="FindServer"/> looks, for a message that says so.</param>
/// <param name="StartCheck">Starts the bridge a registration describes, or null when it cannot be started.</param>
internal sealed record ClientConnector(
    IProcessRunner Runner,
    Func<McpServerCommand?> FindServer,
    string Places,
    Func<McpServerRegistration, McpConnectionCheck?> StartCheck)
{
    /// <summary>The real programs, found from where this app is running.</summary>
    internal static ClientConnector ForThisProcess()
    {
        var directory = AppContext.BaseDirectory;

        return new ClientConnector(
            new SystemProcessRunner(),
            () => McpServerLocator.FindForDesktop(
                directory,
                Environment.GetEnvironmentVariable("APPIMAGE"),
                Environment.GetEnvironmentVariable("APPDIR"),
                Environment.GetEnvironmentVariable("PATH")),
            McpServerLocator.Places(directory),
            McpConnectionCheck.Start);
    }
}
