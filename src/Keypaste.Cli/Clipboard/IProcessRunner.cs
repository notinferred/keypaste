using System.Diagnostics;
using System.Text;

namespace Keypaste.Cli.Clipboard;

/// <summary>The outcome of running an external tool.</summary>
/// <param name="ToolFound">Whether the executable existed at all.</param>
/// <param name="ExitCode">Its exit code, when it ran.</param>
/// <param name="StandardOutput">Its captured stdout.</param>
/// <param name="StandardError">Its captured stderr.</param>
internal readonly record struct ProcessResult(
    bool ToolFound,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    /// <summary>Whether the tool ran and reported success.</summary>
    internal bool Succeeded => ToolFound && ExitCode == 0;
}

/// <summary>
/// Runs an external tool, writing text to its standard input.
/// </summary>
/// <remarks>
/// A seam so the per-platform clipboard implementations — argument construction, stdin encoding,
/// the order of closing and waiting — can be unit-tested without spawning <c>clip.exe</c>.
/// </remarks>
internal interface IProcessRunner
{
    /// <summary>Runs <paramref name="fileName"/> and returns what it did.</summary>
    /// <param name="fileName">Executable path.</param>
    /// <param name="arguments">Arguments, passed without shell interpretation.</param>
    /// <param name="stdin">Text written to the tool's standard input, or null to write nothing.</param>
    /// <param name="stdinEncoding">Encoding for <paramref name="stdin"/>.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    ProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? stdin,
        Encoding stdinEncoding,
        TimeSpan timeout);
}

/// <summary>Runs tools as real child processes.</summary>
internal sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc/>
    public ProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? stdin,
        Encoding stdinEncoding,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(stdinEncoding);

        var info = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = stdinEncoding,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The executable does not exist. This is the only condition that lets a caller try
            // the next candidate tool; a tool that exists and fails is a hard error.
            return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
        }

        if (process is null)
        {
            return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
        }

        using (process)
        {
            // Drain both pipes concurrently and BEFORE writing stdin: either 64 KiB buffer
            // filling up would deadlock the child and us against each other.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
            }

            // Closing stdin is not optional: clip.exe, pbcopy, wl-copy and xclip all read to
            // EOF and will never exit without it.
            process.StandardInput.Close();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                TryKill(process);
                return new ProcessResult(ToolFound: true, ExitCode: -1, string.Empty, "timed out");
            }

            // Bounded joins. wl-copy and xclip fork a daemon that inherits these pipe handles,
            // so EOF may never arrive even though the process we launched has exited.
            var stdout = WaitFor(stdoutTask, timeout);
            var stderr = WaitFor(stderrTask, timeout);

            return new ProcessResult(ToolFound: true, process.ExitCode, stdout, stderr);
        }
    }

    private static string WaitFor(Task<string> task, TimeSpan timeout)
    {
        return task.Wait(timeout) ? task.Result : string.Empty;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout and here.
        }
    }
}
