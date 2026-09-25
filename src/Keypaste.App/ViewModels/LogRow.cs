using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Audit;
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
internal sealed class LogRow
{
    internal const string You = "you";

    private const string _none = "—";

    private LogRow(AuditEntry source) => Source = source;

    /// <summary>The record this row was drawn from.</summary>
    internal AuditEntry Source { get; }

    /// <summary>Local time: HH:mm today, the date on an earlier day.</summary>
    internal string Time { get; private init; } = string.Empty;

    /// <summary>The full local timestamp, for the tooltip.</summary>
    internal string When { get; private init; } = string.Empty;

    internal string Actor { get; private init; } = string.Empty;

    internal string Action { get; private init; } = string.Empty;

    internal string Secrets { get; private init; } = string.Empty;

    internal string Where { get; private init; } = string.Empty;

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

    /// <summary>Whether this release was served under a reason no person read (THREATS.md T-12).</summary>
    internal bool ReasonUnread => Source.ReasonUnread;

    /// <summary>Whether the person did this themselves, rather than an agent or a token asking.</summary>
    internal bool ByYou { get; private init; }

    /// <summary>Whether a person answered this or did it themselves: approvals, refusals they gave, shares, bundles.</summary>
    internal bool AnsweredOrDoneByYou => ByYou || Is(Source.Method, "prompt");

    internal bool Denied => !Source.Granted;

    internal static LogRow From(AuditEntry entry, bool verified, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(clock);

        var byYou = Is(entry.Method, "share-created") || Is(entry.Method, "share-revoked") || Is(entry.Tool, "token bundle");
        var (secrets, where) = Place(entry);
        var (result, tone) = Outcome(entry);

        return new LogRow(entry)
        {
            Time = Clock(entry, clock),
            When = Stamp(entry, clock),
            Actor = byYou ? You : ActorOf(entry),
            Action = ActionOf(entry),
            Secrets = secrets,
            Where = where,
            Result = result,
            Tone = tone,
            Detail = DetailOf(entry),
            Verified = verified,
            ByYou = byYou,
        };
    }

    private static string ActorOf(AuditEntry entry)
    {
        if (Is(entry.Method, "token"))
        {
            return TokenAuditReason.TryName(entry.Reason, out var name) ? name : "token";
        }

        return entry.Client.Length > 0 ? entry.Client : "unknown client";
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
            "run" => entry.Granted ? "Injected" : "Requested",
            "token bundle" => "Bundled",
            "" => _none,
            var tool when tool.EndsWith("_credential", StringComparison.Ordinal) => "Requested",
            var tool => tool,
        };
    }

    private static (string Secrets, string Where) Place(AuditEntry entry)
    {
        if (entry.Entries.Count > 0)
        {
            var titles = entry.Entries.Select(Title).ToList();
            var groups = entry.Entries.Select(Group).Distinct(StringComparer.Ordinal).ToList();
            var secrets = titles.Count <= 3 ? string.Join(", ", titles) : $"{titles.Count} secrets";
            var where = groups.Count == 1 ? Readable(groups[0]) : $"{groups.Count} groups";

            return (secrets, where);
        }

        if (Is(entry.Tool, "list_entry_names"))
        {
            return ("names only", _none);
        }

        if (entry.Entry.Length == 0)
        {
            return (_none, _none);
        }

        // A run or a token names the set it asked about, a group rather than an entry.
        if (Is(entry.Tool, "run") || Is(entry.Tool, "token bundle"))
        {
            var sets = entry.Entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return (_none, sets.Length == 1 ? Readable(sets[0]) : $"{sets.Length} sets");
        }

        var title = Title(entry.Entry);
        var field = entry.Field.Length == 0 || Is(entry.Field, "password") ? string.Empty : $" · {entry.Field}";

        return (title + field, Readable(Group(entry.Entry)));
    }

    private static (string Result, LogTone Tone) Outcome(AuditEntry entry)
    {
        if (!entry.Granted)
        {
            return ("Denied", LogTone.Danger);
        }

        return entry.Method switch
        {
            "prompt" => (entry.GrantedSeconds switch
            {
                null => "Approved",
                0 => "Approved once",
                var seconds => $"Approved {Span(seconds.Value)}",
            }, LogTone.Ok),
            "policy" => ("By rule", LogTone.Ok),
            "token" => ("Token", LogTone.Info),
            "share-created" => ("Link", LogTone.Muted),
            "share-revoked" => ("Revoked", LogTone.Muted),
            _ => ("Granted", LogTone.Ok),
        };
    }

    private static string? DetailOf(AuditEntry entry)
    {
        var lines = new List<string>();

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

        if (entry.Name.Length > 0 && entry.Label.Length > 0)
        {
            lines.Add($"client {entry.Name}, label {entry.Label}");
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string Clock(AuditEntry entry, TimeProvider clock)
    {
        if (entry.At is not { } at)
        {
            return _none;
        }

        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).Date;

        return local.Date == today
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    private static string Stamp(AuditEntry entry, TimeProvider clock) =>
        entry.At is { } at
            ? TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : entry.Timestamp;

    private static string Span(int seconds) => seconds switch
    {
        >= 86_400 when seconds % 86_400 == 0 => $"{seconds / 86_400}d",
        >= 3_600 when seconds % 3_600 == 0 => $"{seconds / 3_600}h",
        >= 60 when seconds % 60 == 0 => $"{seconds / 60}m",
        _ => $"{seconds}s",
    };

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

    /// <summary>A group as a place: <c>env/acme-api/dev</c> reads <c>acme-api · dev</c>, a login group reads as itself.</summary>
    private static string Readable(string group)
    {
        if (group.Length == 0)
        {
            return _none;
        }

        var root = EnvConvention.RootGroup + "/";
        return group.StartsWith(root, StringComparison.Ordinal)
            ? group[root.Length..].Replace("/", " · ", StringComparison.Ordinal)
            : group;
    }

    private static bool Is(string text, string wanted) => string.Equals(text, wanted, StringComparison.Ordinal);
}
