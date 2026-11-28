using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

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

    /// <summary>The UUID, as hex, of the item the line is about, or empty where the line names none, as every line before items were named.</summary>
    public string Item { get; init; } = string.Empty;

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
    /// True when it was released without asking under a reason that differs from the one shown to
    /// the person who last approved it. This is THREATS.md T-12, and it is the whole reason the record
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

    /// <summary>On a summary line, one agent's or one browser pairing's lookups of one UTC day the vault's owner folded into it; otherwise null.</summary>
    public AuditSummary? Summary { get; init; }

    /// <summary>On a removal line, the whole days the vault's owner removed; otherwise null.</summary>
    public AuditRemoval? Removal { get; init; }

    /// <summary>How many requests the line records: a summary's folded lookups, a removal's removed requests, otherwise one.</summary>
    public int Requests => Summary is { } summary ? summary.Counts.Sum(count => count.Count) : Removal?.Count ?? 1;

    /// <summary>Whether this is the owner's checkpoint rather than a request, which no listing shows.</summary>
    public bool IsCheckpoint => string.Equals(Method, AuditCheckpoint.Method, StringComparison.Ordinal);

    /// <summary>Whether this is the record sealing a compacted log rather than a request, which no listing shows.</summary>
    public bool IsCompaction => string.Equals(Method, AuditCompaction.Method, StringComparison.Ordinal);

    /// <summary>
    /// Whether the person's paired browser made this request through keypaste's extension, rather than an agent: a pairing,
    /// a pairing's check, a page's lookup, a fill, the person's answer to a sign-in frame's trust, or a day of its lookups
    /// folded into one line. A pairing or a frame's trust the person removed in keypaste itself is not the browser's.
    /// </summary>
    /// <remarks>Only the vault's owner writes the browser's lines and its summaries, under tool names no bridge can give its own.</remarks>
    public bool ByBrowser => IsBrowserSummary
        || (string.Equals(Tool, AuditTools.Browser, StringComparison.Ordinal)
            && !string.Equals(Method, AuditLog.Wire(AuditMethod.Removed), StringComparison.Ordinal));

    /// <summary>
    /// Whether this is the browser asking which logins match a page, which lists them by name and releases no password, or
    /// a day of those lookups folded into one line.
    /// </summary>
    public bool IsBrowserLookup => IsBrowserSummary
        || (ByBrowser && string.Equals(Field, RequestRecords.BrowserListField, StringComparison.Ordinal));

    /// <summary>Whether this is a summary line that folds the browser's lookups alone.</summary>
    private bool IsBrowserSummary => string.Equals(Tool, AuditTools.Summary, StringComparison.Ordinal)
        && Summary is { Counts.Count: > 0 } summary
        && summary.Counts.All(count => string.Equals(count.Tool, AuditTools.Browser, StringComparison.Ordinal));
}

