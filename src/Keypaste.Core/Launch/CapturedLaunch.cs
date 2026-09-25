using System.ComponentModel;
using System.Diagnostics;

namespace Keypaste.Core.Launch;

/// <summary>How much of a run's output is kept, and how long its pipes are waited for after it exits.</summary>
/// <param name="KeptBytes">The most bytes kept of each stream while it runs: the last ones.</param>
/// <param name="DrainGrace">How long to wait for the pipes once the child has exited.</param>
public sealed record CaptureLimits(int KeptBytes, TimeSpan DrainGrace)
{
    /// <summary>256 KiB of each stream, and two seconds.</summary>
    public static CaptureLimits Default { get; } = new(256 * 1024, TimeSpan.FromSeconds(2));
}

/// <summary>What one stream of a run printed: its last bytes, never decoded or scrubbed here.</summary>
/// <param name="Bytes">The kept bytes, which may hold injected values until <see cref="OutputScrubber"/> removes them.</param>
/// <param name="TotalBytes">How many bytes the stream carried in all.</param>
/// <param name="HeadCut">Whether bytes before <paramref name="Bytes"/> were dropped.</param>
public sealed record CapturedOutput(byte[] Bytes, long TotalBytes, bool HeadCut)
{
    /// <summary>A stream that carried nothing.</summary>
    public static CapturedOutput Empty { get; } = new([], 0, false);

    /// <summary>A description with the bytes left out.</summary>
    /// <returns>The counts, never the output.</returns>
    public override string ToString() => $"CapturedOutput {{ Kept = {Bytes.Length}, Total = {TotalBytes}, HeadCut = {HeadCut} }}";
}

/// <summary>How a run ended.</summary>
/// <param name="Outcome">Whether it started and exited, or why it did not start.</param>
/// <param name="ExitCode">Its exit code, or null when it did not exit on its own.</param>
/// <param name="TimedOut">Whether it was stopped at its timeout.</param>
/// <param name="Stdout">What it printed to standard output.</param>
/// <param name="Stderr">What it printed to standard error.</param>
/// <param name="Error">Why it did not start, or empty.</param>
public sealed record CapturedResult(
    ChildOutcome Outcome,
    int? ExitCode,
    bool TimedOut,
    CapturedOutput Stdout,
    CapturedOutput Stderr,
    string Error);

/// <summary>
/// Starts an agent's command with an exact environment, captures what it prints, and stops it and
/// everything it started (D-0358).
/// </summary>
/// <remarks>
/// <para>
/// <b>No shell and no input.</b> The file is started with its argument list as given, and standard
/// input is redirected and closed at once: the bridge's own input is the JSON-RPC stream and must never
/// reach the child. Output is captured, never inherited, because one stray byte on the bridge's
/// standard output corrupts the protocol. No console window opens on Windows.
/// </para>
/// <para>
/// <b>It always ends.</b> The timeout counts from the start; at it, or when the caller gives up, the
/// child and everything <see cref="ChildContainment"/> can find are killed. After the child exits the
/// pipes are waited for <see cref="CaptureLimits.DrainGrace"/>, then the rest is killed and reading
/// stops: a read still pending is abandoned rather than disposed, which on Windows could block.
/// </para>
/// </remarks>
public static class CapturedLaunch
{
    private const int _chunk = 16 * 1024;
    private static readonly TimeSpan _afterKill = TimeSpan.FromSeconds(1);

    /// <summary>Marks this process as the reaper of its runs' orphans. The bridge calls it once, at start.</summary>
    /// <remarks>Only for a process whose every child is a run's (see <see cref="ChildContainment.AdoptOrphans"/>).</remarks>
    public static void AdoptOrphans() => ChildContainment.AdoptOrphans();

