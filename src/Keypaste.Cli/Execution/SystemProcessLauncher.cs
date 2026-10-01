using System.ComponentModel;
using System.Diagnostics;
using Keypaste.Core.Launch;

namespace Keypaste.Cli.Execution;

/// <summary>Runs children as real child processes, sharing keypaste's console.</summary>
internal sealed class SystemProcessLauncher : IProcessLauncher
{
    /// <inheritdoc/>
    public ChildResult Run(ChildStart start)
    {
        // Set before the start, so that no signal arriving as the child starts finds keypaste untrapped (D-0394).
        using var relay = new SignalRelay(Console.IsInputRedirected);
        return Run(start, relay, Process.Start);
    }

    /// <summary>Starts <paramref name="start"/> under <paramref name="relay"/>'s traps and waits for it.</summary>
    /// <param name="start">What to start.</param>
    /// <param name="relay">The traps, already set.</param>
    /// <param name="launch">What starts the process: <see cref="Process.Start(ProcessStartInfo)"/> outside tests.</param>
    /// <returns>How it ended.</returns>
    internal static ChildResult Run(ChildStart start, SignalRelay relay, Func<ProcessStartInfo, Process?> launch)
    {
        // No shell and nothing redirected: the child gets the argument list as parsed and keypaste's own stdin, stdout and stderr.
        var info = new ProcessStartInfo { FileName = start.FileName, UseShellExecute = false };

        foreach (var argument in start.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Cleared first so that nothing of keypaste's own environment reaches the child beyond the merge.
        info.Environment.Clear();
        foreach (var (name, value) in start.Environment)
        {
            info.Environment[name] = value;
        }

        // A signal held before the start ends the run without starting anything, as an untrapped one would end keypaste.
        if (Interrupted(relay) is { } stopped)
        {
            return stopped;
        }

        Process? process;
        try
        {
            process = launch(info);
        }
        catch (Win32Exception ex)
        {
            // The shell's 127 and 126, which scripts already branch on.
            return Interrupted(relay) ?? ex.NativeErrorCode switch
            {
                2 => new ChildResult(ChildOutcome.NotFound, 0, $"no such command '{start.FileName}'"),
                5 or 13 => new ChildResult(ChildOutcome.NotExecutable, 0, $"'{start.FileName}' is not executable"),
                _ => new ChildResult(ChildOutcome.Failed, 0, ex.Message),
            };
        }

        if (process is null)
        {
            return Interrupted(relay) ?? new ChildResult(ChildOutcome.Failed, 0, $"could not start '{start.FileName}'");
        }

        using (process)
        {
            relay.Attach(process);
            process.WaitForExit();

            // Unix already reports 128 + the signal number for a signalled child, as a shell would.
            return new ChildResult(ChildOutcome.Exited, process.ExitCode, string.Empty);
        }
    }

    private static ChildResult? Interrupted(SignalRelay relay) =>
        relay.Held is { } held && NativeSignals.Number(held) is { } number
            ? new ChildResult(ChildOutcome.Interrupted, 128 + number, $"received {held}")
            : null;
}
