using Polly;
using Polly.Retry;

namespace Keypaste.Core.Internal;

/// <summary>Retries whose waits block the calling thread.</summary>
/// <remarks>
/// The caller's <c>wait</c> runs between attempts and Polly's own delay is zero: Polly waits a synchronous retry on a pool
/// timer, which a process with no pool worker free does not fire in time (F.9).
/// </remarks>
internal static class Retries
{
    /// <summary>Tries up to <paramref name="attempts"/> times while <paramref name="again"/> holds for an attempt's outcome.</summary>
    /// <param name="attempts">The most attempts, the first included.</param>
    /// <param name="again">Whether an attempt's outcome, its result boxed, is worth another attempt.</param>
    /// <param name="wait">Called between attempts with the number, from 1, of the attempt that failed.</param>
    internal static ResiliencePipeline OnThisThread(int attempts, Func<Outcome<object>, bool> again, Action<int> wait) =>
        attempts < 2
            ? ResiliencePipeline.Empty
            : new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = args => ValueTask.FromResult(again(args.Outcome)),
                    MaxRetryAttempts = attempts - 1,
                    Delay = TimeSpan.Zero,
                    OnRetry = args =>
                    {
                        wait(args.AttemptNumber + 1);
                        return default;
                    },
                })
                .Build();
}
