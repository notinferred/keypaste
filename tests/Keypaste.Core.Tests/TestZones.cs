namespace Keypaste.Core.Tests;

/// <summary>Time zones a test can name on any runner, built here rather than read from the system's database.</summary>
internal static class TestZones
{
    /// <summary>Two hours east of UTC all year.</summary>
    internal static TimeZoneInfo PlusTwo { get; } = Fixed("keypaste test +02:00", TimeSpan.FromHours(2));

    /// <summary>Five hours west of UTC all year.</summary>
    internal static TimeZoneInfo MinusFive { get; } = Fixed("keypaste test -05:00", TimeSpan.FromHours(-5));

    /// <summary>
    /// Central Europe's rules: +01:00, and +02:00 from 02:00 on the last Sunday of March to 03:00 on
    /// the last Sunday of October, so in 2026 the clock skips 2026-03-29 02:00 to 03:00 and shows
    /// 2026-10-25 02:00 to 03:00 twice.
    /// </summary>
    internal static TimeZoneInfo Berlin { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "keypaste test Berlin",
        TimeSpan.FromHours(1),
        "keypaste test Berlin",
        "keypaste test Berlin",
        "keypaste test Berlin summer",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    private static TimeZoneInfo Fixed(string name, TimeSpan offset) => TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
}
