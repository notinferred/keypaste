using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>A program these tests started, whose output lines are kept as they arrive, and whose whole tree ends with the test.</summary>
internal sealed class RunningProcess : IDisposable
{
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output = new();
    private readonly ConcurrentQueue<string> _error = new();

    private RunningProcess(Process process)
    {
        _process = process;
        process.OutputDataReceived += (_, line) => Keep(_output, line.Data);
        process.ErrorDataReceived += (_, line) => Keep(_error, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    internal int Id => _process.Id;

    internal IReadOnlyList<string> Output => [.. _output];

    internal IReadOnlyList<string> Error => [.. _error];

    internal static RunningProcess Start(string program, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        return new RunningProcess(Process.Start(start)!);
    }

    internal void Send(string line)
    {
        _process.StandardInput.WriteLine(line);
        _process.StandardInput.Flush();
    }

    internal void CloseInput() => _process.StandardInput.Close();

    /// <summary>Waits until standard output holds <paramref name="count"/> lines matching <paramref name="pattern"/>.</summary>
    internal string WaitForOutput(string pattern, int count = 1) => WaitFor(_output, pattern, count, "standard output");

    /// <summary>Waits until standard error holds a line matching <paramref name="pattern"/>.</summary>
    internal string WaitForError(string pattern) => WaitFor(_error, pattern, 1, "standard error");

    internal int WaitForExit()
    {
        Assert.True(_process.WaitForExit(Wait), $"{_process.StartInfo.FileName} did not exit:\n{Transcript()}");
        _process.WaitForExit();
        return _process.ExitCode;
    }

    internal void Kill()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }

    private static void Keep(ConcurrentQueue<string> lines, string? line)
    {
        if (line is not null)
        {
            lines.Enqueue(line.TrimEnd('\r'));
        }
    }

    private string WaitFor(ConcurrentQueue<string> lines, string pattern, int count, string stream)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < Wait)
        {
            var matching = lines.Where(line => Regex.IsMatch(line, pattern)).ToList();
            if (matching.Count >= count)
            {
                return matching[count - 1];
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"timed out waiting for {count} line(s) matching '{pattern}' on {stream}:\n{Transcript()}");
        return "";
    }

    private string Transcript() => $"--- stdout\n{string.Join('\n', _output)}\n--- stderr\n{string.Join('\n', _error)}";
}
