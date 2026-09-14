using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Reads <see cref="SaveClock"/> for the tests and writes it onto the F.9 timeline.</summary>
/// <remarks>
/// Every save in this process becomes one <c>save-timing</c> line when a timeline was asked for, so a
/// save that waited can be read beside the operation it waited behind. A test that wants its own
/// save found among them marks <c>save-op</c> with the operation it got; <c>scripts/f9-timeline.sh
/// --saves</c> joins the two. Only counts and durations are written.
/// </remarks>
internal static class SaveTimings
{
    [ModuleInitializer]
    internal static void WriteOntoTheTimeline()
    {
        if (PoolTimeline.Asked)
        {
            SaveClock.Observed += timing => PoolTimeline.Mark("save-timing", Describe(timing));
        }
    }

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

    /// <summary>Records which operation a labelled save was, so the reader can find its timing.</summary>
    internal static void Mark(string label, SaveTiming timing) =>
        PoolTimeline.Mark("save-op", string.Create(CultureInfo.InvariantCulture, $"label={label} op={timing.Operation}"));

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
