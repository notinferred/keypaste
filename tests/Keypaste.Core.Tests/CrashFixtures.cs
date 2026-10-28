using System.Runtime.CompilerServices;
using Keypaste.Core.CrashReports;

namespace Keypaste.Core.Tests;

/// <summary>Exceptions with real stacks, and the context the crash-report tests report them under.</summary>
internal static class CrashFixtures
{
    internal static readonly DateTimeOffset At = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    internal static CrashContext Context { get; } = new(At, TimeSpan.FromMinutes(5), "agents");

    /// <summary>The exception the action throws, with the stack its throw gave it.</summary>
    internal static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("the action threw nothing");
    }

    /// <summary>Text no name, number or marker in a report holds by chance.</summary>
    internal static string Sentinel(string label) => $"{label}-{Guid.NewGuid():N}";

    /// <summary>
    /// An aggregate of four exceptions, each thrown sixty calls down in a type with a long name, so its report holds more than
    /// 16 KiB of frames before the scrubber drops some.
    /// </summary>
    internal static Exception Large() => Thrown(() =>
        A_type_whose_long_name_gives_each_frame_of_these_reports_more_room.Deep(60, () =>
            throw new AggregateException(Enumerable.Range(0, 4).Select(_ => Thrown(() =>
                A_type_whose_long_name_gives_each_frame_of_these_reports_more_room.Deep(60, () => throw new IOException()))).ToList())));

    /// <summary>Runs the action <paramref name="depth"/> calls down, so its exception's stack is at least that deep.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Deep(int depth, Action action)
    {
        if (depth == 0)
        {
            action();
            return 0;
        }

        // Adding after the call keeps it out of tail position, where the JIT could drop the frame.
        return Deep(depth - 1, action) + 1;
    }

    private static class A_type_whose_long_name_gives_each_frame_of_these_reports_more_room
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Deep(int depth, Action action)
        {
            if (depth == 0)
            {
                action();
                return 0;
            }

            return Deep(depth - 1, action) + 1;
        }
    }
}
