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

    /// <summary>Space-separated <c>key=value</c>, lists slash-separated, milliseconds rounded; <c>-</c> for a step not taken.</summary>
    internal static string Describe(SaveTiming timing) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"op={timing.Operation} heldby={timing.HeldBy} ok={(timing.Succeeded ? 1 : 0)} " +
            $"check={Ms(timing.Check)} redirect={Ms(timing.Redirect)} gate={Ms(timing.GateWait)} " +
            $"work={Each(timing.Attempts)} waits={Each(timing.Waits)} rereads={Each(timing.Rereads)} " +
            $"stamp={Ms(timing.Stamp)} total={Ms(timing.Total)}");

    internal static double Milliseconds(IEnumerable<TimeSpan> spans) => spans.Sum(span => span.TotalMilliseconds);

    private static string Ms(TimeSpan? span) =>
        span is { } value ? Math.Round(value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) : "-";

    private static string Each(IReadOnlyList<TimeSpan> spans) =>
        spans.Count == 0 ? "-" : string.Join('/', spans.Select(span => Ms(span)));
}
