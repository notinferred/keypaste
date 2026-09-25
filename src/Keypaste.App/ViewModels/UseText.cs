using System.Globalization;
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

        return use.State == EntryUseState.InUse ? "in use" : use.LastUsed is { } at ? Ago(at, now) : "never";
    }

    /// <summary>A time as how long ago it was, in its largest whole unit.</summary>
    /// <param name="at">The time.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>"just now", "4m ago", "3h ago" or "2d ago".</returns>
    internal static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        return elapsed switch
        {
            _ when elapsed < TimeSpan.FromMinutes(1) => "just now",
            _ when elapsed < TimeSpan.FromHours(1) => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m ago"),
            _ when elapsed < TimeSpan.FromDays(1) => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h ago"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalDays}d ago"),
        };
    }

    /// <summary>A remaining time in its largest whole unit, rounded up so a live grant never reads as over.</summary>
    /// <param name="seconds">Seconds left.</param>
    /// <returns>"42m", "2h" or "30s".</returns>
    internal static string Left(int seconds) => seconds switch
    {
        >= 3600 => string.Create(CultureInfo.InvariantCulture, $"{(seconds + 3599) / 3600}h"),
        >= 60 => string.Create(CultureInfo.InvariantCulture, $"{(seconds + 59) / 60}m"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{seconds}s"),
    };

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
            <= 0 => "today",
            1 => "1 day ago",
            _ => string.Create(CultureInfo.InvariantCulture, $"{days} days ago"),
        };

        return $"{when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} · {ago}";
    }

    /// <summary>The Agent access card's one line.</summary>
    /// <param name="access">What the card shows.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>"claude-code · grant, 42m left", "cursor · 1h ago", "ci-staging · token" or "None active".</returns>
    internal static string Summary(EntryAgentAccess? access, DateTimeOffset now)
    {
        if (access is null)
        {
            return "None active";
        }

        if (access.Grants.Count > 0)
        {
            var grant = access.Grants.MinBy(grant => grant.SecondsLeft)!;
            return $"{grant.Client} · grant, {Left(grant.SecondsLeft)} left";
        }

        if (access.Clients.Count > 0)
        {
            var latest = access.Clients[0];
            return latest.Client.EndsWith(" · token", StringComparison.Ordinal)
                ? latest.Client
                : $"{latest.Client} · {Ago(latest.LastAt, now)}";
        }

        return access.Waiting ? "a request is waiting" : "None active";
    }

    /// <summary>How many lines the Agent access card lists under its summary.</summary>
    internal const int MaximumLines = 5;

    /// <summary>The Agent access card's lines under its summary: each grant in force, then each client that received the entry.</summary>
    /// <param name="access">What agents did with the entry.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>Names, kinds and times, never a value; none when the summary already says all there is.</returns>
    internal static IReadOnlyList<string> Lines(EntryAgentAccess access, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (access.Grants.Count + access.Clients.Count <= 1)
        {
            return [];
        }

        var grants = access.Grants.Select(grant =>
            $"{EntryNameSanitizer.Sanitize(grant.Client).Text} · {grant.Kind} grant, {Left(grant.SecondsLeft)} left");
        var clients = access.Clients.Select(client =>
            string.Create(CultureInfo.InvariantCulture, $"{EntryNameSanitizer.Sanitize(client.Client).Text} · {Ago(client.LastAt, now)} · {client.Releases}×"));

        return [.. grants.Concat(clients).Take(MaximumLines)];
    }
}
