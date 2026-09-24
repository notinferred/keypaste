namespace Keypaste.Core.Launch;

/// <summary>What to start with a project's set, before its environment is built.</summary>
/// <param name="FileName">The program.</param>
/// <param name="Arguments">Its arguments. Never a value from the set.</param>
/// <param name="WorkingDirectory">Where it starts, or null for the launcher's own directory.</param>
/// <param name="CommandLine">A verbatim Windows command line for <c>cmd.exe</c>; see <see cref="ChildStart.CommandLine"/>.</param>
public sealed record LaunchTarget(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    string? CommandLine = null);

/// <summary>
/// Starts a program with a project's env set in its environment: the one launch <c>keypaste run</c>
/// and the app's Run and Open terminal share.
/// </summary>
/// <remarks>
/// Only a set <see cref="EnvResolution"/> released whole gets here, and its values go into the
/// child's environment and nowhere else: not its arguments, not a file (PRODUCT §3.4).
/// </remarks>
public static class EnvLaunch
{
    /// <summary>Starts <paramref name="target"/> with <paramref name="resolved"/> over <paramref name="parent"/>.</summary>
    /// <param name="resolved">A set whose outcome is <see cref="EnvOutcome.Resolved"/>.</param>
    /// <param name="target">What to start.</param>
    /// <param name="parent">The environment the child would otherwise inherit.</param>
    /// <param name="launcher">What starts it.</param>
    /// <returns>How starting it went.</returns>
    /// <exception cref="ArgumentException"><paramref name="resolved"/> was not released.</exception>
    public static ChildResult Start(
        EnvResolved resolved,
        LaunchTarget target,
        IReadOnlyDictionary<string, string> parent,
        IProcessLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(launcher);

        if (resolved.Outcome != EnvOutcome.Resolved)
        {
            throw new ArgumentException($"'{resolved.Project}' was not released: {resolved.Refusal}", nameof(resolved));
        }

        return launcher.Run(new ChildStart(
            target.FileName,
            target.Arguments,
            EnvironmentMerge.Build(parent, resolved.Variables),
            target.WorkingDirectory,
            target.CommandLine));
    }
}
