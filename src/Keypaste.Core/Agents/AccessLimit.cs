using System.Globalization;

namespace Keypaste.Core.Agents;

/// <summary>An optional limit on what Automatic releases: at most so many an hour, and only until a time.</summary>
/// <remarks>Reaching either asks instead of refusing: the request falls back to Ask, allow for a while.</remarks>
public sealed record AccessLimit
{
    /// <summary>The most releases an hour a limit may allow, which bounds the memory its window costs.</summary>
    public const int MaximumPerHour = 1000;

    private readonly int? _perHour;
    private readonly DateTimeOffset? _until;

    /// <summary>No limit.</summary>
    public static AccessLimit None { get; } = new();

    /// <summary>How many releases an hour, from 1 to <see cref="MaximumPerHour"/>, or null for no such limit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The number is outside that range.</exception>
    public int? PerHour
    {
        get => _perHour;
        init => _perHour = value is null or (>= 1 and <= MaximumPerHour)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), string.Create(CultureInfo.InvariantCulture, $"a limit allows 1 to {MaximumPerHour} an hour"));
    }

    /// <summary>When Automatic stops, in UTC to the second, or null for no such limit.</summary>
    public DateTimeOffset? Until
    {
        get => _until;
        init => _until = value is { } until
            ? new DateTimeOffset(until.UtcTicks - (until.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero)
            : null;
    }

    /// <summary>Whether this limits nothing.</summary>
    public bool IsNone => PerHour is null && Until is null;
}
