using System.ComponentModel;
using System.Diagnostics;

namespace Keypaste.Core.Launch;

/// <summary>Starts a child that outlives the call, such as the terminal the app opens, and does not wait for it.</summary>
/// <remarks>
/// Nothing is redirected, so nothing the child prints passes through the launching process. A
/// console program started by the desktop, which has no console of its own, gets a window of its
/// own on Windows.
/// </remarks>
public sealed class DetachedProcessLauncher : IProcessLauncher
{
    /// <inheritdoc/>
    public ChildResult Run(ChildStart start)
    {
        var info = new ProcessStartInfo
        {
            FileName = start.FileName,
            UseShellExecute = false,
            WorkingDirectory = start.WorkingDirectory ?? string.Empty,
        };

        if (start.CommandLine is not null)
        {
            info.Arguments = start.CommandLine;
        }
        else
        {
            foreach (var argument in start.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        // Cleared first so nothing of the launching process's own reaches the child except what
        // the merge decided it should.
        info.Environment.Clear();
        foreach (var (name, value) in start.Environment)
        {
            info.Environment[name] = value;
        }

        try
        {
            using var process = Process.Start(info);

            return process is null
                ? new ChildResult(ChildOutcome.Failed, 0, $"could not start '{start.FileName}'")
                : new ChildResult(ChildOutcome.Started, 0, string.Empty, process.Id);
        }
        catch (Win32Exception ex)
        {
            return ex.NativeErrorCode switch
            {
                2 => new ChildResult(ChildOutcome.NotFound, 0, $"no such command '{start.FileName}'"),
                5 or 13 => new ChildResult(ChildOutcome.NotExecutable, 0, $"'{start.FileName}' is not executable"),
                _ => new ChildResult(ChildOutcome.Failed, 0, ex.Message),
            };
        }
    }
}
