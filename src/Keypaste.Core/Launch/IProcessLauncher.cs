namespace Keypaste.Core.Launch;

/// <summary>What is asked of the operating system to start.</summary>
/// <param name="FileName">The command. Resolved by the OS, not by keypaste.</param>
/// <param name="Arguments">Its arguments, passed without any shell interpretation.</param>
/// <param name="Environment">
/// The child's <b>complete</b> environment, not a delta. The merge has already happened and has
/// already been checked.
/// </param>
/// <param name="WorkingDirectory">Where the child starts, or null for the launcher's own directory.</param>
/// <param name="CommandLine">
/// The arguments as one Windows command line, passed verbatim in place of
/// <paramref name="Arguments"/>. Only <c>cmd.exe</c> needs it: it parses its own command line
/// rather than by the C runtime's rules, so no quoting of an argument list reaches it intact.
/// </param>
public readonly record struct ChildStart(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string? WorkingDirectory = null,
    string? CommandLine = null);

/// <summary>How an attempt to run a child ended.</summary>
public enum ChildOutcome
{
    /// <summary>The child ran and exited. <see cref="ChildResult.ExitCode"/> is its own.</summary>
    Exited = 0,

    /// <summary>There is no such command.</summary>
    NotFound = 1,

    /// <summary>The command exists but could not be executed.</summary>
    NotExecutable = 2,

    /// <summary>It could not be started, for some other reason.</summary>
    Failed = 3,

    /// <summary>The child started and is not waited for. <see cref="ChildResult.ProcessId"/> is its own.</summary>
    Started = 4,
}

/// <summary>The outcome of running a child.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="ExitCode">The child's exit code, meaningful only for <see cref="ChildOutcome.Exited"/>.</param>
/// <param name="Error">What went wrong, or an empty string.</param>
/// <param name="ProcessId">The child's process id, meaningful only for <see cref="ChildOutcome.Started"/>.</param>
public readonly record struct ChildResult(ChildOutcome Outcome, int ExitCode, string Error, int ProcessId = 0);

/// <summary>Starts a child with an exact environment and reports how that went.</summary>
/// <remarks>
/// <para>
/// The seam exists because what a child prints and receives is beyond any in-process test. What a
/// fake <em>can</em> assert is everything that decides what the child sees — the file name, the
/// exact argument list, the directory and the exact environment.
/// </para>
/// <para>
/// The whole operation is behind one call so no <see cref="IDisposable"/> ever crosses the
/// boundary, which satisfies CA2000 by construction rather than by suppression.
/// </para>
/// </remarks>
public interface IProcessLauncher
{
    /// <summary>Starts <paramref name="start"/>: the CLI's launcher waits for it to exit, the app's returns once it runs.</summary>
    /// <param name="start">What to start.</param>
    /// <returns>How it ended, or that it started.</returns>
    ChildResult Run(ChildStart start);
}