    /// <summary>Runs a child to its end.</summary>
    /// <param name="start">The program, its arguments, its complete environment and its directory.</param>
    /// <param name="limits">What is kept of its output.</param>
    /// <param name="timeout">How long it may run.</param>
    /// <param name="cancellationToken">The caller giving up, which kills it and then throws.</param>
    /// <returns>How it ended and what it printed.</returns>
    /// <exception cref="OperationCanceledException">The caller gave up; the child has been killed.</exception>
    public static async Task<CapturedResult> RunAsync(
        ChildStart start,
        CaptureLimits limits,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(start.FileName);
        ArgumentNullException.ThrowIfNull(start.Arguments);
        ArgumentNullException.ThrowIfNull(start.Environment);

        cancellationToken.ThrowIfCancellationRequested();

        if (Path.IsPathFullyQualified(start.FileName) && !File.Exists(start.FileName))
        {
            return NotStarted(ChildOutcome.NotFound, $"no such command '{start.FileName}'");
        }

        var leader = ChildContainment.SessionLeader;
        var info = new ProcessStartInfo
        {
            FileName = leader ?? start.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = start.WorkingDirectory ?? string.Empty,
        };

        if (leader is not null)
        {
            info.ArgumentList.Add(start.FileName);
        }

        foreach (var argument in start.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

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
            return ex.NativeErrorCode switch
            {
                2 => NotStarted(ChildOutcome.NotFound, $"no such command '{start.FileName}'"),
                5 or 13 => NotStarted(ChildOutcome.NotExecutable, $"'{start.FileName}' is not executable"),
                _ => NotStarted(ChildOutcome.Failed, ex.Message),
            };
        }

        if (process is null)
        {
            return NotStarted(ChildOutcome.Failed, $"could not start '{start.FileName}'");
        }

        var containment = ChildContainment.Contain(process, leader is not null);
        var stdout = new Kept(limits.KeptBytes);
        var stderr = new Kept(limits.KeptBytes);
        var reading = Task.WhenAll(
            ReadAsync(process.StandardOutput.BaseStream, stdout),
            ReadAsync(process.StandardError.BaseStream, stderr));
        var released = false;

        try
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child exited before its input was closed: there is nothing left to withhold.
            }

            var exited = process.WaitForExitAsync(CancellationToken.None);
            var finished = await Task.WhenAny(exited, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested && !exited.IsCompleted)
            {
                containment.KillAll(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var timedOut = finished != exited;

            if (timedOut)
            {
                containment.KillAll(process);
            }

            await Task.WhenAny(exited, Task.Delay(_afterKill, CancellationToken.None)).ConfigureAwait(false);
            await Task.WhenAny(reading, Task.Delay(limits.DrainGrace, CancellationToken.None)).ConfigureAwait(false);

            // Whatever is left, a daemon holding the pipes or a descendant that closed them, goes now.
            containment.KillAll(process);
            await Task.WhenAny(reading, Task.Delay(_afterKill, CancellationToken.None)).ConfigureAwait(false);

            released = reading.IsCompleted;

            return new CapturedResult(
                ChildOutcome.Exited,
                timedOut || !process.HasExited ? null : process.ExitCode,
                timedOut,
                stdout.Snapshot(),
                stderr.Snapshot(),
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            containment.KillAll(process);
            throw;
        }
        finally
        {
            containment.Dispose();

            // A read still pending is abandoned rather than disposed under: disposing a pipe with a
            // blocked read can hang on Windows, and the process handle is released by its finalizer.
            if (released)
            {
                process.Dispose();
            }
            else
            {
                _ = reading.ContinueWith(_ => process.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }

    private static CapturedResult NotStarted(ChildOutcome outcome, string error) =>
        new(outcome, null, false, CapturedOutput.Empty, CapturedOutput.Empty, error);

    private static async Task ReadAsync(Stream stream, Kept kept)
    {
        var chunk = new byte[_chunk];

        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                kept.Write(chunk.AsSpan(0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The pipe went away with its writers: what was read is what there is.
        }
        finally
        {
            Array.Clear(chunk);
        }
    }

    /// <summary>The last bytes of one stream, as a ring.</summary>
    private sealed class Kept(int capacity)
    {
        private readonly Lock _gate = new();
        private readonly byte[] _ring = new byte[Math.Max(1, capacity)];
        private int _next;
        private long _total;

        internal void Write(ReadOnlySpan<byte> bytes)
        {
            lock (_gate)
            {
                _total += bytes.Length;

                if (bytes.Length >= _ring.Length)
                {
                    bytes[^_ring.Length..].CopyTo(_ring);
                    _next = 0;
                    return;
                }

                var first = Math.Min(bytes.Length, _ring.Length - _next);
                bytes[..first].CopyTo(_ring.AsSpan(_next));
                bytes[first..].CopyTo(_ring);
                _next = (_next + bytes.Length) % _ring.Length;
            }
        }

        internal CapturedOutput Snapshot()
        {
            lock (_gate)
            {
                if (_total <= _ring.Length)
                {
                    return new CapturedOutput(_ring[..(int)_total], _total, false);
                }

                var bytes = new byte[_ring.Length];
                _ring.AsSpan(_next).CopyTo(bytes);
                _ring.AsSpan(0, _next).CopyTo(bytes.AsSpan(_ring.Length - _next));
                return new CapturedOutput(bytes, _total, true);
            }
        }
    }
}
