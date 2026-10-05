using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Audit;

/// <summary>One audit record, reduced to what a person reads.</summary>
/// <remarks>
/// Every text field here has already been through <see cref="EntryNameSanitizer"/>. That is not
/// belt-and-braces: <c>entry</c> and <c>reason_excerpt</c> are written by the agent and the client
/// name is asserted by whatever connected, so this record is the last thing between text an attacker
/// chose and a terminal (THREATS.md T-1 and T-2). Sanitizing in the reader rather than in each
/// renderer means a second front end cannot forget.
/// </remarks>
public sealed record AuditEntry
{
    /// <summary>The 1-based physical line the record came from.</summary>
    public required int Line { get; init; }

    /// <summary>When it happened, or null when the record does not say in a form that parses.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>The timestamp as written.</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary>The operator's label if there is one, otherwise the name the client asserted.</summary>
    public string Client { get; init; } = string.Empty;

    /// <summary>The label the operator configured, or empty.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The name the client asserted, or empty. Unauthenticated.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Which tool was called.</summary>
    public string Tool { get; init; } = string.Empty;

    /// <summary>The entry the agent named, or empty for a call that names none.</summary>
    public string Entry { get; init; } = string.Empty;

    /// <summary>The field the agent asked for, or empty.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary><c>granted</c> or <c>denied</c>.</summary>
    public string Decision { get; init; } = string.Empty;

    /// <summary>How the decision was reached.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>keypaste's own explanation. Trusted text, unlike the agent's reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The owner's session that answered, or empty when none did.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>The hash of the agent's stated reason, or empty.</summary>
    public string ReasonSha256 { get; init; } = string.Empty;

    /// <summary>How long a release stays granted, zero for "allow once", or null when the line does not say.</summary>
    public int? GrantedSeconds { get; init; }

    /// <summary>Whether this release was served under a reason no person ever read.</summary>
    /// <remarks>
    /// True when a grant was reused from the cache under a reason that differs from the one shown to
    /// the person who approved it. This is THREATS.md T-12, and it is the whole reason the record
    /// carries a hash of the reason as well as an excerpt of it.
    /// </remarks>
    public bool ReasonUnread { get; init; }

    /// <summary>The vault the request named, as its identity key, or empty on a line written before lines named one.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>Each entry a run or token line released or asked about, sanitized; empty otherwise.</summary>
    public IReadOnlyList<string> Entries { get; init; } = [];

    /// <summary>The command a run line names, sanitized, or empty.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Whether this record was granted.</summary>
    public bool Granted => string.Equals(Decision, "granted", StringComparison.Ordinal);
}

/// <summary>Where an incremental read of the log stopped.</summary>
/// <param name="Offset">The byte just past the last complete line read.</param>
/// <param name="Line">The number of physical lines read so far.</param>
public readonly record struct AuditPosition(long Offset, int Line)
{
    /// <summary>The start of the file.</summary>
    public static AuditPosition Start { get; } = new(0, 0);
}

/// <summary>Turns the audit log into records a person can be shown.</summary>
/// <remarks>
/// <para>
/// This is the only place in keypaste that parses an audit line as JSON, and it is deliberately not
/// on the path that decides whether the log is intact — <see cref="AuditChainVerifier"/> works on
/// bytes. A parser difference must never be able to change a verdict; here the worst it can do is
/// drop a line from a table, which is visible and is counted.
/// </para>
/// <para>
/// It opens <see cref="FileShare.ReadWrite"/>, because a bridge may hold the log open for
/// writing and on Windows any narrower share mode fails outright against it.
/// </para>
/// </remarks>
public static class AuditReader
{
    /// <summary>The longest text kept for any one field.</summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> because the naming rule in <c>.editorconfig</c>
    /// applies <c>_camelCase</c> to every private field, constants included.
    /// </remarks>
    internal const int FieldLength = 200;

    /// <summary>Reads every record in a log.</summary>
    /// <param name="path">The log.</param>
    /// <param name="entries">The records, in file order.</param>
    /// <param name="unreadable">How many lines were not records this version understands.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when the file could not be read at all.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static bool TryRead(
        string path,
        out IReadOnlyList<AuditEntry> entries,
        out int unreadable,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(path);

        entries = [];
        unreadable = 0;

        var read = new List<AuditEntry>();
        var skipped = 0;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var number = 0;
            while (reader.ReadLine() is { } line)
            {
                number++;

                if (line.Length == 0)
                {
                    continue;
                }

                if (Parse(line, number) is { } entry)
                {
                    read.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }

        MarkUnreadReasons(read);

        entries = read;
        unreadable = skipped;
        error = string.Empty;
        return true;
    }

