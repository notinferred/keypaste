using Keypaste.Core.Processes;

namespace Keypaste.Core.Clients;

/// <summary>Whether an AI tool found on this computer has keypaste in its own configuration.</summary>
public enum McpToolState
{
    /// <summary>Its configuration holds keypaste's entry.</summary>
    Connected,

    /// <summary>Its configuration holds no keypaste entry.</summary>
    NotConnected,

    /// <summary>keypaste cannot read whether its configuration holds keypaste; Connect still works, because it first removes any earlier entry.</summary>
    CannotTell,

    /// <summary>It is installed, but keypaste cannot start it or cannot tell where its file is, so it cannot be connected.</summary>
    Problem,
}

/// <summary>What the Agents screen says of a found tool.</summary>
/// <param name="State">Whether it is connected.</param>
/// <param name="Reason">Why keypaste cannot tell, or what stands in the way; null otherwise.</param>
public sealed record McpToolPresence(McpToolState State, string? Reason);

/// <summary>One AI tool of the catalogue found on this computer, and what its own configuration says.</summary>
/// <param name="Tool">The catalogue's tool.</param>
/// <param name="Detection">What <see cref="McpClientSetup.Detect"/> found.</param>
/// <param name="Reading">What <see cref="McpClientConfig.Read"/> found, or null for a tool with a problem.</param>
public sealed record McpToolFinding(McpClient Tool, McpClientDetection Detection, McpEntryReading? Reading)
{
    /// <summary>Gets whether the tool is connected, as a person reads it.</summary>
    public McpToolPresence Presence => (Detection.Problem, Reading?.Presence) switch
    {
        ({ } problem, _) => new McpToolPresence(McpToolState.Problem, problem),
        (_, McpEntryPresence.Present) => new McpToolPresence(McpToolState.Connected, Reason: null),
        (_, McpEntryPresence.Absent) => new McpToolPresence(McpToolState.NotConnected, Reason: null),
        _ => new McpToolPresence(McpToolState.CannotTell, Reading?.Reason),
    };
}

/// <summary>Looks for every AI tool the catalogue connects on a machine, as <c>keypaste setup</c> finds them, changing nothing.</summary>
/// <remarks>
/// Each tool is looked for on its own thread, because finding one may start its <c>--version</c> for up to
/// <see cref="McpClientSetup.ProbeTimeout"/>. Nothing is written or kept: a found tool's configuration is read
/// read-only by <see cref="McpClientConfig.Read"/>, which keeps only whether keypaste is in it.
/// </remarks>
public static class McpToolSurvey
{
    /// <summary>Finds the tools of <paramref name="machine"/>'s platform that are installed, and reads whether each holds keypaste.</summary>
    /// <param name="machine">Where to look.</param>
    /// <param name="runner">Starts each tool's <c>--version</c>.</param>
    /// <param name="cancellationToken">
    /// Ends the wait at once. A <c>--version</c> already started still ends within its probe timeout, and no
    /// configuration is read after the cancel.
    /// </param>
    /// <returns>One finding per installed tool, in catalogue order.</returns>
    /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
    public static async Task<IReadOnlyList<McpToolFinding>> LookAsync(McpClientMachine machine, IProcessRunner runner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(runner);

        cancellationToken.ThrowIfCancellationRequested();

        var looks = McpClientCatalog.For(machine.Platform)
            .Select(tool => Task.Factory.StartNew(
                () => Look(tool, machine, runner, McpClientConfig.Read, cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        var found = await Task.WhenAll(looks).WaitAsync(cancellationToken).ConfigureAwait(false);

        return [.. found.OfType<McpToolFinding>()];
    }

    /// <summary>Looks for one tool, reading its configuration through <paramref name="read"/> only when it was found, has no problem and the survey was not cancelled.</summary>
    internal static McpToolFinding? Look(
        McpClient tool,
        McpClientMachine machine,
        IProcessRunner runner,
        Func<McpClient, McpClientMachine, McpEntryReading> read,
        CancellationToken cancellationToken)
    {
        var detection = McpClientSetup.Detect(tool, machine, runner);

        if (!detection.Found || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return new McpToolFinding(tool, detection, detection.Problem is null ? read(tool, machine) : null);
    }
}
