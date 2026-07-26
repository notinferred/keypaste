namespace Keypaste.Cli.Execution;

/// <summary>What keypaste asks the operating system to start.</summary>
/// <param name="FileName">The command as the user typed it. Resolved by the OS, not by keypaste.</param>
/// <param name="Arguments">Its arguments, passed without any shell interpretation.</param>
/// <param name="Environment">
/// The child's <b>complete</b> environment, not a delta. The merge has already happened and has
/// already been checked.
/// </param>
internal readonly record struct ChildStart(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>How an attempt to run a child ended.</summary>
internal enum ChildOutcome
{
    /// <summary>The child ran and exited. <see cref="ChildResult.ExitCode"/> is its own.</summary>
    Exited = 0,

    /// <summary>There is no such command.</summary>
    NotFound = 1,

    /// <summary>The command exists but could not be executed.</summary>
    NotExecutable = 2,

    /// <summary>It could not be started, for some other reason.</summary>
    Failed = 3,
}

/// <summary>The outcome of running a child.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="ExitCode">The child's exit code, meaningful only for <see cref="ChildOutcome.Exited"/>.</param>
/// <param name="Error">What went wrong, or an empty string.</param>
internal readonly record struct ChildResult(ChildOutcome Outcome, int ExitCode, string Error);

/// <summary>
/// Starts a child with an exact environment, hands it keypaste's own stdin, stdout and stderr,
/// waits for it, and reports how it ended.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists because streaming stdio transparently means the child inherits real handles,
/// which puts everything it prints beyond <see cref="CliContext.Stdout"/> and beyond any
/// in-process test. What a fake <em>can</em> assert is everything that decides what the child
/// sees — the resolved file name, the exact argument list, the exact environment, and the exit
/// code coming back. Inherited handles and real signals are covered by
/// <c>scripts/verify-run-injection.sh</c> and <c>scripts/verify-run-signals.sh</c> instead.
/// </para>
/// <para>
/// The whole start-supervise-wait operation is behind one call so no <see cref="IDisposable"/>
/// ever crosses the boundary — the same shape as <see cref="VaultSession.Open"/>, and the same
/// reason: it satisfies CA2000 by construction rather than by suppression.
/// </para>
/// </remarks>
internal interface IProcessLauncher
{
    /// <summary>Runs <paramref name="start"/> to completion.</summary>
    ChildResult Run(ChildStart start);
}
