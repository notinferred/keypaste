using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Keypaste.Cli.Execution;

/// <summary>Runs children as real child processes, sharing keypaste's console.</summary>
internal sealed class SystemProcessLauncher : IProcessLauncher
{
    /// <inheritdoc/>
    public ChildResult Run(ChildStart start)
    {
        var info = new ProcessStartInfo
        {
            FileName = start.FileName,

            // No shell, ever. The arguments were parsed by keypaste and are handed over as a
            // list, so there is no quoting rule anywhere in this codebase to get wrong.
            UseShellExecute = false,

            // Nothing is redirected, and that IS the feature: the child gets keypaste's own
            // stdin, stdout and stderr. It is a terminal when keypaste's was, so colours work,
            // progress bars work, prompts work, and no keypaste thread sits between the child and
            // the screen adding latency or reordering output.
        };

        foreach (var argument in start.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Set explicitly rather than inherited implicitly. The merge is keypaste's decision, it
        // has already been made and tested, and clearing first means no variable of keypaste's own
        // survives into the child by accident.
        info.Environment.Clear();
        foreach (var (name, value) in start.Environment)
        {
            info.Environment[name] = value;
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception ex)
        {
            // Shells report 127 and 126 for these, and scripts already branch on them, so keypaste
            // says the same thing rather than inventing a third convention.
            return ex.NativeErrorCode switch
            {
                2 => new ChildResult(ChildOutcome.NotFound, 0, $"no such command '{start.FileName}'"),
                5 or 13 => new ChildResult(ChildOutcome.NotExecutable, 0, $"'{start.FileName}' is not executable"),
                _ => new ChildResult(ChildOutcome.Failed, 0, ex.Message),
            };
        }

        if (process is null)
        {
            return new ChildResult(ChildOutcome.Failed, 0, $"could not start '{start.FileName}'");
        }

        using (process)
        {
            // Sampled here, once, because asking inside a signal handler would be a syscall on a
            // path that must not block, and the answer cannot change while the child runs.
            var stdinRedirected = Console.IsInputRedirected;

            // Declared with the wait, in one method, so CA2000 is satisfied by construction. A
            // null registration — a signal this platform never raises — disposes as a no-op.
            using var sigint = Trap(PosixSignal.SIGINT, process, stdinRedirected);
            using var sigterm = Trap(PosixSignal.SIGTERM, process, stdinRedirected);
            using var sigquit = Trap(PosixSignal.SIGQUIT, process, stdinRedirected);
            using var sighup = Trap(PosixSignal.SIGHUP, process, stdinRedirected);

            process.WaitForExit();

            // Unix already reports 128 + the signal number for a child that was signalled, which
            // is what a shell would have reported, so there is nothing to translate.
            return new ChildResult(ChildOutcome.Exited, process.ExitCode, string.Empty);
        }
    }

    /// <summary>Handles one signal for as long as the child is running.</summary>
    private static PosixSignalRegistration? Trap(PosixSignal signal, Process child, bool stdinRedirected)
    {
        try
        {
            return PosixSignalRegistration.Create(signal, context =>
            {
                // Suppresses keypaste's own default termination. keypaste has exactly one job left
                // at this point — reap the child and report its status — and it cannot do that if
                // the runtime tears it down first. On Windows this does not survive the console
                // window being closed, which SECURITY.md states rather than papers over.
                context.Cancel = true;

                if (SignalPolicy.ShouldForward(signal, stdinRedirected))
                {
                    NativeSignals.TryRaise(child, signal);
                }
            });
        }
        catch (PlatformNotSupportedException)
        {
            // A signal this platform never raises. Skipping it is right; refusing to run is not.
            return null;
        }
    }
}
