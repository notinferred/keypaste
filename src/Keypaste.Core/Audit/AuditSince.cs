using System.Globalization;

namespace Keypaste.Core.Audit;

/// <summary>Reads the moment a <c>--since</c> filter starts at.</summary>
/// <remarks>
/// Two forms, because the two questions people ask are different ones. <c>--since 2h</c> answers
/// "what has happened lately", which is what somebody watching an agent work wants; <c>--since
/// 2026-07-20</c> answers "what happened around then", which is what somebody reconstructing an
/// incident wants, and a relative span cannot express it without arithmetic they should not have to
/// do. Everything is UTC, in both directions, because that is what the log holds and a filter that
/// quietly shifted by a timezone would hide records at exactly the boundary somebody was looking at.
/// </remarks>
public static class AuditSince
{
    /// <summary>What to say when the text is not a moment.</summary>
    public const string Expected =
        "expected a span like 30m, 2h or 7d, or a UTC moment like 2026-07-20 or 2026-07-20T14:00:00Z";

    /// <summary>Reads one <c>--since</c> value.</summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="now">The moment a relative span is measured back from.</param>
    /// <param name="since">The moment the filter starts at, on success.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="true"/> when the text is a moment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static bool TryParse(string text, DateTimeOffset now, out DateTimeOffset since, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        since = default;

        if (TryRelative(text, now, out since))
        {
            error = string.Empty;
            return true;
        }

        if (DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out since))
        {
            error = string.Empty;
            return true;
        }

        error = $"'{EntryNameSanitizer.Sanitize(text, 40).Text}' is not a moment - {Expected}";
        return false;
    }

    /// <summary>Days beyond which a span is simply "everything".</summary>
    /// <remarks>
    /// Ten thousand years. Past this the arithmetic starts throwing rather than answering, and
    /// refusing a number that is merely large would be a worse answer than the one the user meant.
    /// </remarks>
    internal const long Forever = 3_650_000;

    private static bool TryRelative(string text, DateTimeOffset now, out DateTimeOffset since)
    {
        since = default;

        if (text.Length < 2
            || !long.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        double minutes;

        switch (text[^1])
        {
            case 'm':
                minutes = amount;
                break;

            case 'h':
                minutes = amount * 60d;
                break;

            case 'd':
                minutes = amount * 1440d;
                break;

            default:
                return false;
        }

        if (amount > Forever)
        {
            since = DateTimeOffset.MinValue;
            return true;
        }

        var span = TimeSpan.FromMinutes(minutes);
        var available = now - DateTimeOffset.MinValue;

        since = span >= available ? DateTimeOffset.MinValue : now - span;
        return true;
    }
}
