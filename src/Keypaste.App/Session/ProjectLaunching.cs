using Keypaste.Core.Launch;

namespace Keypaste.App.Session;

/// <summary>What the Env Sets screen starts a project's terminal with.</summary>
/// <param name="Terminal">Which terminal, and how it is given the command.</param>
/// <param name="Launcher">What starts it.</param>
/// <param name="Parent">The environment the terminal would inherit before the set is added.</param>
internal sealed record ProjectLaunching(
    TerminalLaunch Terminal,
    IProcessLauncher Launcher,
    Func<IReadOnlyDictionary<string, string>> Parent)
{
    /// <summary>The platform's terminal, started detached, with this process's environment beneath the set.</summary>
    internal static ProjectLaunching ForThisMachine() =>
        new(TerminalLaunch.ForThisMachine(), new DetachedProcessLauncher(), EnvironmentMerge.Inherited);
}
