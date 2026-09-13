using Keypaste.Core.Ipc;
using Keypaste.Mcp;
using Keypaste.Mcp.Tests;

namespace Keypaste.PoolStarver;

/// <summary>Runs one F.9 scenario in a pool of exactly two workers, and says what the product answered.</summary>
/// <remarks>
/// <para>
/// <b>Launched with its pool already pinned.</b> The parent sets
/// <c>DOTNET_ThreadPool_ForceMinWorkerThreads</c> and <c>DOTNET_ThreadPool_ForceMaxWorkerThreads</c>
/// to two before this process exists, so no worker can be injected while a scenario holds both. The
/// pinning is read back first and a process that did not get it refuses to measure anything: a green
/// run from a pool that could still grow would prove nothing about a pool that cannot.
/// </para>
/// <para>
/// <b>It never asserts.</b> It prints what it arranged and what the product returned, and the test
/// that launched it decides — the same division as <c>Keypaste.VaultSaver</c>. Exit 0 means a
/// measurement was taken, 3 means the condition could not be arranged, and a test must treat 3 as a
/// failure rather than as a pass it has no reading for.
/// </para>
/// </remarks>
internal static class Program
{
    private const int _workers = 2;

    private static readonly TimeSpan _arrange = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _finish = TimeSpan.FromSeconds(30);

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: <scenario> <pipe-name>   (with the pool pinned to two workers)");
            return 2;
        }

        ThreadPool.GetMinThreads(out var minWorker, out _);
        ThreadPool.GetMaxThreads(out var maxWorker, out _);
        Console.WriteLine($"workers {minWorker}..{maxWorker}");

        if (minWorker != _workers || maxWorker != _workers)
        {
            return Refuse($"the pool was not pinned to {_workers} workers, so no shortage can be arranged");
        }

        return args[0] switch
        {
            "connect-behind-its-deadline" => ConnectBehindItsDeadline(args[1]),
            "an-open-harness" => AnOpenHarness(),
            _ => Refuse($"no scenario named {args[0]}"),
        };
    }

    /// <summary>
    /// A request started by a worker that stays busy, while the only other worker is busy until the
    /// connect budget has run out.
    /// </summary>
    /// <remarks>
    /// Work queued from a pool thread goes to that thread's own queue, and a free worker takes an
    /// expired timer before it steals from another thread's queue (runtime v10.0.10,
    /// <c>ThreadPoolWorkQueue.Enqueue</c> and <c>Dequeue</c>). So when the product's connect is a pool
    /// work item and its budget is a pool timer, the worker freed here runs the expired budget first
    /// and reaches the connect only afterwards, with its token already cancelled — the approver on the
    /// other end is never tried. A connect that needs no worker is tried at once, whatever the pool is
    /// doing, and this scenario cannot make it fail.
    /// </remarks>
    private static int ConnectBehindItsDeadline(string pipeName)
    {
        using var otherBusy = new ManualResetEventSlim();
        using var releaseOther = new ManualResetEventSlim();
        using var requested = new ManualResetEventSlim();
        using var releaseCaller = new ManualResetEventSlim();

        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            otherBusy.Set();
            releaseOther.Wait();
        }, null);

        if (!otherBusy.Wait(_arrange))
        {
            return Refuse("the first worker never started");
        }

        var connection = new ApproverConnection(pipeName);

        try
        {
            return Arranged(connection, requested, releaseCaller, releaseOther);
        }
        finally
        {
            releaseOther.Set();
            releaseCaller.Set();
            connection.DisposeAsync().AsTask().Wait(_finish);
        }
    }

    private static int Arranged(
        ApproverConnection connection,
        ManualResetEventSlim requested,
        ManualResetEventSlim releaseCaller,
        ManualResetEventSlim releaseOther)
    {
        var call = new Call();

        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            call.Task = connection.ListAsync(new NamesRequest(["env/**"]), CancellationToken.None).AsTask();
            requested.Set();
            releaseCaller.Wait();
        }, null);

        if (!requested.Wait(_arrange))
        {
            return Refuse("the calling worker never started the request");
        }

        ThreadPool.GetAvailableThreads(out var free, out _);
        Console.WriteLine($"free workers once arranged {free}");

        if (free != 0)
        {
            return Refuse("a worker was still free once both were meant to be busy, so nothing was short");
        }

        // Well past the budget, so the budget's timer has fired and is queued before a worker is free.
        Thread.Sleep(ApproverConnection.ConnectTimeout * 3);

        releaseOther.Set();
        var finished = call.Task!.Wait(_finish);
        releaseCaller.Set();

        Console.WriteLine(finished ? $"outcome {call.Task.Result.Outcome}" : "outcome unfinished");

        return 0;
    }

    /// <summary>
    /// The two reads an open MCP harness keeps outstanding — the server waiting for a request and the
    /// client waiting for a reply — and whether anything else in the process can still get a worker.
    /// </summary>
    /// <remarks>
    /// Every harness keeps both reads outstanding for as long as it is open, and nothing is written
    /// while a test is between calls. A channel that cannot read asynchronously serves a read by
    /// parking a pool worker in a blocking read until data arrives (<c>PipeStream.AsyncOverSyncRead</c>),
    /// which is what pool-probe dumps showed six to eight workers doing while everything else in the
    /// test host queued. With two workers, one open harness holds both, and the canary queued after it
    /// never runs.
    /// </remarks>
    private static int AnOpenHarness()
    {
        using var channels = HarnessChannels.Open();

        var serverWaiting = channels.ServerReads.ReadAsync(new byte[1]).AsTask();
        var clientWaiting = channels.ClientReads.ReadAsync(new byte[1]).AsTask();

        // Long enough for a read that needs a worker to have taken one.
        Thread.Sleep(TimeSpan.FromSeconds(1));

        if (serverWaiting.IsCompleted || clientWaiting.IsCompleted)
        {
            return Refuse("a read completed with nothing written, so no harness was waiting");
        }

        Console.WriteLine("both reads outstanding");

        using var ran = new ManualResetEventSlim();
        ThreadPool.UnsafeQueueUserWorkItem(_ => ran.Set(), null);

        Console.WriteLine(ran.Wait(TimeSpan.FromSeconds(5)) ? "canary ran" : "canary never ran");

        // Answer both reads so the process can end, whichever way the canary went.
        channels.ClientWrites.Write([1]);
        channels.ClientWrites.Flush();
        channels.ServerWrites.Write([1]);
        channels.ServerWrites.Flush();
        Task.WaitAll([serverWaiting, clientWaiting], _finish);

        return 0;
    }

    private static int Refuse(string why)
    {
        Console.Error.WriteLine($"not arranged: {why}");
        return 3;
    }

    private sealed class Call
    {
        public Task<(NamesReply? Reply, ApproverOutcome Outcome)>? Task { get; set; }
    }
}
