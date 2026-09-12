using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Keypaste.Core.Tests;

/// <summary>
/// What the thread pool was doing while a bounded wait ran, and how late it delivered a timer and a
/// work item while that wait was running.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because of F.9.</b> Four bounded waits across three subsystems overran five- to
/// elevenfold in one probe dispatch, and thread-pool starvation is the one thing common to all four.
/// D-0125 records that as inferred from the probe's load and never observed. This is the instrument
/// that takes it out of that state, and the arm knobs it reads back are what prove a probe run
/// measured the pool it claimed to.
/// </para>
/// <para>
/// <b>The sampler runs while the wait runs, not after it.</b> A reading taken at the assertion is
/// taken from a pool that has already drained — the overrun is over by the time anything notices it —
/// so a single delay timed at that point measures a recovered machine and decides nothing.
/// <see cref="Watch"/> starts before the wait and stops when it ends, and reports the worst it saw in
/// between.
/// </para>
/// <para>
/// <b>It samples from a dedicated thread.</b> A sampler the starvation can delay cannot report the
/// starvation: it would come back late for the same reason its subject did, and read as though
/// nothing had happened.
/// </para>
/// <para>
/// <b>Two latencies, because the four sites do not wait on one thing.</b> A connect budget is a
/// <c>CancelAfter</c> timer and a <c>WhenAny</c> race is a continuation, so "the pool was busy" is
/// not one reading. <see cref="PoolWatch.WorstTimer"/> is how late a timer arrived and
/// <see cref="PoolWatch.WorstQueue"/> is how long a queued work item waited to start; a site whose
/// budget overran while both stayed flat did not overrun for this reason.
/// </para>
/// <para>
/// <b>Nothing here can reach a vault.</b> It reads runtime counters and a clock. Unlike
/// <c>ListingCall</c> it has no credential path to stay off (docs/PRODUCT.md law 3.5) because there
/// is no argument through which one could arrive, which is why it is safe to print whole into a CI
/// log that outlives the run.
/// </para>
/// </remarks>
internal static class PoolSnapshot
{
    /// <summary>Starts watching the pool, and stops when the returned handle is disposed.</summary>
    /// <param name="what">The wait being measured, named for the log.</param>
    /// <returns>The watch, to be disposed when the wait ends.</returns>
    internal static PoolWatch Watch(string what) => new(what);

    /// <summary>The pool's counters, as they are right now.</summary>
    internal static PoolCounters Now() => PoolCounters.Read();
}

/// <summary>Every number the runtime will say about the pool at one instant.</summary>
/// <remarks>
/// <c>MinWorker</c> is here for a reason beyond diagnosis: it is the readback that proves a probe
/// arm's knob landed. An arm whose log does not show the floor it asked for measured the default,
/// and its count belongs to a different experiment than the one it was filed under.
/// </remarks>
internal readonly record struct PoolCounters(
    int Cpus,
    int MinWorker,
    int MinIo,
    int MaxWorker,
    int MaxIo,
    int AvailableWorker,
    int AvailableIo,
    int ThreadCount,
    long PendingWorkItems,
    long CompletedWorkItems)
{
    /// <summary>Reads them.</summary>
    internal static PoolCounters Read()
    {
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
        ThreadPool.GetAvailableThreads(out var freeWorker, out var freeIo);

        return new PoolCounters(
            Environment.ProcessorCount,
            minWorker,
            minIo,
            maxWorker,
            maxIo,
            freeWorker,
            freeIo,
            ThreadPool.ThreadCount,
            ThreadPool.PendingWorkItemCount,
            ThreadPool.CompletedWorkItemCount);
    }

    /// <summary>One line, in the order a reader asks the questions.</summary>
    public override string ToString() =>
        $"cpus {Cpus}; min {MinWorker}/{MinIo}; max {MaxWorker}/{MaxIo}; free {AvailableWorker}/{AvailableIo}; " +
        $"threads {ThreadCount}; pending {PendingWorkItems}; completed {CompletedWorkItems}";
}

/// <summary>
/// Samples the pool for the length of one wait, from a thread the pool does not own.
/// </summary>
/// <remarks>
/// The cadence is deliberately short relative to the budgets under measurement — the tightest is the
/// five-hundred-millisecond connect — so a wait that overran has tens of samples rather than one, and
/// the worst of them is a reading about the wait rather than about whichever instant the assertion
/// happened to land on.
/// </remarks>
internal sealed class PoolWatch : IDisposable
{
    /// <summary>How long each timer sample asks to sleep for.</summary>
    private const int _cadenceMilliseconds = 50;

