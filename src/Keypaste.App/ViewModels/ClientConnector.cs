using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Processes;

namespace Keypaste.App.ViewModels;

/// <summary>What connecting a client reaches outside the app: the machine, the client's own command, the bridge and a check.</summary>
/// <param name="Runner">Runs each client's own command.</param>
/// <param name="Machine">Where the clients and their files are.</param>
/// <param name="ApproverOverride">The app's <c>KEYPASTE_APPROVER</c>, or null.</param>
/// <param name="FindServer">Finds the bridge a client should start, or says why there is none.</param>
/// <param name="StartCheck">Starts the bridge a registration describes, or null when it cannot be started.</param>
internal sealed record ClientConnector(
    IProcessRunner Runner,
    McpClientMachine Machine,
    string? ApproverOverride,
    Func<McpBridgeTargetResult> FindServer,
    Func<McpServerRegistration, McpConnectionCheck?> StartCheck)
{
    /// <summary>The real programs, found from where this app is running.</summary>
    internal static ClientConnector ForThisProcess()
    {
        var directory = AppContext.BaseDirectory;
        var appImage = McpAppImage.ForThisProcess();

        return new ClientConnector(
            new SystemProcessRunner(),
            McpClientMachine.ForTheApp(),
            Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
            () => McpBridgeTargetResult.ForDesktop(directory, appImage, Environment.GetEnvironmentVariable("PATH")),
            McpConnectionCheck.Start);
    }
}

/// <summary>The bridge a client is told to start, or why there is none.</summary>
/// <param name="Target">The bridge, or null.</param>
/// <param name="Error">Why there is none, or null.</param>
internal sealed record McpBridgeTargetResult(McpBridgeTarget? Target, string? Error)
{
    /// <summary>The bridge the desktop registers, as <see cref="McpServerLocator.TryForDesktop"/> finds it.</summary>
    /// <param name="appDirectory">The desktop's own directory.</param>
    /// <param name="appImage">The AppImage the desktop runs from, or null.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    internal static McpBridgeTargetResult ForDesktop(string appDirectory, McpAppImage? appImage, string? pathVariable) =>
        McpServerLocator.TryForDesktop(appDirectory, appImage, pathVariable, out var target, out var error)
            ? new McpBridgeTargetResult(target, Error: null)
            : new McpBridgeTargetResult(Target: null, error);
}
