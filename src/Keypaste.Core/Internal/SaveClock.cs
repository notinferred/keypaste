using System.Diagnostics;

namespace Keypaste.Core.Internal;

/// <summary>Where one save's caller wait went, component by component (docs/STEPS.md F.10a).</summary>
/// <remarks>
/// The components are disjoint and together are the caller's whole wait: <see cref="Total"/> is
/// <see cref="Check"/> + <see cref="Redirect"/> + every attempt's parts + <see cref="Stamp"/>, plus
/// the loop's own overhead. The retry schedule describes the attempts' <see cref="AttemptTiming.Wait"/>
/// alone and bounds none of the others.
/// </remarks>
internal readonly record struct SaveTiming(
    long Operation,
    int Thread,
    bool Succeeded,
    TimeSpan? Check,
    TimeSpan Redirect,
    IReadOnlyList<AttemptTiming> Attempts,
    TimeSpan? Stamp,
    TimeSpan Total);

/// <summary>One attempt, in the order its parts happen: gate wait, re-read, work, then the sleep after it.</summary>
/// <remarks>
/// <see cref="HeldBy"/> is the operation holding the gate when this attempt began waiting for it, 0
/// for none; <see cref="Held"/> runs from taking the gate to releasing it, null for an ungated attempt.
/// </remarks>
internal readonly record struct AttemptTiming(
    TimeSpan? Gate,
    long HeldBy,
    TimeSpan? Held,
    TimeSpan? Reread,
    TimeSpan? Work,
    TimeSpan? Wait);

/// <summary>Collects one save's <see cref="SaveTiming"/> as it happens.</summary>
internal sealed class SaveClock
{
    private static long _lastOperation;

    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly List<AttemptTiming> _attempts = [];
    private TimeSpan? _check;
    private TimeSpan? _stamp;
    private TimeSpan _redirect;
    private int _gatedAttempt = -1;
    private long _gateTaken;

    /// <summary>Raised once per save, successful or not. Nothing subscribes outside a test.</summary>
    internal static event Action<SaveTiming>? Observed;

    internal long Operation { get; } = Interlocked.Increment(ref _lastOperation);

    internal T Check<T>(Func<T> read) => Time(read, elapsed => _check = elapsed);

    internal void Redirect(Action redirect) => Time(redirect, elapsed => _redirect = elapsed);

    internal void BeginAttempt() => _attempts.Add(default);

    /// <summary>Times this attempt's wait for the gate, which <paramref name="heldBy"/> held as it began.</summary>
    internal void WaitForGate(long heldBy, Action wait)
    {
        Time(wait, elapsed => Current = Current with { Gate = elapsed, HeldBy = heldBy });
        _gatedAttempt = _attempts.Count - 1;
        _gateTaken = Stopwatch.GetTimestamp();
    }

    /// <summary>Records the hold against the attempt that took the gate.</summary>
    internal void ReleaseGate()
    {
        _attempts[_gatedAttempt] = _attempts[_gatedAttempt] with { Held = Stopwatch.GetElapsedTime(_gateTaken) };
    }

    internal T Reread<T>(Func<T> read) => Time(read, elapsed => Current = Current with { Reread = elapsed });

    internal void Attempt(Action attempt) => Time(attempt, elapsed => Current = Current with { Work = elapsed });

    internal void Wait(Action wait) => Time(wait, elapsed => Current = Current with { Wait = elapsed });

    internal T Stamp<T>(Func<T> read) => Time(read, elapsed => _stamp = elapsed);

    internal void Publish(bool succeeded) =>
        Observed?.Invoke(new SaveTiming(
            Operation,
            Environment.CurrentManagedThreadId,
            succeeded,
            _check,
            _redirect,
            _attempts,
            _stamp,
            Stopwatch.GetElapsedTime(_started)));

    private AttemptTiming Current
    {
        get => _attempts[^1];
        set => _attempts[^1] = value;
    }

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