    private readonly string _what;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread? _thread;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly PoolCounters _entry = PoolCounters.Read();

    private TimeSpan _worstTimer;
    private TimeSpan _worstQueue;
    private PoolCounters _atWorst;
    private int _samples;
    private bool _disposed;

    internal PoolWatch(string what)
    {
        _what = what;
        _atWorst = _entry;

        // Sampling only when a probe asked for it. The sampler costs a thread and a timer every
        // fifty milliseconds for the length of the wait, and three of the four sites it wraps are
        // themselves timing budgets — an instrument that runs on every ordinary green run would be
        // load applied to the thing it is there to measure. The counters above are free and are
        // taken either way.
        if (!PoolTimeline.Asked)
        {
            return;
        }

        _thread = new Thread(Sample)
        {
            IsBackground = true,
            Name = "f9-pool-watch",
        };

        _thread.Start();
    }

    /// <summary>The latest a timer arrived during the wait, past the time it was asked for.</summary>
    internal TimeSpan WorstTimer => _worstTimer;

    /// <summary>The longest a queued work item waited before it started running.</summary>
    internal TimeSpan WorstQueue => _worstQueue;

    /// <summary>How long the wait has been running.</summary>
    internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

    /// <summary>Stops sampling. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    /// <summary>
    /// What the pool did while this wait ran. Built on the overrun path and nowhere else.
    /// </summary>
    /// <remarks>
    /// <b>Never pass this to a passing assertion.</b> <c>Assert.True(condition, message)</c>
    /// evaluates its message eagerly, so handing it this string would run a sampler and read the
    /// pool on every green assertion — inside tests whose subject is a timing budget, which is the
    /// one place an instrument must not be. Call it after the comparison has already failed.
    /// </remarks>
    internal string Report()
    {
        var report = new StringBuilder();

        report.AppendLine(CultureInfo.InvariantCulture, $"{_what}: {(int)Elapsed.TotalMilliseconds} ms, {(_thread is null ? $"not sampled ({PoolTimeline.DirectoryVariable} unset)" : $"{_samples} pool sample(s)")}")
            .AppendLine(CultureInfo.InvariantCulture, $"  worst timer lateness: {(int)_worstTimer.TotalMilliseconds} ms past a {_cadenceMilliseconds} ms delay")
            .AppendLine(CultureInfo.InvariantCulture, $"  worst queue latency:  {(int)_worstQueue.TotalMilliseconds} ms before a queued item started")
            .AppendLine(CultureInfo.InvariantCulture, $"  pool at entry:   {_entry}")
            .Append(CultureInfo.InvariantCulture, $"  pool at worst:   {_atWorst}");

        return report.ToString();
    }

    private void Sample()
    {
        while (!_stop.IsCancellationRequested)
        {
            TimeSpan timer;
            TimeSpan queue;

            try
            {
                var asked = Stopwatch.GetTimestamp();
                Task.Delay(_cadenceMilliseconds, _stop.Token).Wait(_stop.Token);
                timer = Stopwatch.GetElapsedTime(asked) - TimeSpan.FromMilliseconds(_cadenceMilliseconds);

                var queued = Stopwatch.GetTimestamp();
                var ran = Task.Run(Stopwatch.GetTimestamp, _stop.Token);
                ran.Wait(_stop.Token);
                queue = Stopwatch.GetElapsedTime(queued, ran.Result);
            }
            catch (Exception ex) when (ex is OperationCanceledException or AggregateException)
            {
                // The wait ended. Everything sampled before it did still stands.
                return;
            }

            _samples++;

            if (timer > _worstTimer || queue > _worstQueue)
            {
                _worstTimer = timer > _worstTimer ? timer : _worstTimer;
                _worstQueue = queue > _worstQueue ? queue : _worstQueue;
                _atWorst = PoolCounters.Read();
            }
        }
    }
}

