using System.Runtime.Versioning;
using Keypaste.Core.Launch;
using Keypaste.Core.Processes;

namespace Keypaste.App.Session;

/// <summary>What the Env Sets screen starts a project's terminal with.</summary>
/// <param name="Terminal">Which terminal, and how it is given the command.</param>
/// <param name="Launcher">What starts it on Windows and Linux.</param>
/// <param name="Parent">The environment the terminal would inherit before the set is added, on Windows and Linux; on macOS the one <c>open</c> is given, less every <c>KEYPASTE_</c> variable.</param>
/// <remarks>
/// On macOS the app hands Terminal no set: it leaves a request in <see cref="Requests"/>, asks
/// <see cref="Authority"/> for the run's launch pass and opens Terminal with <see cref="Opener"/> (D-0451).
/// </remarks>
internal sealed record ProjectLaunching(
    TerminalLaunch Terminal,
    IProcessLauncher Launcher,
    Func<IReadOnlyDictionary<string, string>> Parent)
{
    /// <summary>Where Terminal's launcher finds the run the person confirmed, or null where there is none.</summary>
    internal TerminalRequests? Requests { get; init; }

    /// <summary>What runs <c>open</c> to bring Terminal up, given the plan and how long it may take.</summary>
    internal Func<TerminalOpen, TimeSpan, ProcessResult> Opener { get; init; } = TerminalLaunch.Open;

    /// <summary>The authority whose log a Run's History line is written in: on macOS by answering the window's <c>keypaste run --session</c>, whose pass it issues, and on Windows and Linux before the terminal starts.</summary>
    internal AppAuthority? Authority { get; init; }

    /// <summary>The platform's terminal: on Windows and Linux started detached with this process's environment beneath the set, on macOS Terminal through this bundle's launcher.</summary>
    /// <param name="authority">The app's authority, or null when nothing records a Run, which then starts nothing.</param>
    /// <returns>The launching.</returns>
    internal static ProjectLaunching ForThisMachine(AppAuthority? authority) =>
        OperatingSystem.IsMacOS()
            ? ThroughTerminal(TerminalLaunch.ForThisMachine(), authority, TerminalLaunch.Open)
            : new(TerminalLaunch.ForThisMachine(), new DetachedProcessLauncher(), EnvironmentMerge.Inherited) { Authority = authority };

    /// <summary>Terminal through the launcher of a <c>keypaste.app</c> other than the one this runs from.</summary>
    /// <param name="authority">The app's authority.</param>
    /// <param name="bundle">The bundle's directory, ending in <c>.app</c>.</param>
    /// <param name="opener">What runs <c>open</c>; the system's when null.</param>
    /// <returns>The launching.</returns>
    [SupportedOSPlatform("macos")]
    internal static ProjectLaunching ForMacBundle(AppAuthority? authority, string bundle, Func<TerminalOpen, TimeSpan, ProcessResult>? opener = null) =>
        ThroughTerminal(TerminalLaunch.ForMacApp(Path.Combine(bundle, "Contents", "MacOS")), authority, opener ?? TerminalLaunch.Open);

    [SupportedOSPlatform("macos")]
    private static ProjectLaunching ThroughTerminal(TerminalLaunch terminal, AppAuthority? authority, Func<TerminalOpen, TimeSpan, ProcessResult> opener) =>
        new(terminal, new DetachedProcessLauncher(), EnvironmentMerge.Inherited)
        {
            Requests = TerminalRequests.ForThisUser(),
            Opener = opener,
            Authority = authority,
        };
}