/// <summary>Where an incremental read of the log stopped.</summary>
/// <param name="Offset">The byte just past the last complete line read.</param>
/// <param name="Line">The number of physical lines read so far.</param>
/// <param name="Head">The file's first bytes when it was read, which a compacted file's new first line changes.</param>
public readonly record struct AuditPosition(long Offset, int Line, string Head = "")
{
    /// <summary>How many of the file's first bytes tell one file from the one that replaced it: past the first line's timestamp.</summary>
    internal const int HeadBytes = 64;

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

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return TryRead(stream, out entries, out unreadable, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            entries = [];
            unreadable = 0;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Reads a log's bytes whole, opened as <see cref="TryRead(string, out IReadOnlyList{AuditEntry}, out int, out string)"/> opens it.</summary>
    /// <param name="path">The log.</param>
    /// <param name="bytes">Its bytes, or null when it could not be read.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when the file could not be read at all.</returns>
    /// <remarks>For a caller that reads the records and checks the chain over one copy of the file, so both see the same lines.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static bool TryReadBytes(string path, [NotNullWhen(true)] out byte[]? bytes, out string error)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            file.CopyTo(copy);
            bytes = copy.ToArray();
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            bytes = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Reads every record in a log that is already open, as <see cref="TryRead(string, out IReadOnlyList{AuditEntry}, out int, out string)"/> reads a file.</summary>
    /// <param name="stream">The bytes of the log, positioned at its start; left open.</param>
    /// <param name="entries">The records, in file order.</param>
    /// <param name="unreadable">How many lines were not records this version understands.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when the stream could not be read at all.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static bool TryRead(
        Stream stream,
        out IReadOnlyList<AuditEntry> entries,
        out int unreadable,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(stream);

        entries = [];
        unreadable = 0;

        var read = new List<AuditEntry>();
        var skipped = 0;
        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);

            var number = 0;
            while (reader.ReadLine() is { } line)
            {
                number++;

                if (line.Length == 0)
                {
                    continue;
                }

                if (Parse(line, number, sanitized) is { } entry)
                {
                    if (!entry.IsCheckpoint && !entry.IsCompaction)
                    {
                        read.Add(entry);
                    }
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
    /// written. A file shorter than <paramref name="from"/>, or one that no longer begins with the bytes it began
    /// with, was replaced, as compacting it does, so it is read again from its start, which a caller sees as
    /// <paramref name="next"/> numbering lines from one again.
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
        string head;

        try
        {
            if (!File.Exists(path))
            {
                next = AuditPosition.Start;
                return true;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var first = new byte[(int)Math.Min(stream.Length, AuditPosition.HeadBytes)];
            stream.ReadExactly(first);
            head = Convert.ToHexStringLower(first);

            if (stream.Length < start.Offset || (start.Offset > 0 && !head.StartsWith(start.Head, StringComparison.Ordinal)))
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
        var read = new List<AuditEntry>();
        var number = start.Line;
        var skipped = 0;
        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var at = 0; at < complete;)
        {
            var end = Array.IndexOf(bytes, (byte)'\n', at, complete - at);
            var line = bytes.AsMemory(at, end - at);
            at = end + 1;
            number++;

            while (line.Length > 0 && line.Span[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (Parse(line, number, sanitized) is { } entry)
            {
                if (!entry.IsCheckpoint && !entry.IsCompaction)
                {
                    read.Add(entry);
                }
            }
            else
            {
                skipped++;
            }
        }

        entries = read;
        next = new AuditPosition(start.Offset + complete, number, head);
        unreadable = skipped;
        return true;
    }

    /// <summary>
    /// Flags every release under an Always allow row, or an older log's cached grant, under a reason nobody was shown.
    /// </summary>
    /// <remarks>
    /// One client, entry and field identify the approval such a release follows. The comparison is between hashes rather than
    /// excerpts, because an excerpt is truncated and two reasons that differ past 200 characters
    /// differ. An <c>automatic</c> release is not flagged: there is no earlier approval it could be
    /// diverging from, which THREATS.md T-12 says is the worse case rather than the better one.
    /// </remarks>
    private static void MarkUnreadReasons(List<AuditEntry> entries)
    {
        var approved = new Dictionary<(string Client, string Entry, string Field), string>();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!entry.Granted)
            {
                continue;
            }

            var key = (entry.Client, entry.Entry, entry.Field);

            if (string.Equals(entry.Method, "prompt", StringComparison.Ordinal))
            {
                approved[key] = entry.ReasonSha256;
                continue;
            }

            // always-allowed is what this writes; grant-cache stands in a log an older keypaste wrote.
            if (!string.Equals(entry.Method, "always-allowed", StringComparison.Ordinal)
                && !string.Equals(entry.Method, "grant-cache", StringComparison.Ordinal))
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

    private static AuditEntry? Parse(string line, int number, Dictionary<string, string> sanitized)
    {
        try
        {
            using var parsed = JsonDocument.Parse(line);
            return Read(parsed.RootElement, number, sanitized);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AuditEntry? Parse(ReadOnlyMemory<byte> line, int number, Dictionary<string, string> sanitized)
    {
        try
        {
            // Invalid UTF-8 is decoded first, as it always was, which replaces the bad bytes where parsing them would refuse the line.
            using var parsed = Utf8.IsValid(line.Span) ? JsonDocument.Parse(line) : JsonDocument.Parse(Encoding.UTF8.GetString(line.Span));
            return Read(parsed.RootElement, number, sanitized);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The record a parsed line holds, or null when it is not one.</summary>
    /// <param name="root">The parsed line.</param>
    /// <param name="number">Its physical line.</param>
    /// <param name="sanitized">This read's sanitized text of the few-valued fields keypaste writes, by their raw text.</param>
    private static AuditEntry? Read(JsonElement root, int number, Dictionary<string, string> sanitized)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var client = root.TryGetProperty("client", out var who) ? who : default;
        var args = root.TryGetProperty("args", out var what) ? what : default;

        var label = Repeated(client, "label", sanitized);
        var name = Repeated(client, "name", sanitized);
        var timestamp = Text(root, "ts");

        return new AuditEntry
        {
            Line = number,
            At = When(timestamp),
            Timestamp = timestamp,
            Client = label.Length > 0 ? label : name,
            Label = label,
            Name = name,
            Tool = Repeated(root, "tool", sanitized),
            Entry = EntryPath(args, "entry"),
            Item = Text(args, "item"),
            Field = Text(args, "field"),
            Decision = Repeated(root, "decision", sanitized),
            Method = Repeated(root, "method", sanitized),
            Reason = Text(root, "reason"),
            Session = Repeated(root, "session", sanitized),
            ReasonSha256 = Text(args, "reason_sha256"),
            GrantedSeconds = root.TryGetProperty("granted_seconds", out var granted)
                && granted.ValueKind == JsonValueKind.Number
                && granted.TryGetInt32(out var seconds)
                    ? seconds
                    : null,
            Vault = Repeated(root, "vault", sanitized),
            Entries = EntryPaths(root, "entries"),
            Command = Text(root, "command"),
            Summary = SummaryOf(root),
            Removal = RemovalOf(root),
        };
    }

    private static AuditSummary? SummaryOf(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var counts = summary.TryGetProperty("counts", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Where(count => count.ValueKind == JsonValueKind.Object)
                .Select(count => new AuditFoldCount(
                    Text(count, "tool"),
                    string.Equals(Raw(count, "decision"), "granted", StringComparison.Ordinal) ? AuditDecision.Granted : AuditDecision.Denied,
                    Number(count, "count")))
                .ToList()
            : [];

        return new AuditSummary(Text(summary, "day"), Text(summary, "first"), Text(summary, "last"), counts, Text(summary, "sha256"));
    }

    private static AuditRemoval? RemovalOf(JsonElement root) =>
        root.TryGetProperty("removed", out var removed) && removed.ValueKind == JsonValueKind.Object
            ? new AuditRemoval(Text(removed, "from"), Text(removed, "to"), Number(removed, "count"), Text(removed, "sha256"))
            : null;

    /// <summary>A count, or zero when the line does not hold one that fits; never negative.</summary>
    private static int Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0
            ? number
            : 0;

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

    /// <summary>The same, sanitized once per read for a field that takes few values, so the lines repeating one share its text.</summary>
    /// <remarks>Never for text an agent writes freely, such as a reason, which would grow the cache by a line's worth every line.</remarks>
    private static string Repeated(JsonElement parent, string name, Dictionary<string, string> sanitized)
    {
        var raw = Raw(parent, name);

        if (raw.Length == 0)
        {
            return raw;
        }

        if (!sanitized.TryGetValue(raw, out var text))
        {
            text = EntryNameSanitizer.Sanitize(raw, FieldLength).Text;
            sanitized.Add(raw, text);
        }

        return text;
    }

    /// <summary>
    /// The same as <see cref="Text"/>, segment by segment, so an entry keeps its separators.
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
