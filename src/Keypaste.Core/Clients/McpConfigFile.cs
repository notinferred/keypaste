using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Linking;

namespace Keypaste.Core.Clients;

/// <summary>Whether a tool's configuration file holds keypaste's entry.</summary>
public enum McpEntryPresence
{
    /// <summary>There is no file, no servers object, or no <c>keypaste</c> member in it.</summary>
    Absent,

    /// <summary>The servers object has a <c>keypaste</c> member.</summary>
    Present,

    /// <summary>The file is not one object holding at most one servers object, or it holds a key twice, so nobody can say.</summary>
    Unknown,
}

/// <summary>A change to keypaste's entry in a tool's JSON configuration file.</summary>
/// <param name="Path">The file the tool reads.</param>
/// <param name="ServersKey">The top-level member holding the tool's servers, such as <c>mcpServers</c>.</param>
/// <param name="Entry">keypaste's entry, or null to remove it.</param>
public sealed record McpConfigEdit(string Path, string ServersKey, JsonObject? Entry);

/// <summary>What applying an edit did.</summary>
public enum McpConfigOutcomeKind
{
    /// <summary>The file was replaced and read back holding the change.</summary>
    Written,

    /// <summary>The file already said this, so nothing was written.</summary>
    Unchanged,

    /// <summary>The file was left as keypaste found it, or what keypaste wrote did not read back.</summary>
    Refused,
}

/// <summary>The outcome of applying an edit.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Backup">The copy of the first original this edit made, or null when it made none.</param>
/// <param name="Problem">Why it was refused, for the person.</param>
public sealed record McpConfigOutcome(McpConfigOutcomeKind Kind, string? Backup, string? Problem);

/// <summary>Changes keypaste's member of a tool's JSON configuration file and nothing else (D-G2-1).</summary>
/// <remarks>
/// <para>
/// Only keypaste's member changes, with one comma and the space it stood in, so the other servers, comments, the order
/// of members, the rest of the whitespace, a byte order mark and the line endings stay as the person or the tool left
/// them. The result is parsed beside the
/// original before anything is written, and the two must be equal once keypaste's member is taken out of each.
/// </para>
/// <para>
/// The first original is kept for good beside the path the tool reads, the file is replaced in one rename, and it is
/// read back. A file that does not parse, holds a key twice, or changed while keypaste was editing it is left alone.
/// </para>
/// </remarks>
public static class McpConfigFile
{
    /// <summary>The largest configuration file read, in bytes.</summary>
    public const int MaximumBytes = 64 * _mebibyte;

    private const int _mebibyte = 1024 * 1024;

    private const string _defaultUnit = "  ";

    // Utf8JsonWriter indents by at most this many characters.
    private const int _widestUnit = 127;

    private static readonly JsonDocumentOptions _lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Where the first original of a configuration file is kept.</summary>
    /// <param name="path">The file the tool reads.</param>
    /// <returns>Beside that path, never beside the file a link there names, with every link in its directory resolved.</returns>
    public static string BackupPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? throw new ArgumentException("the path has no directory", nameof(path));

