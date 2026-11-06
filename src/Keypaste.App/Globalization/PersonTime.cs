using System.Globalization;

namespace Keypaste.App.Globalization;

/// <summary>A time as a person reads it, in the current culture's own patterns and calendar; with no culture to follow, or outside the range of that culture's calendar, as ISO 8601.</summary>
/// <remarks>Only for reading: a file, a hash, a sort and a value a person copies as data keep the time itself or its invariant form.</remarks>
internal static class PersonTime
{
    private const char _rightToLeftMark = '\u200F';

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>The date and the time to the minute.</summary>
    internal static string Moment(DateTimeOffset at) => Read(at.DateTime, "g", "yyyy-MM-dd HH:mm");

    /// <summary>The date and the time to the second.</summary>
    internal static string MomentWithSeconds(DateTimeOffset at) => Read(at.DateTime, "G", "yyyy-MM-dd HH:mm:ss");

    /// <summary>The date alone.</summary>
    internal static string Date(DateTimeOffset at) => Read(at.DateTime, "d", "yyyy-MM-dd");

    /// <summary>The time to the minute.</summary>
    internal static string Clock(DateTimeOffset at) => Read(at.DateTime, "t", "HH:mm");

    /// <summary>The time to the second.</summary>
    internal static string ClockWithSeconds(DateTimeOffset at) => Read(at.DateTime, "T", "HH:mm:ss");

    /// <summary>A day in words, with the year only where it is not this one or the culture always writes it.</summary>
    internal static string Day(DateTime day, DateTime today)
    {
        var withNoCulture = day.Year == today.Year ? "dddd, MMMM d" : "MMMM d, yyyy";

        return Read(day, "D", Culture.Name.Length == 0 ? withNoCulture : "yyyy-MM-dd");
    }

    private static string Read(DateTime at, string culturePattern, string isoPattern)
    {
        var culture = Culture;

        if (culture.Name.Length == 0 || !InCalendar(at, culture.DateTimeFormat.Calendar))
        {
            return at.ToString(isoPattern, CultureInfo.InvariantCulture);
        }

        var read = at.ToString(culturePattern, culture);

        // Inside left-to-right text the date's parts would scatter, its first and last numbers left out of its order; Avalonia draws an isolate's characters as missing glyphs.
        return culture.TextInfo.IsRightToLeft
            ? string.Concat(_rightToLeftMark.ToString(), read, _rightToLeftMark.ToString())
            : read;
    }

    private static bool InCalendar(DateTime at, Calendar calendar) =>
        at >= calendar.MinSupportedDateTime && at <= calendar.MaxSupportedDateTime;
}
