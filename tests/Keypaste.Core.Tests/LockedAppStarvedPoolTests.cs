using System.Diagnostics;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A locked app whose thread pool has no thread free still takes the next bridge that knocks (F.70).</summary>
/// <remarks>
/// Alone in its collection, because it caps this process's pool and holds every thread of it, which would hold up any
/// test running beside it.
/// </remarks>
[Collection(nameof(PoolStarvedAlone))]
public sealed class LockedAppStarvedPoolTests : IDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-starved-pool-tests-").FullName;
    private readonly string _name;

    public LockedAppStarvedPoolTests() =>
        _name = LockedAppEndpoint.NameFor(VaultIdentity.Of(_home, Path.Combine(_home, "vault.kdbx")));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BridgesKnockingWhileNoPoolThreadIsFree_ReachTheApp_AndAreHeardOnceOneIs()
    {
        var heard = 0;
        string? unreachable = null;
        using var endpoint = LockedAppEndpoint.Listen(_name, () => Interlocked.Increment(ref heard), why => unreachable = why);

        // The first reaches the instance Listen made; before F.70 the second, on Windows, waited for a pool thread to make the next.
        var reached = await Task.Factory.StartNew(
            () => WhileNoPoolThreadIsFree(() => new[] { Knock(), Knock() }),
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

        Assert.True(reached[0], "the first bridge did not reach the app");
        Assert.True(reached[1], "a bridge knocking while the app had no pool thread free did not reach it");
        Assert.True(await HeardAsync(() => Volatile.Read(ref heard), 2), "the app did not hear both bridges once a pool thread was free");

        for (var i = 0; i < 5; i++)
        {
            Assert.True(await LockedAppEndpoint.TellAsync(_name, Token), $"bridge {i} after the pool came free did not reach the app");
        }

        Assert.True(await HeardAsync(() => Volatile.Read(ref heard), 7), "the app did not hear the bridges after the pool came free");
        Assert.Equal(7, endpoint.Requests);
        Assert.Null(unreachable);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }

    private static async Task<bool> HeardAsync(Func<int> heard, int expected)
    {
        var started = Stopwatch.GetTimestamp();
        while (heard() < expected && Stopwatch.GetElapsedTime(started) < _wait)
        {
            await Task.Delay(20, Token);
        }

        return heard() == expected;
    }

    // On a thread of its own, so the knock needs no pool thread either.
    private bool Knock() => LockedAppEndpoint.TellAsync(_name, CancellationToken.None).GetAwaiter().GetResult();

    // Caps the pool at its floor and holds every one of its threads until act has run.
    private static T WhileNoPoolThreadIsFree<T>(Func<T> act)
    {
        ThreadPool.GetMinThreads(out var floor, out _);
        ThreadPool.GetMaxThreads(out var workers, out var completions);
        Assert.True(ThreadPool.SetMaxThreads(floor, completions), "the pool could not be capped");
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            for (var i = 0; i < floor; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ => released.Task.Wait(), null);
            }

            var started = Stopwatch.GetTimestamp();
            int free;
            while (true)
            {
                ThreadPool.GetAvailableThreads(out free, out _);
                if (free == 0 || Stopwatch.GetElapsedTime(started) > _wait)
                {
                    break;
                }

                Thread.Sleep(10);
            }

            Assert.True(free == 0, $"{free} of the pool's {floor} threads stayed free");
            return act();
        }
        finally
        {
            released.SetResult();
            ThreadPool.SetMaxThreads(workers, completions);
        }
    }
}

[CollectionDefinition(nameof(PoolStarvedAlone), DisableParallelization = true)]
public sealed class PoolStarvedAlone;