        return Path.Combine(PathIdentity.Canonical(directory), Path.GetFileName(full) + CliLinks.BackupSuffix);
    }

    /// <summary>Sets or removes keypaste's member of the file, keeping its first original.</summary>
    /// <param name="edit">The file and the change.</param>
    /// <returns>What happened.</returns>
    public static McpConfigOutcome Apply(McpConfigEdit edit) => Apply(edit, beforeCommit: null);

    /// <summary>As <see cref="Apply(McpConfigEdit)"/>, running <paramref name="beforeCommit"/> just before the file is read again to commit. A test seam.</summary>
    internal static McpConfigOutcome Apply(McpConfigEdit edit, Action? beforeCommit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentException.ThrowIfNullOrEmpty(edit.Path);
        ArgumentException.ThrowIfNullOrEmpty(edit.ServersKey);

        var path = Path.GetFullPath(edit.Path);
        string target;
        byte[]? original;

        try
        {
            target = TargetOf(path);
            original = ReadWhole(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused($"could not read {path}: {ex.Message}; nothing was written");
        }

        if (!TrySplice(original ?? [], edit.ServersKey, edit.Entry, out var edited, out var error))
        {
            return Refused($"{path} {error}; nothing was written");
        }

        if (edited.AsSpan().SequenceEqual(original ?? []))
        {
            return new McpConfigOutcome(McpConfigOutcomeKind.Unchanged, Backup: null, Problem: null);
        }

        var backup = BackupPath(path);
        string? made = null;

        if (original is not null && !File.Exists(backup))
        {
            try
            {
                using var staged = AtomicFile.Stage(backup, original);
                staged.CommitWithoutReplacing();
                made = backup;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Refused($"could not keep the original as {backup}: {ex.Message}; nothing was written");
            }
        }

        beforeCommit?.Invoke();

        try
        {
            if (!Same(ReadWhole(target), original))
            {
                return Refused($"{path} changed while keypaste was editing it; nothing was written", made);
            }

            if (original is null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }

            AtomicFile.Write(target, edited, ModeFor(target, original));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused($"could not write {path}: {ex.Message}; nothing was written", made);
        }

        if (!ReadsBack(target, edit.ServersKey, edit.Entry))
        {
            var kept = File.Exists(backup) ? $"; the original is kept as {backup}" : string.Empty;
            return Refused($"{path} did not read back as written{kept}", made);
        }

        return new McpConfigOutcome(McpConfigOutcomeKind.Written, made, Problem: null);
    }

    /// <summary>Whether a configuration file's servers object holds keypaste's member.</summary>
    /// <param name="text">The file's bytes.</param>
    /// <param name="serversKey">The top-level member holding the tool's servers.</param>
    /// <param name="problem">
    /// Why it is <see cref="McpEntryPresence.Unknown"/>, completing a sentence that starts with the file's path and never
    /// quoting the file.
    /// </param>
    /// <returns>Present, Absent for an empty file or one without the member, or Unknown.</returns>
    internal static McpEntryPresence Locate(ReadOnlySpan<byte> text, string serversKey, out string? problem)
    {
        ArgumentException.ThrowIfNullOrEmpty(serversKey);

        var body = text[MarkLength(text)..];
        problem = null;

        if (IsBlank(body))
        {
            return McpEntryPresence.Absent;
        }

        if (!TryScan(body, serversKey, out var layout, out var error))
        {
            problem = error;
            return McpEntryPresence.Unknown;
        }

        return layout.Keypaste >= 0 ? McpEntryPresence.Present : McpEntryPresence.Absent;
    }

    /// <summary>The file with keypaste's member set or removed and every other byte kept.</summary>
    /// <param name="original">The file's bytes; empty for a missing file.</param>
    /// <param name="serversKey">The top-level member holding the tool's servers.</param>
    /// <param name="entry">keypaste's entry, or null to remove it.</param>
    /// <param name="edited">The new bytes, the same as <paramref name="original"/> when nothing changes.</param>
    /// <param name="error">Why not, completing a sentence that starts with the file's path.</param>
    /// <returns>False when the file must be left alone.</returns>
    internal static bool TrySplice(ReadOnlySpan<byte> original, string serversKey, JsonObject? entry, out byte[] edited, out string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(serversKey);

        var mark = MarkLength(original);
        var body = original[mark..];
        byte[] spliced;
        edited = [];

        if (IsBlank(body))
        {
            spliced = entry is null ? body.ToArray() : Fresh(serversKey, entry);
        }
        else if (TryScan(body, serversKey, out var layout, out error))
        {
            spliced = entry is null ? Removed(body, layout) : Set(body, layout, serversKey, entry);
        }
        else
        {
            return false;
        }

        error = string.Empty;

        if (!spliced.AsSpan().SequenceEqual(body) && !Keeps(body, spliced, serversKey, entry, out error))
        {
            return false;
        }

        edited = [.. original[..mark], .. spliced];
        return true;
    }

    /// <summary>A whole file, shared with whoever else has it open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its bytes, or null when there is no such file.</returns>
    /// <exception cref="IOException">It could not be read, or is larger than <see cref="MaximumBytes"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for want of permission.</exception>
    internal static byte[]? ReadWhole(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > MaximumBytes)
            {
                throw new IOException($"it is larger than {MaximumBytes / _mebibyte} MiB");
            }

            var contents = new byte[stream.Length];
            stream.ReadExactly(contents);
            return contents;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static McpConfigOutcome Refused(string problem, string? backup = null) =>
        new(McpConfigOutcomeKind.Refused, backup, problem);

    // A dotfiles link survives the edit: the file it names is replaced, never the link.
    private static string TargetOf(string path)
    {
        var info = new FileInfo(path);

        return info.LinkTarget is null ? path : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
    }

    private static bool Same(byte[]? now, byte[]? then) =>
        now is null || then is null ? now is null && then is null : now.AsSpan().SequenceEqual(then);

    private static UnixFileMode ModeFor(string target, byte[]? original)
    {
        if (!OperatingSystem.IsWindows() && original is not null)
        {
            return File.GetUnixFileMode(target);
        }

        return AtomicFile.OwnerOnly;
    }

    private static bool ReadsBack(string target, string serversKey, JsonObject? entry)
    {
        try
        {
            return ReadWhole(target) is { } written
                && Parsed(written.AsSpan(MarkLength(written))) is JsonObject root
                && Holds(root, serversKey, entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int MarkLength(ReadOnlySpan<byte> text) => text.StartsWith(ByteOrderMark) ? ByteOrderMark.Length : 0;

    private static bool IsBlank(ReadOnlySpan<byte> text) => text.IndexOfAnyExcept(" \t\r\n"u8) < 0;

    private static JsonNode? Parsed(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: _lenient);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsStrict(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);

        try
        {
            while (reader.Read())
            {
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Holds(JsonObject root, string serversKey, JsonObject? entry)
    {
        if (root[serversKey] is not JsonObject servers || !servers.TryGetPropertyValue(McpClientCatalog.ServerName, out var value))
        {
            return entry is null;
        }

        return entry is not null && JsonNode.DeepEquals(value, entry);
    }

    // A servers object the edit added to a file without one is taken out with keypaste's member before comparing.
    private static bool Keeps(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after, string serversKey, JsonObject? entry, out string error)
    {
        error = "could not be edited without changing more than keypaste's entry";

        if ((IsBlank(before) ? new JsonObject() : Parsed(before) as JsonObject) is not { } old
            || Parsed(after) is not JsonObject now
            || !Holds(now, serversKey, entry))
        {
            return false;
        }

        var added = !old.ContainsKey(serversKey);

        if (!JsonNode.DeepEquals(Stripped(old, serversKey, dropEmpty: false), Stripped(now, serversKey, dropEmpty: added))
            || (IsStrict(before) && !IsStrict(after)))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static JsonObject Stripped(JsonObject root, string serversKey, bool dropEmpty)
    {
        if (root[serversKey] is JsonObject servers)
        {
            servers.Remove(McpClientCatalog.ServerName);

            if (dropEmpty && servers.Count == 0)
            {
                root.Remove(serversKey);
            }
        }

        return root;
    }

    private static bool TryScan(ReadOnlySpan<byte> body, string serversKey, [NotNullWhen(true)] out Layout? layout, out string error)
    {
        layout = null;
        List<Token> tokens = [];
        List<(int Start, int End)> comments = [];

        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true });

            while (reader.Read())
            {
                var start = (int)reader.TokenStartIndex;
                var delimiters = reader.TokenType switch
                {
                    JsonTokenType.String or JsonTokenType.PropertyName => 2,
                    JsonTokenType.Comment => body[start + 1] == (byte)'*' ? 4 : 2,
                    _ => 0,
                };
                var end = start + reader.ValueSpan.Length + delimiters;

                if (reader.TokenType == JsonTokenType.Comment)
                {
                    comments.Add((start, end));
                    continue;
                }

                tokens.Add(new Token(reader.TokenType, start, end, reader.TokenType == JsonTokenType.PropertyName ? reader.GetString() : null));
            }
        }
        catch (JsonException ex)
        {
            error = $"is not valid JSON at line {(ex.LineNumber ?? 0) + 1}, byte {(ex.BytePositionInLine ?? 0) + 1}";
            return false;
        }

        if (tokens.Count == 0 || tokens[0].Type != JsonTokenType.StartObject)
        {
            error = "is not a JSON object";
            return false;
        }

        var closes = Closes(tokens);
        var root = ObjectAt(tokens, closes, 0);
        var servers = root.Members.FindAll(member => member.Name == serversKey);

        if (servers.Count > 1)
        {
            error = $"holds \"{serversKey}\" twice";
            return false;
        }

        if (servers.Count == 1 && servers[0].ValueType != JsonTokenType.StartObject)
        {
            error = $"has a \"{serversKey}\" that is not an object";
            return false;
        }

        var inner = servers.Count == 1 ? ObjectAt(tokens, closes, servers[0].ValueToken) : null;

        if (inner is not null && inner.Members.Count(member => member.Name == McpClientCatalog.ServerName) > 1)
        {
            error = $"holds \"{McpClientCatalog.ServerName}\" twice in \"{serversKey}\"";
            return false;
        }

        // A tool reading a repeated key keeps one of them, so which one an edit would keep cannot be known.
        if (HoldsAKeyTwice(tokens, closes))
        {
            error = "holds a key twice";
            return false;
        }

        layout = new Layout(root, inner, inner?.Members.FindIndex(member => member.Name == McpClientCatalog.ServerName) ?? -1, comments);
        error = string.Empty;
        return true;
    }

    private static bool HoldsAKeyTwice(List<Token> tokens, int[] closes)
    {
        for (var open = 0; open < tokens.Count; open++)
        {
            if (tokens[open].Type != JsonTokenType.StartObject)
            {
                continue;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var member in ObjectAt(tokens, closes, open).Members)
            {
                if (!names.Add(member.Name!))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int[] Closes(List<Token> tokens)
    {
        var closes = new int[tokens.Count];
        var open = new Stack<int>();

        for (var i = 0; i < tokens.Count; i++)
        {
            closes[i] = -1;

            if (tokens[i].Type is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                open.Push(i);
            }
            else if (tokens[i].Type is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                closes[open.Pop()] = i;
            }
        }

        return closes;
    }

    private static ObjectSpan ObjectAt(List<Token> tokens, int[] closes, int open)
    {
        var close = closes[open];
        List<Member> members = [];

        for (var name = open + 1; name < close;)
        {
            var value = name + 1;
            var last = closes[value] >= 0 ? closes[value] : value;

            members.Add(new Member(tokens[name].Name, tokens[name].Start, tokens[value].Start, tokens[last].End, tokens[last + 1].Start, value, tokens[value].Type));
            name = last + 1;
        }

        return new ObjectSpan(tokens[open].Start, tokens[close].Start, members);
    }

    private static byte[] Fresh(string serversKey, JsonObject entry) =>
        Encoding.UTF8.GetBytes(Rendered(new JsonObject { [serversKey] = Servers(entry) }, string.Empty, _defaultUnit, "\n") + "\n");

    private static JsonObject Servers(JsonObject entry) => new() { [McpClientCatalog.ServerName] = entry.DeepClone() };

    private static byte[] Set(ReadOnlySpan<byte> body, Layout layout, string serversKey, JsonObject entry)
    {
        var newLine = NewLineOf(body);
        var pretty = layout.Root.Members.Count > 0
            ? IndentIfFirstOnLine(body, layout.Root.Members[0].Start) is not null
            : body.Contains((byte)'\n');

        if (layout.Servers is not { } servers)
        {
            return Inserted(body, layout.Root, serversKey, Servers(entry), UnitOf(body, layout.Root) ?? _defaultUnit, newLine, pretty);
        }

        var unit = UnitOf(body, servers) ?? UnitOf(body, layout.Root) ?? _defaultUnit;

        if (layout.Keypaste < 0)
        {
            return Inserted(body, servers, McpClientCatalog.ServerName, entry, unit, newLine, pretty);
        }

        var member = servers.Members[layout.Keypaste];

        return JsonNode.DeepEquals(Parsed(body[member.ValueStart..member.End]), entry)
            ? body.ToArray()
            : Spliced(body, member.ValueStart, member.End, Rendered(entry, IndentIfFirstOnLine(body, member.Start), unit, newLine));
    }

    // The new member goes first, laid out as its first sibling is, or on its own line inside an empty object.
    private static byte[] Inserted(ReadOnlySpan<byte> body, ObjectSpan into, string name, JsonNode value, string unit, string newLine, bool pretty)
    {
        if (into.Members.Count > 0)
        {
            var first = into.Members[0].Start;

            return IndentIfFirstOnLine(body, first) is { } indent
                ? Spliced(body, first, first, MemberText(name, value, indent, unit, newLine) + "," + newLine + indent)
                : Spliced(body, first, first, MemberText(name, value, indent: null, unit, newLine) + ", ");
        }

        var lead = LineIndent(body, into.Open);
        var inner = lead + unit;
        var opened = into.Open + 1;

        if (IsBlank(body[opened..into.Close]))
        {
            return pretty
                ? Spliced(body, opened, into.Close, newLine + inner + MemberText(name, value, inner, unit, newLine) + newLine + lead)
                : Spliced(body, opened, into.Close, MemberText(name, value, indent: null, unit, newLine));
        }

        return pretty
            ? Spliced(body, opened, opened, newLine + inner + MemberText(name, value, inner, unit, newLine))
            : Spliced(body, opened, opened, " " + MemberText(name, value, indent: null, unit, newLine));
    }

    // The member and one comma go, with the lines they stood on when nothing else is on them, so an insert then a removal
    // gives back the original bytes.
    private static byte[] Removed(ReadOnlySpan<byte> body, Layout layout)
    {
        if (layout.Servers is not { } servers || layout.Keypaste < 0)
        {
            return body.ToArray();
        }

        var member = servers.Members[layout.Keypaste];
        var after = CommaIn(body, member.End, member.Next, layout.Comments);

        if (after >= 0)
        {
            if (!IsBlank(body[member.End..after]))
            {
                return Cut(Cut(body, after, after + 1), member.Start, member.End);
            }

            var separated = SpacesFrom(body, after + 1);
            var (start, end) = WholeLines(body, member.Start, separated) ?? (member.Start, separated);
            return Cut(body, start, end);
        }

        var before = layout.Keypaste > 0 ? CommaIn(body, servers.Members[layout.Keypaste - 1].End, member.Start, layout.Comments) : -1;

        if (before >= 0)
        {
            return IsBlank(body[(before + 1)..member.Start])
                ? Cut(body, before, member.End)
                : Cut(Cut(body, member.Start, member.End), before, before + 1);
        }

        var (from, to) = WholeLines(body, member.Start, member.End) ?? (member.Start, member.End);
        return Cut(body, from, to);
    }

    private static string MemberText(string name, JsonNode value, string? indent, string unit, string newLine) =>
        "\"" + JsonEncodedText.Encode(name, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value + "\": " + Rendered(value, indent, unit, newLine);

    private static string Rendered(JsonNode value, string? indent, string unit, string newLine)
    {
        var options = new JsonWriterOptions
        {
            // Relaxed so a path's non-ASCII letters stay readable in the file; it is never embedded in HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = indent is not null,
            IndentCharacter = unit[0],
            IndentSize = unit.Length,
            NewLine = newLine,
        };

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            value.WriteTo(writer);
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray());
        return indent is null ? text : text.Replace(newLine, newLine + indent, StringComparison.Ordinal);
    }

    private static byte[] Spliced(ReadOnlySpan<byte> body, int from, int to, string text) =>
        [.. body[..from], .. Encoding.UTF8.GetBytes(text), .. body[to..]];

    private static byte[] Cut(ReadOnlySpan<byte> body, int from, int to) => [.. body[..from], .. body[to..]];

    private static string NewLineOf(ReadOnlySpan<byte> body)
    {
        var lineFeed = body.IndexOf((byte)'\n');

        return lineFeed > 0 && body[lineFeed - 1] == (byte)'\r' ? "\r\n" : "\n";
    }

    private static string? IndentIfFirstOnLine(ReadOnlySpan<byte> body, int position)
    {
        var start = position;

        while (start > 0 && body[start - 1] is (byte)' ' or (byte)'\t')
        {
            start--;
        }

        return start == 0 || body[start - 1] == (byte)'\n' ? Encoding.ASCII.GetString(body[start..position]) : null;
    }

    private static string LineIndent(ReadOnlySpan<byte> body, int position)
    {
        var start = body[..position].LastIndexOf((byte)'\n') + 1;

        return Encoding.ASCII.GetString(body[start..Math.Min(SpacesFrom(body, start), position)]);
    }

    // What one level adds, read from the object's first member against the line the object opens on.
    private static string? UnitOf(ReadOnlySpan<byte> body, ObjectSpan container)
    {
        if (container.Members.Count == 0 || IndentIfFirstOnLine(body, container.Members[0].Start) is not { } indent)
        {
            return null;
        }

        var lead = LineIndent(body, container.Open);

        if (indent.Length <= lead.Length || !indent.StartsWith(lead, StringComparison.Ordinal))
        {
            return null;
        }

        var unit = indent[lead.Length..];
        return unit.Length <= _widestUnit && !unit.AsSpan().ContainsAnyExcept(unit[0]) ? unit : null;
    }

    private static int SpacesFrom(ReadOnlySpan<byte> body, int position)
    {
        while (position < body.Length && body[position] is (byte)' ' or (byte)'\t')
        {
            position++;
        }

        return position;
    }

    private static (int Start, int End)? WholeLines(ReadOnlySpan<byte> body, int start, int end)
    {
        var stop = SpacesFrom(body, end);
        var lineBreak = body[stop..].StartsWith("\r\n"u8) ? 2 : body[stop..].StartsWith("\n"u8) ? 1 : 0;

        return lineBreak > 0 && IndentIfFirstOnLine(body, start) is { } indent ? (start - indent.Length, stop + lineBreak) : null;
    }

    private static int CommaIn(ReadOnlySpan<byte> body, int from, int to, List<(int Start, int End)> comments)
    {
        var at = from;

        while (at < to)
        {
            var past = PastComment(comments, at);

            if (past > at)
            {
                at = past;
            }
            else if (body[at] == (byte)',')
            {
                return at;
            }
            else
            {
                at++;
            }
        }

        return -1;
    }

    private static int PastComment(List<(int Start, int End)> comments, int at)
    {
        foreach (var (start, end) in comments)
        {
            if (start <= at && at < end)
            {
                return end;
            }
        }

        return at;
    }

    private readonly record struct Token(JsonTokenType Type, int Start, int End, string? Name);

    /// <summary>One member of an object.</summary>
    /// <param name="Name">Its name, unescaped.</param>
    /// <param name="Start">Where its name starts.</param>
    /// <param name="ValueStart">Where its value starts.</param>
    /// <param name="End">Just past its value.</param>
    /// <param name="Next">Where the next member's name, or the object's closing brace, starts.</param>
    /// <param name="ValueToken">Its value's index among the tokens.</param>
    /// <param name="ValueType">Its value's kind.</param>
    private sealed record Member(string? Name, int Start, int ValueStart, int End, int Next, int ValueToken, JsonTokenType ValueType);

    private sealed record ObjectSpan(int Open, int Close, List<Member> Members);

    /// <summary>Where the parts keypaste edits are.</summary>
    /// <param name="Root">The top-level object.</param>
    /// <param name="Servers">The servers object, or null when there is none.</param>
    /// <param name="Keypaste">keypaste's member's index in <paramref name="Servers"/>, or -1.</param>
    /// <param name="Comments">Every comment's range, so a comma inside one is never taken for a separator.</param>
    private sealed record Layout(ObjectSpan Root, ObjectSpan? Servers, int Keypaste, List<(int Start, int End)> Comments);
}