/// <summary>
/// One append-only file per process, so an overrun in one test assembly can be lined up against what
/// another was doing at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only instrument that can test F.9's producer hypothesis.</b> The candidate
/// mechanism is that a blocking save in <c>Keypaste.Core.Tests</c> holds pool threads while a listing
/// in <c>Keypaste.Mcp.Tests</c> waits on a deadline the pool then delivers late — and
/// <c>dotnet test keypaste.slnx</c> runs those assemblies as concurrent <em>processes</em>. Counters
/// describe one process; the hypothesis is about two, so the correlation has to be written down
/// somewhere both can reach.
/// </para>
/// <para>
/// <b>The timestamps are comparable across processes.</b> <see cref="Stopwatch.GetTimestamp"/> is
/// QPC on Windows, which is machine-wide, so two files written by two processes on one runner merge
/// into one ordered timeline. <see cref="Stopwatch.Frequency"/> is written into every line rather
/// than assumed by whatever reads them.
/// </para>
/// <para>
/// <b>It fails loudly rather than empty.</b> The whole timeline is behind an environment variable so
/// an ordinary run carries none of it — which means a probe that sets the variable somewhere the test
/// host does not inherit produces a full set of failure counts and an empty artifact, and the
/// measurement is unrepeatable without another dispatch. So when the variable is set and the file
/// cannot be written, this throws with the variable and the path in the sentence, and the probe
/// checks the artifact is non-empty before it reports a count.
/// </para>
/// <para>
/// Nothing vault-derived is written: an event name this file declares, a process id, an assembly
/// name and a tick count.
/// </para>
/// </remarks>
internal static class PoolTimeline
{
    /// <summary>The variable naming the directory to write into. Unset means write nothing.</summary>
    internal const string DirectoryVariable = "KEYPASTE_F9_TIMELINE";

    private static readonly Lock _gate = new();
    private static readonly string? _path = Resolve();

    /// <summary>Whether the probe asked for a timeline.</summary>
    internal static bool Asked => _path is not null;

    /// <summary>
    /// Writes this process onto the timeline the moment the assembly loads.
    /// </summary>
    /// <remarks>
    /// <b>So that an empty artifact means one thing.</b> Marking lazily, at the first wait that
    /// happens to be instrumented, makes an empty timeline ambiguous: the variable may never have
    /// reached the test host, or the run may simply have ended before it reached one of the four
    /// sites. The first costs a whole dispatch and the second costs nothing, and a probe cannot tell
    /// them apart after the fact. Marking at load means a process that started at all leaves a file,
    /// so an empty directory says the variable did not arrive and says only that.
    /// </remarks>
    [ModuleInitializer]
    internal static void Opened()
    {
        if (_path is not null)
        {
            Mark("opened");
        }
    }

    /// <summary>Where this process is writing, for a diagnostic that has to name it.</summary>
    internal static string? Path => _path;

    /// <summary>Records that something started or finished, if a timeline was asked for.</summary>
    /// <param name="happening">What happened. An identifier this repository chose, never input.</param>
    /// <param name="detail">An optional count or budget, never a name or a value.</param>
    internal static void Mark(string happening, string? detail = null)
    {
        if (_path is null)
        {
            return;
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"ticks":{{Stopwatch.GetTimestamp()}},"freq":{{Stopwatch.Frequency}},"pid":{{Environment.ProcessId}},"asm":"{{Assembly()}}","event":"{{happening}}","detail":"{{detail ?? string.Empty}}"}""");

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"{DirectoryVariable} is set, so this run is a measurement, and its timeline could not be " +
                    $"written to {_path}. A probe run whose timeline is empty cannot answer what it was " +
                    $"dispatched to answer, so it fails here instead of reporting a count nothing can explain.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Throws unless this process has written a timeline the probe can collect.
    /// </summary>
    /// <remarks>
    /// A guard test in each participating assembly calls this, so an assembly that inherited the
    /// variable and wrote nothing is a red test rather than an artifact nobody looks at until the
    /// counts are already spent.
    /// </remarks>
    internal static void EnsureWritten()
    {
        if (_path is null)
        {
            return;
        }

        Mark("guard");

        var written = File.Exists(_path) ? new FileInfo(_path).Length : 0;

        if (written == 0)
        {
            throw new InvalidOperationException(
                $"{DirectoryVariable} is set and {_path} is empty, so nothing in this assembly is on the " +
                "timeline and the producer correlation cannot be read from this run.");
        }
    }

    private static string Assembly() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";

    private static string? Resolve()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        Directory.CreateDirectory(directory);

        return System.IO.Path.Combine(directory, $"f9-timeline-{Environment.ProcessId}.jsonl");
    }
}
