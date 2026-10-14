using System.Globalization;

namespace Keypaste.Core.Agents;

/// <summary>
/// When Automatic ends, as a person or an agent writes it and a person reads it: on this computer's
/// clock, while the pipe, the vault's record and the audit log hold it in UTC.
/// </summary>
/// <remarks>
/// A date ends at the close of that day on this computer. A time with no offset is on this computer's
/// clock: one the clock shows twice, as it goes back, is the earlier, which ends Automatic sooner, and
/// one it skips, as it goes forward, is refused. A time with <c>Z</c> or an offset such as
/// <c>+02:00</c> is that instant wherever this computer is.
/// </remarks>
public static class UntilTime
{
    /// <summary>What a time to end Automatic may be, completing a sentence that begins with its name.</summary>
    public const string Rule =
        "is a date such as 2026-12-31, which ends at the close of that day on this computer, or a time such as 2026-12-31 18:00, on this computer's clock unless it ends in Z or an offset such as +02:00";

    private const string _recordedFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string _dateFormat = "yyyy-MM-dd";
    private const string _shownFormat = "yyyy-MM-dd HH:mm";
    private const string _shownToTheSecondFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] _timeFormats =
        ["yyyy-MM-dd'T'H:mm", "yyyy-MM-dd'T'H:mm:ss", "yyyy-MM-dd H:mm", "yyyy-MM-dd H:mm:ss"];

    /// <summary>The last second of a day, when a date given alone ends.</summary>
    public static TimeSpan CloseOfDay { get; } = new(23, 59, 59);

    /// <summary>A time as the pipe and the record hold it: UTC, to the second.</summary>
    /// <param name="until">The time.</param>
    /// <returns>Its words, such as <c>2026-12-31T21:59:59Z</c>.</returns>
    public static string Recorded(DateTimeOffset until) =>
        until.UtcDateTime.ToString(_recordedFormat, CultureInfo.InvariantCulture);

    /// <summary>Reads a time as the pipe and the record hold it, and nothing else.</summary>
    /// <param name="text">The words.</param>
    /// <param name="until">The time, when they are one.</param>
    /// <returns>Whether they are.</returns>
    public static bool TryParseRecorded(string text, out DateTimeOffset until) =>
        DateTimeOffset.TryParseExact(
            text,
            _recordedFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out until);

    /// <summary>A time as a person reads it: on this computer's clock, with that clock's offset from UTC then.</summary>
    /// <param name="until">The time.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <returns>
    /// Its words, such as <c>2026-12-31 23:59 +02:00</c>, with seconds where they are not zero, such as
    /// <c>2026-12-31 18:00:30 +02:00</c>, so an end is never shown earlier than it is; the close of a day,
    /// which a date given alone means, reads as its minute.
    /// </returns>
    public static string Shown(DateTimeOffset until, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var here = TimeZoneInfo.ConvertTime(until, zone);
        var format = here.Second == 0 || here.TimeOfDay == CloseOfDay ? _shownFormat : _shownToTheSecondFormat;

        return here.ToString(format, CultureInfo.InvariantCulture) + " " + here.ToString("zzz", CultureInfo.InvariantCulture);
    }

    /// <summary>Words naming ends as keypaste records them, <c>until</c> and a time in UTC, with each shown on this computer's clock instead.</summary>
    /// <param name="text">keypaste's own words, such as an audit line's reason.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <returns>The words, every <c>until 2026-12-31T21:59:59Z</c> in them read as <see cref="Shown"/> writes it.</returns>
    public static string OnThisClock(string text, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zone);

        const string until = "until ";
        var recordedLength = Recorded(DateTimeOffset.UnixEpoch).Length;
        var shown = new StringBuilder(text.Length);
        var at = 0;

        while (text.IndexOf(until, at, StringComparison.Ordinal) is var found and >= 0)
        {
            var time = found + until.Length;
            shown.Append(text, at, time - at);
            at = time;

            if (time + recordedLength <= text.Length && TryParseRecorded(text.Substring(time, recordedLength), out var end))
            {
                shown.Append(Shown(end, zone));
                at = time + recordedLength;
            }
        }

        return shown.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>Whether a time is the close of a day on this computer's clock, which a date given alone means.</summary>
    /// <param name="until">The time.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsCloseOfDay(DateTimeOffset until, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return TimeZoneInfo.ConvertTime(until, zone).TimeOfDay == CloseOfDay;
    }

    /// <summary>Reads a time a person or an agent gave, on this computer's clock unless it names its offset.</summary>
    /// <param name="text">The words.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <param name="now">The time now, which it must come after.</param>
    /// <param name="until">The time, when the words are one still to come.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether the words are a time still to come.</returns>
    public static bool TryRead(string text, TimeZoneInfo zone, DateTimeOffset now, out DateTimeOffset until, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zone);

        until = default;
        error = $"until {Rule}";

        if (!TrySplitOffset(text.Trim(), out var clock, out var offset))
        {
            return false;
        }

        if (DateTime.TryParseExact(clock, _dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            // A date ends at the close of that day here; an offset would make it some other day's.
            return offset is null && InRange(day) && TryHere(day + CloseOfDay, zone, now, out until, out error);
        }

        if (!DateTime.TryParseExact(clock, _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || !InRange(time))
        {
            return false;
        }

        return offset is { } given
            ? TryComing(new DateTimeOffset(time, given), zone, now, out until, out error)
            : TryHere(time, zone, now, out until, out error);
    }

    /// <summary>
    /// Reads <c>--until</c>'s words, or the panel's, into the words the pipe carries: a time a person
    /// gave becomes UTC here, before it is sent, and <see cref="AccessEdit.None"/> and nothing pass as they are.
    /// </summary>
    /// <param name="text">The words, or null.</param>
    /// <param name="clock">This computer's clock and time zone.</param>
    /// <param name="recorded">The words to send, or null when none were given.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether the words can be sent.</returns>
    public static bool TryRecord(string? text, TimeProvider clock, out string? recorded, out string error)
    {
        ArgumentNullException.ThrowIfNull(clock);

        recorded = text;
        error = string.Empty;

        if (text is null || string.Equals(text, AccessEdit.None, StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryRead(text, clock.LocalTimeZone, clock.GetUtcNow(), out var until, out error))
        {
            recorded = null;
            return false;
        }

        recorded = Recorded(until);
        return true;
    }

    /// <summary>Splits a trailing <c>Z</c> or <c>±HH:mm</c>, with or without a space before it, from the rest.</summary>
    private static bool TrySplitOffset(string text, out string clock, out TimeSpan? offset)
    {
        clock = text;
        offset = null;

        if (text.EndsWith('Z') || text.EndsWith('z'))
        {
            clock = text[..^1].TrimEnd();
            offset = TimeSpan.Zero;
            return true;
        }

        if (text.Length < 6 || text[^6] is not ('+' or '-') || text[^3] != ':')
        {
            return true;
        }

        if (!int.TryParse(text.AsSpan(text.Length - 5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(text.AsSpan(text.Length - 2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours > 14
            || minutes > 59
            || (hours == 14 && minutes > 0))
        {
            return false;
        }

        var span = new TimeSpan(hours, minutes, 0);
        clock = text[..^6].TrimEnd();
        offset = text[^6] == '-' ? -span : span;
        return true;
    }

    // Far enough from DateTime's ends that no offset or added day leaves its range.
    private static bool InRange(DateTime time) => time.Year is > 1 and < 9999;

    /// <summary>A time on this computer's clock as the instant it names.</summary>
    private static bool TryHere(DateTime time, TimeZoneInfo zone, DateTimeOffset now, out DateTimeOffset until, out string error)
    {
        if (zone.IsInvalidTime(time))
        {
            until = default;
            error = $"until {time.ToString(_shownFormat, CultureInfo.InvariantCulture)} never happens on this computer's clock, which skips it as it goes forward; give a time after the change";
            return false;
        }

        var offset = zone.IsAmbiguousTime(time) ? zone.GetAmbiguousTimeOffsets(time).Max() : zone.GetUtcOffset(time);
        return TryComing(new DateTimeOffset(time, offset), zone, now, out until, out error);
    }

    private static bool TryComing(DateTimeOffset time, TimeZoneInfo zone, DateTimeOffset now, out DateTimeOffset until, out string error)
    {
        until = time.ToUniversalTime();

        if (until <= now)
        {
            error = $"until {Shown(until, zone)} has already passed on this computer's clock";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
