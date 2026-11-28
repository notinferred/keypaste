using System.Globalization;
using Keypaste.App.Globalization;
using Keypaste.App.Resources;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;
using Keypaste.Core.Tokens;

namespace Keypaste.App.ViewModels;

/// <summary>How a row's result is coloured.</summary>
internal enum LogTone
{
    Ok,
    Danger,
    Info,
    Muted,
}

/// <summary>
/// One audit record as the Activity table shows it: when, who, what they did, which secrets, where, and what came of it.
/// </summary>
/// <remarks>
/// Every string is built from an <see cref="AuditEntry"/>, which the core's reader has already sanitized, so nothing an
/// agent wrote reaches the screen on other terms than it reaches <c>keypaste log</c>. No record holds a value.
/// </remarks>
internal sealed record LogRow
{
    internal const string You = "you";

    /// <summary>What a row the hash chain does not vouch for is told, on its tooltip and in the detail.</summary>
    internal static string UnverifiedWords => Strings.KeypasteCantVouchForThisRecord;

    private const string _namesOnly = "names only";

    private const string _none = "—";

    /// <summary>A cell's text, or null where it says it holds nothing, which sorts as blank.</summary>
    internal static string? UnlessNone(string text) => text == _none ? null : text;

    private LogRow(AuditEntry source) => Source = source;

    /// <summary>The record this row was drawn from.</summary>
    internal AuditEntry Source { get; }

    /// <summary>Local time of day, to the second; the day is <see cref="Day"/>'s divider.</summary>
    internal string Time { get; private init; } = string.Empty;

    /// <summary>The local date the record was written on, or null when it has no timestamp.</summary>
    internal DateTime? Date { get; private init; }

    /// <summary>The divider drawn above this row when it starts a day, or null; the screen sets it per filter.</summary>
    internal string? Day { get; init; }

    /// <summary>What a screen reader says for the row: its columns, none of which holds a value.</summary>
    internal string RowName => $"{When}, {Actor}, {Action}, {Secrets}, {WhereInFull}, {Result}{(Edited ? $", {Strings.Edited}: {EditedWords}" : Unverified ? $", {UnverifiedWords}" : string.Empty)}{(ReasonUnread ? $", {Strings.ServedFromAnEarlierApprovalUnderAXde6ddd}" : string.Empty)}";

    internal bool StartsDay => Day is not null;

    /// <summary>The full local timestamp, for the tooltip.</summary>
    internal string When { get; private init; } = string.Empty;

    internal string Actor { get; private init; } = string.Empty;

    internal string Action { get; private init; } = string.Empty;

    internal string Secrets { get; private init; } = string.Empty;

    /// <summary>Where the values live as the cell shows it: a group's last part, a set, a recipient.</summary>
    internal string Where { get; private init; } = string.Empty;

    /// <summary>Where the values live in full, the cell's tooltip.</summary>
    internal string WhereInFull { get; private init; } = string.Empty;

    /// <summary>Whether the Values cell holds a note about the values, drawn in the interface face, rather than their names.</summary>
    internal bool NamesOnly => Secrets == _namesOnly;

    /// <summary>The Values cell's tooltip where the table has no Where column.</summary>
    internal string SecretsAndWhere => string.Join(" · ", new[] { Secrets, WhereInFull }.Where(part => part.Length > 0));

    /// <summary>What the line was about: the values and where they live, or empty when it names neither.</summary>
    internal string Subject => string.Join(" · ", new[] { UnlessNone(Secrets), UnlessNone(WhereInFull) }.OfType<string>());

    internal bool HasSubject => Subject.Length > 0;

    /// <summary>The action as the rest of the sentence after its actor, or empty when the record names none.</summary>
    internal string ActionWords => Action == _none ? string.Empty : char.ToLowerInvariant(Action[0]) + Action[1..];

    internal string Result { get; private init; } = string.Empty;

    internal LogTone Tone { get; private init; }

    internal bool IsOk => Tone == LogTone.Ok;

    internal bool IsDanger => Tone == LogTone.Danger;

    internal bool IsInfo => Tone == LogTone.Info;

    internal bool IsMuted => Tone == LogTone.Muted;

    /// <summary>keypaste's own account of the record, with the command and client it names.</summary>
    internal string? Detail { get; private init; }

    /// <summary>Whether the hash chain vouches for this record.</summary>
    internal bool Verified { get; private init; }

    internal bool Unverified => !Verified;

    /// <summary>Whether the chain breaks at this record: changed, inserted or written by something else since keypaste wrote it.</summary>
    internal bool Edited { get; private init; }

    /// <summary>A record the chain cannot check without its breaking there, such as one a newer keypaste wrote.</summary>
    internal bool Unchecked => Unverified && !Edited;

    /// <summary>What the Edited mark says, with the result the record states.</summary>
    internal string EditedWords => $"This record was changed or added after keypaste wrote it. It says: {Result}.";

    /// <summary>Whether this release was served under a reason no person read (THREATS.md T-12).</summary>
    internal bool ReasonUnread => Source.ReasonUnread;

