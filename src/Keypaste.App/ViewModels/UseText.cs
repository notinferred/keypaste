using System.Globalization;
using Keypaste.App.Globalization;
using Keypaste.App.Resources;
using Keypaste.Core;
using Keypaste.Core.Activity;

namespace Keypaste.App.ViewModels;

/// <summary>How the Secrets rows and the detail pane word when something was last used.</summary>
internal static class UseText
{
    /// <summary>"in use", "4m ago", "3h ago", "2d ago" or "never".</summary>
    /// <param name="use">The entry's use.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>The words.</returns>
    internal static string LastUsed(EntryUse use, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(use);

        return use.State == EntryUseState.InUse ? Strings.TimeInUse : use.LastUsed is { } at ? Ago(at, now) : Strings.TimeNever;
    }

    /// <summary>What the reveal button's tooltip says.</summary>
    internal static string FragileKeyFile(string name) => string.Format(CultureInfo.CurrentCulture, Strings.FragileKeyFileWarningValue, name);

    internal static string RevealFor(TimeSpan shownFor) => string.Format(CultureInfo.CurrentCulture, Strings.RevealForSeconds, shownFor.TotalSeconds);

    /// <summary>A time as how long ago it was, in its largest whole unit.</summary>
    /// <param name="at">The time.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>"just now", "4m ago", "3h ago" or "2d ago".</returns>
    internal static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        return elapsed switch
        {
            _ when elapsed < TimeSpan.FromMinutes(1) => Strings.TimeJustNow,
            _ when elapsed < TimeSpan.FromHours(1) => string.Format(CultureInfo.CurrentCulture, Strings.TimeMinutesAgoShort, (int)elapsed.TotalMinutes),
            _ when elapsed < TimeSpan.FromDays(1) => string.Format(CultureInfo.CurrentCulture, Strings.TimeHoursAgoShort, (int)elapsed.TotalHours),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.TimeDaysAgoShort, (int)elapsed.TotalDays),
        };
    }

    /// <summary>A date as the detail pane's metadata rows show it: <c>yyyy-MM-dd · 22 days ago</c>, local time.</summary>
    /// <param name="at">The time, or null.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>The row's text, or empty.</returns>
    internal static string Dated(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } when)
        {
            return string.Empty;
        }

        var days = (int)(now - when).TotalDays;
        var ago = days switch
        {
            <= 0 => Strings.DayToday,
            1 => Strings.DayOneAgo,
            _ => string.Format(CultureInfo.CurrentCulture, Strings.DaysAgo, days),
        };

        return $"{PersonTime.Date(when.ToLocalTime())} · {ago}";
    }

    /// <summary>A moment as the detail pane's expiry row shows it: <c>yyyy-MM-dd HH:mm</c>, local time.</summary>
    /// <param name="at">The time, or null.</param>
    /// <returns>The row's text, or empty.</returns>
    internal static string LocalMinute(DateTimeOffset? at) =>
        at is { } moment ? PersonTime.Moment(moment.ToLocalTime()) : string.Empty;

    /// <summary>The Agent access card's one line.</summary>
    /// <param name="access">What the card shows.</param>
    /// <param name="seeing">The attached agents that can name the entry without having asked yet.</param>
    /// <returns>
    /// "cursor", "ci-staging · token", "claude-code can see it · nothing released"
    /// or "None active": who, never when, which the card's Last used line says.
    /// </returns>
    internal static string Summary(EntryAgentAccess? access, IReadOnlyList<string> seeing)
    {
        ArgumentNullException.ThrowIfNull(seeing);

        if (access is null)
        {
            return "None active";
        }

        if (access.Clients.Count > 0)
        {
            return access.Clients[0].Client;
        }

        if (access.Waiting)
        {
            return "a request is waiting";
        }

        return seeing.Count > 0 ? $"{Listed(seeing)} can see it · nothing released" : "None active";
    }

    private static string Listed(IReadOnlyList<string> names) => names.Count == 1
        ? names[0]
        : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    /// <summary>How many lines the Agent access card lists under its summary.</summary>
    internal const int MaximumLines = 5;

    /// <summary>The Agent access card's lines under its summary: each client that received the entry.</summary>
    /// <param name="access">What agents did with the entry.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>Names, kinds and times, never a value; none when the summary already says all there is.</returns>
    internal static IReadOnlyList<string> Lines(EntryAgentAccess access, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (access.Clients.Count <= 1)
        {
            return [];
        }

        var clients = access.Clients.Select(client =>
            string.Create(CultureInfo.InvariantCulture, $"{EntryNameSanitizer.Sanitize(client.Client).Text} · {Ago(client.LastAt, now)} · {client.Releases}×"));

        return [.. clients.Take(MaximumLines)];
    }
}