    /// <summary>Reads the complete lines appended since an earlier read.</summary>
    /// <param name="path">The log.</param>
    /// <param name="from">Where the earlier read stopped, or <see cref="AuditPosition.Start"/>.</param>
    /// <param name="entries">The records in those lines, in file order.</param>
    /// <param name="next">Where this read stopped, to pass to the next one.</param>
    /// <param name="unreadable">How many of those lines were not records this version understands.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when the file could not be read at all; a missing file reads as empty.</returns>
    /// <remarks>
    /// A tail with no newline yet is left for the next read, because it may be a record still being
    /// written. A file shorter than <paramref name="from"/> was replaced, so it is read again from its
    /// start, which a caller sees as <paramref name="next"/> numbering lines from one again.
    /// <see cref="AuditEntry.ReasonUnread"/> is not computed here: it needs the whole file.
    /// </remarks>
    public static bool TryReadFrom(
        string path,
        AuditPosition from,
        out IReadOnlyList<AuditEntry> entries,
        out AuditPosition next,
        out int unreadable,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(path);

        entries = [];
        next = from;
        unreadable = 0;
        error = string.Empty;

        byte[] bytes;
        var start = from;

        try
        {
            if (!File.Exists(path))
            {
                next = AuditPosition.Start;
                return true;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < start.Offset)
            {
                start = AuditPosition.Start;
            }

            stream.Seek(start.Offset, SeekOrigin.Begin);
            bytes = new byte[stream.Length - start.Offset];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }

        var complete = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        var lines = complete == 0 ? [] : Encoding.UTF8.GetString(bytes, 0, complete - 1).Split('\n');
        var read = new List<AuditEntry>();
        var number = start.Line;
        var skipped = 0;

        foreach (var raw in lines)
        {
            number++;
            var line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            if (Parse(line, number) is { } entry)
            {
                read.Add(entry);
            }
            else
            {
                skipped++;
            }
        }

        entries = read;
        next = new AuditPosition(start.Offset + complete, number);
        unreadable = skipped;
        return true;
    }

    /// <summary>
    /// Flags every grant that was served from the cache under a reason nobody was shown.
    /// </summary>
    /// <remarks>
    /// A grant is scoped to one client, entry and field, so that triple is what identifies the
    /// approval a cached release is drawing on. The comparison is between hashes rather than
    /// excerpts, because an excerpt is truncated and two reasons that differ past 200 characters
    /// differ. A <c>policy</c> release is not flagged: there is no earlier approval it could be
    /// diverging from, which THREATS.md T-12 says is the worse case rather than the better one.
    /// </remarks>
    private static void MarkUnreadReasons(List<AuditEntry> entries)
    {
        var approved = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!entry.Granted)
            {
                continue;
            }

            var key = $"{entry.Client} {entry.Entry} {entry.Field}";

            if (string.Equals(entry.Method, "prompt", StringComparison.Ordinal))
            {
                approved[key] = entry.ReasonSha256;
                continue;
            }

            if (!string.Equals(entry.Method, "grant-cache", StringComparison.Ordinal))
            {
                continue;
            }

            if (approved.TryGetValue(key, out var shown)
                && shown.Length > 0
                && entry.ReasonSha256.Length > 0
                && !string.Equals(shown, entry.ReasonSha256, StringComparison.Ordinal))
            {
                entries[i] = entry with { ReasonUnread = true };
            }
        }
    }

    private static AuditEntry? Parse(string line, int number)
    {
        try
        {
            using var parsed = JsonDocument.Parse(line);
            var root = parsed.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var client = root.TryGetProperty("client", out var who) ? who : default;
            var args = root.TryGetProperty("args", out var what) ? what : default;

            var label = Text(client, "label");
            var name = Text(client, "name");
            var timestamp = Text(root, "ts");

            return new AuditEntry
            {
                Line = number,
                At = When(timestamp),
                Timestamp = timestamp,
                Client = label.Length > 0 ? label : name,
                Label = label,
                Name = name,
                Tool = Text(root, "tool"),
                Entry = EntryPath(args, "entry"),
                Field = Text(args, "field"),
                Decision = Text(root, "decision"),
                Method = Text(root, "method"),
                Reason = Text(root, "reason"),
                Session = Text(root, "session"),
                ReasonSha256 = Text(args, "reason_sha256"),
                GrantedSeconds = root.TryGetProperty("granted_seconds", out var granted)
                    && granted.ValueKind == JsonValueKind.Number
                    && granted.TryGetInt32(out var seconds)
                        ? seconds
                        : null,
                Vault = Text(root, "vault"),
                Entries = EntryPaths(root, "entries"),
                Command = Text(root, "command"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? When(string timestamp) =>
        DateTimeOffset.TryParse(
            timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var at)
            ? at
            : null;

    private static string Text(JsonElement parent, string name)
    {
        var raw = Raw(parent, name);

        // Sanitizing an absent field would spell the sanitizer's placeholder for a name into a
        // column that should simply be blank.
        return raw.Length == 0 ? raw : EntryNameSanitizer.Sanitize(raw, FieldLength).Text;
    }

    /// <summary>
    /// The same, segment by segment, so an entry keeps its separators.
    /// </summary>
    /// <remarks>
    /// <see cref="EntryNameSanitizer.Sanitize(string, int)"/> treats <c>/</c> as structural and would
    /// flatten <c>env/dev/STRIPE_KEY</c> into <c>env dev STRIPE_KEY</c>, in the one column whose
    /// entire job is to say which entry was asked for.
    /// </remarks>
    private static string EntryPath(JsonElement parent, string name)
    {
        var raw = Raw(parent, name);

        return raw.Length == 0
            ? raw
            : EntryNameSanitizer.SanitizePath(raw, maximumLength: FieldLength).Text;
    }

    private static IReadOnlyList<string> EntryPaths(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 })
                .Select(item => EntryNameSanitizer.SanitizePath(item.GetString()!, maximumLength: FieldLength).Text),
        ];
    }

    private static string Raw(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