    /// <summary>Whether the person did this themselves, in keypaste or in their browser, rather than an agent or a token asking.</summary>
    internal bool ByYou { get; private init; }

    /// <summary>Whether the person's paired browser made the request, through keypaste's extension.</summary>
    internal bool ByBrowser => Source.ByBrowser;

    /// <summary>Whether this is the browser asking which logins match a page, which releases no password.</summary>
    internal bool IsLookup => Source.IsBrowserLookup;

    internal bool Denied => !Source.Granted;

    /// <summary>What a screen reader says for the row in the list.</summary>
    public override string ToString() => RowName;

    internal static LogRow From(AuditEntry entry, bool verified, TimeProvider clock, bool edited = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(clock);

        // Every browser line is the person's: in their browser, a day of its lookups folded, or a pairing they removed in keypaste.
        var byYou = entry.ByBrowser || Is(entry.Tool, AuditTools.Browser) || Is(entry.Method, "share-created") || Is(entry.Method, "share-revoked") || Is(entry.Tool, "token bundle");
        var (secrets, where) = Place(entry);
        var (result, tone) = Outcome(entry);
        var local = entry.At is { } at ? TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone) : (DateTimeOffset?)null;

        if (Is(entry.Method, "share-created") && ShareAuditReason.TryRead(entry.Reason, out var recipient, out var expires))
        {
            where = recipient;
            result = entry.At is { } created && Lifetime(expires - created) is { } lifetime ? $"{Strings.Shared} · {lifetime}" : Strings.Shared;
        }

