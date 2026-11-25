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

    /// <summary>"Changed" and the moment, as an item's row says it in its tooltip and to a screen reader.</summary>
    internal static string Changed(DateTimeOffset at) => string.Format(CultureInfo.CurrentCulture, Strings.ChangedValue, PersonTime.Moment(at.ToLocalTime()));

    /// <summary>Who wants what, as Agents' waiting row says it.</summary>
    internal static string Wants(string who, string what) => string.Format(CultureInfo.CurrentCulture, Strings.WantsValue, who, what);

    /// <summary>Where a tool's configuration file is, under the entry to paste into it.</summary>
    internal static string GoesIn(string path) => string.Format(CultureInfo.CurrentCulture, Strings.GoesInValue, path);

    /// <summary>An agent's rule for every kind of value, the rest of them, or the default's, in a word.</summary>
    internal static string RuleFor(RuleScope scope, string rule) => string.Format(
        CultureInfo.CurrentCulture,
        scope switch
        {
            RuleScope.EveryKind => Strings.RuleForEveryKind,
            RuleScope.TheRest => Strings.RuleForTheRest,
            _ => Strings.DefaultRuleValue,
        },
        rule);

    internal static string FragileKeyFile(string name) => string.Format(CultureInfo.CurrentCulture, Strings.FragileKeyFileWarningValue, name);

    /// <summary>How a vault derives its key now and how Strengthen would, as Security's details line names them.</summary>
    internal static string KeyDerivationNowAndAfter(VaultKeyDerivation now) =>
        string.Format(CultureInfo.CurrentCulture, Strings.KeyDerivationNowAndAfterValue, Described(now), Described(now.Strengthened));

    private static string Described(VaultKeyDerivation derivation) => derivation.Function == KeyDerivationFunction.AesKdf
        ? derivation.Name
        : string.Format(CultureInfo.CurrentCulture, Strings.KeyDerivationArgon2Value, derivation.Name, derivation.Memory / (1024 * 1024), derivation.Iterations);

    /// <summary>A Security finding's keys: how many, and the item whose notes hold them.</summary>
    internal static string KeysInNotes(int count, string item) => string.Format(CultureInfo.CurrentCulture, Strings.NKeysInTheNotesOfValue, count, item);

    internal static string InNotesOf(string item) => string.Format(CultureInfo.CurrentCulture, Strings.InTheNotesOfValue, item);

    internal static string AlsoOn(string items) => string.Format(CultureInfo.CurrentCulture, Strings.AlsoOnValue, items);

    internal static string RotatePassword(string item, string? host) => host is null
        ? string.Format(CultureInfo.CurrentCulture, Strings.RotateValuePassword, item)
        : string.Format(CultureInfo.CurrentCulture, Strings.RotateValuePasswordOnValue, item, host);

    internal static string Modified(string when) => string.Format(CultureInfo.CurrentCulture, Strings.ModifiedValue, when);

    internal static string CopyInto(string vault) => string.Format(CultureInfo.CurrentCulture, Strings.CopyIntoValue, vault);

    internal static string ChangedElsewhere(string vault) => string.Format(CultureInfo.CurrentCulture, Strings.ValueChangedElsewhere, vault);

    internal static string Expired(DateTimeOffset at) => string.Format(CultureInfo.CurrentCulture, Strings.ExpiredValue, PersonTime.Date(at.ToLocalTime()));

    internal static string Expires(DateTimeOffset at) => string.Format(CultureInfo.CurrentCulture, Strings.ExpiresValue, PersonTime.Date(at.ToLocalTime()));

    internal static string Marked(DateTimeOffset at) => string.Format(CultureInfo.CurrentCulture, Strings.MarkedValue, PersonTime.Date(at.ToLocalTime()));

    /// <summary>What moving keys into a stage does: the item joins it with these variables.</summary>
    internal static string JoinsStage(string item, string stage, string variables) => string.Format(CultureInfo.CurrentCulture, Strings.ValueJoinsValueWithValue, item, stage, variables);

    internal static string MovedIntoStage(string keys, string item, string stage) => string.Format(CultureInfo.CurrentCulture, Strings.MovedValueIntoValue, keys, item, stage);

    /// <summary>"From .env.staging", the From column's heading naming the item most variables come from.</summary>
    internal static string FromItem(string item) => string.Format(CultureInfo.CurrentCulture, Strings.FromValue, item);

    /// <summary>What adding an item with no variables to a stage does.</summary>
    internal static string NoVariablesToAdd(string item, string stage) => string.Format(CultureInfo.CurrentCulture, Strings.ValueHasNoVariablesToAddToValue, item, stage);

    internal static string AddsVariables(string item, string variables, string stage) => string.Format(CultureInfo.CurrentCulture, Strings.ValueAddsValueToValue, item, variables, stage);

    /// <summary>A project tag whose stage is cased wrong: ".env's tag Prod isn't a stage name."</summary>
    internal static string TagIsNotAStage(string item, string stage) => string.Format(CultureInfo.CurrentCulture, Strings.ValuesTagValueIsntAStageName, item, stage);

    internal static string ChangeTo(string stage) => string.Format(CultureInfo.CurrentCulture, Strings.ChangeToValue, stage);

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

        return $"{PersonTime.Date(when.ToLocalTime())} · {DaysAgo(when, now)}";
    }

    /// <summary>How many whole days ago a time was: "today", "1 day ago" or "22 days ago".</summary>
    internal static string DaysAgo(DateTimeOffset at, DateTimeOffset now) => (int)(now - at).TotalDays switch
    {
        <= 0 => Strings.DayToday,
        1 => Strings.DayOneAgo,
        var days => string.Format(CultureInfo.CurrentCulture, Strings.DaysAgo, days),
    };

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
    /// or "None active": who, never when, which the lines say.
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

    /// <summary>The Agent access row's lines: each client that received the entry, when last and how often.</summary>
    /// <param name="access">What agents did with the entry.</param>
    /// <param name="now">The time to measure from.</param>
    /// <returns>Names, times and counts, never a value; none when no client received it, where the summary speaks.</returns>
    internal static IReadOnlyList<string> Lines(EntryAgentAccess access, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (access.Clients.Count == 0)
        {
            return [];
        }

        var clients = access.Clients.Select(client =>
            string.Create(CultureInfo.InvariantCulture, $"{EntryNameSanitizer.Sanitize(client.Client).Text} · {Ago(client.LastAt, now)} · {client.Releases}×"));

        return [.. clients.Take(MaximumLines)];
    }
}
