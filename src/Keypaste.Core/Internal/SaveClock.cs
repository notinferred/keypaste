using System.Diagnostics;

namespace Keypaste.Core.Internal;

/// <summary>Where one save's caller wait went, component by component (docs/STEPS.md F.10a).</summary>
/// <remarks>
/// The components are disjoint and together are the caller's whole wait: <see cref="Total"/> is
/// <see cref="Check"/> + <see cref="Redirect"/> + <see cref="GateWait"/> + <see cref="Attempts"/> +
/// <see cref="Waits"/> + <see cref="Rereads"/> + <see cref="Stamp"/>, plus the loop's own overhead.
/// The retry schedule describes <see cref="Waits"/> alone and bounds none of the others.
/// </remarks>
internal readonly record struct SaveTiming(
    long Operation,
    int Thread,
    long HeldBy,
    bool Succeeded,
    TimeSpan? Check,
    TimeSpan Redirect,
    TimeSpan? GateWait,
    int? GatedAttempt,
    IReadOnlyList<TimeSpan> Attempts,
    IReadOnlyList<TimeSpan> Waits,
    IReadOnlyList<TimeSpan> Rereads,
    TimeSpan? Stamp,
    TimeSpan Total);

/// <summary>Collects one save's <see cref="SaveTiming"/> as it happens.</summary>
internal sealed class SaveClock
{
    private static long _lastOperation;

    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly List<TimeSpan> _attempts = [];
    private readonly List<TimeSpan> _waits = [];
    private readonly List<TimeSpan> _rereads = [];
    private TimeSpan? _check;
    private TimeSpan? _stamp;
    private TimeSpan _redirect;
    private TimeSpan? _gateWait;
    private int? _gatedAttempt;

    /// <summary>Raised once per save, successful or not. Nothing subscribes outside a test.</summary>
    internal static event Action<SaveTiming>? Observed;

    internal long Operation { get; } = Interlocked.Increment(ref _lastOperation);

    /// <summary>The operation holding the save gate when this one began waiting for it; 0 for none.</summary>
    internal long HeldBy { get; set; }

    internal T Check<T>(Func<T> read) => Time(read, elapsed => _check = elapsed);

    internal void Redirect(Action redirect) => Time(redirect, elapsed => _redirect = elapsed);

    /// <summary>Times the one wait for the gate, taken before attempt <paramref name="attempt"/>.</summary>
    internal void WaitForGate(int attempt, Action wait)
    {
        _gatedAttempt = attempt;
        Time(wait, elapsed => _gateWait = elapsed);
    }

    internal void Attempt(Action attempt) => Time(attempt, _attempts.Add);

    internal void Wait(Action wait) => Time(wait, _waits.Add);

    internal T Reread<T>(Func<T> read) => Time(read, _rereads.Add);

    internal T Stamp<T>(Func<T> read) => Time(read, elapsed => _stamp = elapsed);

    internal void Publish(bool succeeded) =>
        Observed?.Invoke(new SaveTiming(
            Operation,
            Environment.CurrentManagedThreadId,
            HeldBy,
            succeeded,
            _check,
            _redirect,
            _gateWait,
            _gatedAttempt,
            _attempts,
            _waits,
            _rereads,
            _stamp,
            Stopwatch.GetElapsedTime(_started)));

    private static void Time(Action action, Action<TimeSpan> record) =>
        Time<object?>(() => { action(); return null; }, record);

    private static T Time<T>(Func<T> action, Action<TimeSpan> record)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return action();
        }
        finally
        {
            record(Stopwatch.GetElapsedTime(started));
        }
    }
}