        return new LogRow(entry)
        {
            Time = local is { } shown ? PersonTime.ClockWithSeconds(shown) : _none,
            Date = local?.Date,
            When = local?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? entry.Timestamp,
            Actor = entry.ByBrowser ? InBrowser(entry) : byYou ? You : ActorOf(entry),
            Action = ActionOf(entry),
            Secrets = secrets,
            Where = Last(where),
            WhereInFull = where,
            Result = result,
            Tone = tone,
            Detail = DetailOf(entry, verified, clock.LocalTimeZone),
            Verified = verified,
            Edited = edited,
            ByYou = byYou,
        };
    }

    /// <summary>The person in their browser, named by the pairing a browser's line carries as its label: <c>you, in Chrome on studio</c>.</summary>
    private static string InBrowser(AuditEntry entry) => entry.Label.Length > 0 ? $"{You}, in {entry.Label}" : $"{You}, in your browser";

    private static string ActorOf(AuditEntry entry)
    {
        if (Is(entry.Method, "token"))
        {
            return TokenAuditReason.TryName(entry.Reason, out var name) ? name : "token";
        }

        if (Is(entry.Tool, AuditTools.Removed))
        {
            return "keypaste";
        }

        return entry.Client.Length > 0 ? entry.Client : "unknown agent";
    }

    private static string ActionOf(AuditEntry entry)
    {
        if (Is(entry.Method, "share-created"))
        {
            return "Shared";
        }

        if (Is(entry.Method, "share-revoked"))
        {
            return "Revoked link";
        }

        return entry.Tool switch
        {
            "list_entry_names" => "Listed names",
            AuditTools.Summary => "Looked up, in one line for the day",
            AuditTools.Removed => "Removed older days",
            "run" => entry.Granted ? "Ran with" : "Requested",
            "access_change" => entry.Granted ? "Changed settings" : "Asked to change settings",
            "terminal_edit" => entry.Granted ? "Changed from a terminal" : "Asked to change from a terminal",
            "browser" => entry.Field switch
            {
                "check" => "Checked its pairing",
                "remove" => "Removed a pairing",
                "fill" => entry.Granted ? "Filled a login" : "Asked to fill a login",
                // Written by a keypaste that remembered the logins filled on each site.
                "unallow" => "Asks again before filling",
                "list" => "Listed logins by name",
                _ => entry.Granted ? "Paired a browser" : "Asked to pair",
            },
            "vault_merge" => "Merged another program's save",
            "token bundle" => "Made a bundle",
            "" => _none,
            var tool when tool.EndsWith("_credential", StringComparison.Ordinal) => "Requested",
            var tool => tool,
        };
    }

    private static (string Secrets, string Where) Place(AuditEntry entry)
    {
        // A run or a token names the sets it asked about, which are labels rather than groups.
        string[] sets = Is(entry.Tool, "run") || Is(entry.Tool, "token bundle")
            ? entry.Entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        var set = sets.Length switch
        {
            0 => null,
            1 => SetLabel(sets[0]),
            _ => $"{sets.Length} sets",
        };

        if (AuditText.Folded(entry) is { Length: > 0 } folded)
        {
            return (folded, _none);
        }

        if (entry.Entries.Count > 0)
        {
            var titles = entry.Entries.Select(Title).ToList();
            var groups = entry.Entries.Select(Group).Distinct(StringComparer.Ordinal).ToList();
            var secrets = titles.Count <= 3 ? string.Join(", ", titles) : $"{titles.Count} secrets";
            var where = set ?? (groups.Count == 1 ? Readable(groups[0]) : $"{groups.Count} groups");

            return (secrets, where);
        }

        if (Is(entry.Tool, "list_entry_names"))
        {
            return (_namesOnly, _none);
        }

        if (set is not null)
        {
            return (_none, set);
        }

        if (entry.Entry.Length == 0)
        {
            return (_none, _none);
        }

        var title = Title(entry.Entry);
        var field = entry.Field.Length == 0 || Is(entry.Field, "password") ? string.Empty : $" · {entry.Field}";

        return (title + field, Readable(Group(entry.Entry)));
    }

    private static (string Result, LogTone Tone) Outcome(AuditEntry entry)
    {
        if (entry.Summary is not null || entry.Removal is not null)
        {
            return (entry.Summary is not null ? "Summary" : "Removed", LogTone.Muted);
        }

        if (!entry.Granted)
        {
            // Nobody answered, so nobody refused: still a refusal, but not one a person gave. A terminal's
            // edit that asked for what the vault already held was refused by nobody either.
            return entry.Method switch
            {
                "timed-out" => ("No answer", LogTone.Muted),
                "unchanged" => ("Unchanged", LogTone.Muted),
                _ => ("Denied", LogTone.Danger),
            };
        }

        // The prompt's words: a person allowed it once or for a while, an Always allow row allowed it, or a rule did.
        return entry.Method switch
        {
            "prompt" => (entry.GrantedSeconds is > 0 and var seconds ? $"Allowed for {Lifetime(TimeSpan.FromSeconds(seconds))}" : Strings.AllowedOnce, LogTone.Ok),
            "always-allowed" => (Strings.AlwaysAllowed, LogTone.Ok),
            // policy: released by a policy.toml rule, in a line written before agents had modes.
            "automatic" or "automatic-commands" or "policy" or "exposure" or "allowed" => (Strings.AllowedByRule, LogTone.Ok),
            "narrowing" => ("Narrowed", LogTone.Muted),
            "picked" => ("You picked it in the page", LogTone.Ok),
            "pressed" => (entry.Reason.EndsWith(RequestRecords.OpenTerminalPressed, StringComparison.Ordinal) ? "You pressed Open terminal" : "You pressed Run", LogTone.Ok),
            "merged" => ("Merged", LogTone.Muted),
            "token" => (Strings.TokenResult, LogTone.Info),
            "share-created" => (Strings.Shared, LogTone.Muted),
            "share-revoked" => ("Revoked", LogTone.Muted),
            "removed" => ("Removed", LogTone.Muted),
            _ => (Strings.Allowed, LogTone.Ok),
        };
    }

    private static string? DetailOf(AuditEntry entry, bool verified, TimeZoneInfo zone)
    {
        var lines = new List<string>();

        if (!verified)
        {
            lines.Add(UnverifiedWords);
        }

        if (entry.Reason.Length > 0)
        {
            lines.Add(entry.Reason);
        }

        if (entry.Command.Length > 0)
        {
            lines.Add($"command: {entry.Command}");
        }

        if (entry.Entries.Count > 3)
        {
            lines.Add(string.Join(", ", entry.Entries));
        }

        // A browser's line names the extension's build as its client and the pairing as its label.
        if (entry.Name.Length > 0 && entry.Label.Length > 0)
        {
            lines.Add(entry.ByBrowser || Is(entry.Tool, AuditTools.Browser) ? $"extension {entry.Name}, pairing {entry.Label}" : $"agent {entry.Name}, label {entry.Label}");
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    /// <summary>A lifetime to the minute, in words: <c>24 hours</c>, <c>7 days</c>, <c>45 minutes</c>.</summary>
    private static string? Lifetime(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);

        return minutes switch
        {
            <= 0 => span.TotalSeconds >= 1 ? Count((int)Math.Round(span.TotalSeconds), "second") : null,
            < 60 => Count(minutes, "minute"),
            _ when minutes % 1_440 == 0 && minutes >= 2_880 => Count(minutes / 1_440, "day"),
            _ => Count((int)Math.Round(minutes / 60.0), "hour"),
        };
    }

    private static string Count(int n, string unit) => Invariant($"{n} {unit}{(n == 1 ? string.Empty : "s")}");

    private static string Title(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string Group(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>A group as a place: itself, or a dash at the root.</summary>
    private static string Readable(string group) => group.Length == 0 ? _none : group;

    /// <summary>A group path's last part, as the cell shows it; anything else as it is.</summary>
    private static string Last(string where) => where.Contains('/', StringComparison.Ordinal) ? Title(where) : where;

    /// <summary>A set as a place: <c>env/acme-api/dev</c> reads <c>acme-api · dev</c>.</summary>
    private static string SetLabel(string set)
    {
        var root = EnvConvention.RootGroup + "/";
        return set.StartsWith(root, StringComparison.Ordinal)
            ? set[root.Length..].Replace("/", " · ", StringComparison.Ordinal)
            : set;
    }

    private static bool Is(string text, string wanted) => string.Equals(text, wanted, StringComparison.Ordinal);
}
