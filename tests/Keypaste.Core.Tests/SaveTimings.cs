using System.Collections.Concurrent;
using System.Globalization;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Reads <see cref="SaveClock"/> for the tests.</summary>
internal static class SaveTimings
{
    /// <summary>Runs <paramref name="save"/> and returns the timing of the one save it made on this thread.</summary>
    internal static SaveTiming Of(Action save)
    {
        var thread = Environment.CurrentManagedThreadId;
        var seen = new ConcurrentQueue<SaveTiming>();

        void OnThisThread(SaveTiming timing)
        {
            if (timing.Thread == thread)
            {
                seen.Enqueue(timing);
            }
        }

        SaveClock.Observed += OnThisThread;
        try
        {
            save();
        }
        finally
        {
            SaveClock.Observed -= OnThisThread;
        }

        return Assert.Single(seen);
    }

    /// <summary>
    /// Space-separated <c>key=value</c>, milliseconds rounded, <c>-</c> for a step not taken. Every list
    /// has one slash-separated value per attempt, and <c>held</c> marks the F.12 shape for the reader.
    /// </summary>
    internal static string Describe(SaveTiming timing) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"op={timing.Operation} ok={(timing.Succeeded ? 1 : 0)} " +
            $"check={Ms(timing.Check)} redirect={Ms(timing.Redirect)} " +
            $"gate={Each(timing, a => Ms(a.Gate))} heldby={Each(timing, a => a.Gate is null ? "-" : a.HeldBy.ToString(CultureInfo.InvariantCulture))} " +
            $"held={Each(timing, a => Ms(a.Held))} rereads={Each(timing, a => Ms(a.Reread))} " +
            $"work={Each(timing, a => Ms(a.Work))} waits={Each(timing, a => Ms(a.Wait))} " +
            $"stamp={Ms(timing.Stamp)} total={Ms(timing.Total)}");

    internal static TimeSpan Sum(SaveTiming timing, Func<AttemptTiming, TimeSpan?> part) =>
        timing.Attempts.Aggregate(TimeSpan.Zero, (sum, attempt) => sum + (part(attempt) ?? TimeSpan.Zero));

    private static string Ms(TimeSpan? span) =>
        span is { } value ? Math.Round(value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) : "-";

    private static string Each(SaveTiming timing, Func<AttemptTiming, string> part) =>
        timing.Attempts.Count == 0 ? "-" : string.Join('/', timing.Attempts.Select(part));
}
