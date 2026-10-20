using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Clients;

/// <summary>Whether a tool's configuration holds keypaste's entry.</summary>
public enum McpEntryPresence
{
    /// <summary>There is no file, no servers object, or no <c>keypaste</c> member in it.</summary>
    Absent,

    /// <summary>The servers object has a <c>keypaste</c> member.</summary>
    Present,

    /// <summary>
    /// Nobody can say: the file does not read as the tool's format and shape, it could not be read, or the tool keeps it
    /// somewhere keypaste does not look.
    /// </summary>
    Unknown,
}

/// <summary>What reading a tool's own configuration found.</summary>
/// <param name="Presence">Whether the tool holds keypaste's entry.</param>
/// <param name="Path">The file read, or null when keypaste cannot tell which file the tool reads.</param>
/// <param name="Reason">Why the presence is <see cref="McpEntryPresence.Unknown"/>, for the person; null otherwise.</param>
public sealed record McpEntryReading(McpEntryPresence Presence, string? Path, string? Reason)
{
    /// <summary>Gets what keypaste's entry starts, when it is present and its command and arguments read as strings; otherwise null.</summary>
    public McpEntryLaunch? Launch { get; init; }
}

/// <summary>What keypaste's entry in a tool's configuration starts, and nothing else of the entry.</summary>
/// <param name="Command">The program, as the file names it.</param>
/// <param name="Arguments">Its arguments, in order.</param>
public sealed record McpEntryLaunch(string Command, IReadOnlyList<string> Arguments)
{
    /// <summary>Whether another names the same command and the same arguments, in the same order.</summary>
    /// <param name="other">The other launch.</param>
    /// <returns><see langword="true"/> when every string matches ordinally.</returns>
    public bool Equals(McpEntryLaunch? other) =>
        other is not null
        && string.Equals(Command, other.Command, StringComparison.Ordinal)
        && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Command);
}

/// <summary>Reads whether a tool's own configuration holds keypaste, changing nothing.</summary>
/// <remarks>
/// The file is opened read-only and is never written, backed up or copied. Only the presence and keypaste's own command
/// and arguments are kept: the bytes are cleared once read and never shown, because <c>~/.claude.json</c> also holds
/// Claude Code's sign-in. A reading that cannot be sure says Unknown, never Absent, so a tool is called not connected only
/// when its own file says so (D-0437).
/// </remarks>
public static class McpClientConfig
{
    /// <summary>Reads <paramref name="client"/>'s configuration on <paramref name="machine"/>.</summary>
    /// <param name="client">The tool.</param>
    /// <param name="machine">Where the tool keeps its files.</param>
    /// <returns>Whether its file holds keypaste's entry, and which file was read.</returns>
    public static McpEntryReading Read(McpClient client, McpClientMachine machine)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(machine);

        var unlocated = $"keypaste cannot tell where {client.DisplayName} keeps its configuration";

        if (client.Configuration is not { } location)
        {
            return Unknown(path: null, unlocated);
        }

        if (location.MovedOn(machine) is { } variable)
        {
            return Unknown(path: null, $"{variable} is set, so {client.DisplayName} may keep its configuration where keypaste does not look");
        }

        if (location.PathOn(machine) is not { } path)
        {
            return Unknown(path: null, unlocated);
        }

        byte[]? text;

        try
        {
            text = McpConfigFile.ReadWhole(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unknown(path, $"could not read {path}: {ex.Message}");
        }

        if (text is null)
        {
            return new McpEntryReading(McpEntryPresence.Absent, path, Reason: null);
        }

        try
        {
            string? problem;
            McpEntryLaunch? launch;
            var presence = location.Format == McpConfigFormat.Toml
                ? CodexConfigScan.Scan(text, location.ServersKey, out problem, out launch)
                : McpConfigFile.Locate(text, location.ServersKey, out problem, out launch);

            return presence == McpEntryPresence.Unknown
                ? Unknown(path, $"{path} {problem}")
                : new McpEntryReading(presence, path, Reason: null) { Launch = launch };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(text);
        }
    }

    private static McpEntryReading Unknown(string? path, string reason) => new(McpEntryPresence.Unknown, path, reason);
}

/// <summary>Whether Codex's <c>config.toml</c> holds keypaste's server, found by scanning its lines.</summary>
/// <remarks>
/// A scan rather than a parse, because Codex's file holds TOML that <see cref="Infrastructure.Toml"/> refuses. It
/// recognises the table headers <c>codex mcp add</c> writes, <c>[mcp_servers.keypaste]</c> and its sub-tables, with
/// spaces and quoted names allowed. Any other line naming the servers table, a header with an escaped name, or a
/// multi-line string, inside which a header would be text, makes the answer Unknown. In keypaste's own table it reads
/// <c>command</c> and <c>args</c> when each is a basic or literal string, or a one-line array of them, and leaves the
/// launch unread when either is written any other way.
/// </remarks>
internal static class CodexConfigScan
{
    private static readonly SearchValues<byte> _bareKey =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-"u8);

    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private enum Header
    {
        None,
        Escaped,
        OtherTable,
        Servers,
        Keypaste,
        KeypastePart,
        OtherServer,
    }

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Scans a <c>config.toml</c>.</summary>
    /// <param name="text">The file's bytes.</param>
    /// <param name="serversKey">The table holding the servers, <c>mcp_servers</c>.</param>
    /// <param name="problem">
    /// Why it is <see cref="McpEntryPresence.Unknown"/>, completing a sentence that starts with the file's path and never
    /// quoting the file.
    /// </param>
    /// <param name="launch">What keypaste's table starts, when it is present and its <c>command</c> and <c>args</c> read; otherwise null.</param>
    /// <returns>Present, Absent, or Unknown.</returns>
    internal static McpEntryPresence Scan(ReadOnlySpan<byte> text, string serversKey, out string? problem, out McpEntryLaunch? launch)
    {
        ArgumentException.ThrowIfNullOrEmpty(serversKey);

        ReadOnlySpan<byte> servers = Encoding.UTF8.GetBytes(serversKey);
        var rest = text.StartsWith(ByteOrderMark) ? text[ByteOrderMark.Length..] : text;
        var presence = McpEntryPresence.Absent;
        var entry = new EntryLines();
        problem = null;
        launch = null;

        while (!rest.IsEmpty)
        {
            var end = rest.IndexOf((byte)'\n');
            var line = (end < 0 ? rest : rest[..end]).Trim(" \t\r"u8);
            rest = end < 0 ? [] : rest[(end + 1)..];

            if (line.IndexOf("\"\"\""u8) >= 0 || line.IndexOf("'''"u8) >= 0)
            {
                problem = "holds a multi-line string, inside which keypaste cannot tell a table from text";
                return McpEntryPresence.Unknown;
            }

            if (line.StartsWith("#"u8))
            {
                continue;
            }

            var header = HeaderOf(line, servers);

            switch (header)
            {
                case Header.Keypaste or Header.KeypastePart:
                    presence = McpEntryPresence.Present;
                    break;
                case Header.Escaped:
                    problem = "has a table header with an escaped name, which keypaste does not read";
                    return McpEntryPresence.Unknown;
                case Header.Servers:
                case Header.None when line.IndexOf(servers) >= 0:
                    problem = $"names {serversKey} other than in a [{serversKey}.<name>] table header";
                    return McpEntryPresence.Unknown;
            }

            if (header != Header.None || line.StartsWith("["u8))
            {
                entry.Enter(header == Header.Keypaste);
            }
            else
            {
                entry.Take(line);
            }
        }

        launch = presence == McpEntryPresence.Present ? entry.Launch : null;
        return presence;
    }

    private static Header HeaderOf(ReadOnlySpan<byte> line, ReadOnlySpan<byte> servers)
    {
        if (line.Length < 2 || line[0] != (byte)'[' || line[1] == (byte)'[')
        {
            return Header.None;
        }

        var rest = line[1..];
        var underServers = false;
        var named = false;
        var keys = 0;

        while (true)
        {
            rest = rest.TrimStart(" \t"u8);

            if (!TryKey(ref rest, out var key, out var escaped))
            {
                return Header.None;
            }

            if (escaped)
            {
                return Header.Escaped;
            }

            if (keys == 0)
            {
                underServers = key.SequenceEqual(servers);
            }
            else if (keys == 1)
            {
                named = Ascii.Equals(key, McpClientCatalog.ServerName);
            }

            keys++;
            rest = rest.TrimStart(" \t"u8);

            if (rest.StartsWith("."u8))
            {
                rest = rest[1..];
                continue;
            }

            if (!rest.StartsWith("]"u8))
            {
                return Header.None;
            }

            rest = rest[1..].TrimStart(" \t"u8);

            if (!rest.IsEmpty && !rest.StartsWith("#"u8))
            {
                return Header.None;
            }

            break;
        }

        if (!underServers)
        {
            return Header.OtherTable;
        }

        return keys == 1 ? Header.Servers : !named ? Header.OtherServer : keys == 2 ? Header.Keypaste : Header.KeypastePart;
    }

    private static bool TryKey(ref ReadOnlySpan<byte> rest, out ReadOnlySpan<byte> key, out bool escaped)
    {
        key = default;
        escaped = false;

        if (rest.IsEmpty)
        {
            return false;
        }

        if (rest[0] is (byte)'"' or (byte)'\'')
        {
            var close = rest[1..].IndexOf(rest[0]);

            if (close < 0)
            {
                return false;
            }

            key = rest.Slice(1, close);
            escaped = rest[0] == (byte)'"' && key.Contains((byte)'\\');
            rest = rest[(close + 2)..];
            return true;
        }

        var length = rest.IndexOfAnyExcept(_bareKey);

        if (length == 0)
        {
            return false;
        }

        length = length < 0 ? rest.Length : length;
        key = rest[..length];
        rest = rest[length..];
        return true;
    }

    private static bool IsEnd(ReadOnlySpan<byte> rest)
    {
        var after = rest.TrimStart(" \t"u8);

        return after.IsEmpty || after.StartsWith("#"u8);
    }

    private static bool TryArray(ref ReadOnlySpan<byte> rest, [NotNullWhen(true)] out List<string>? values)
    {
        values = null;

        if (!rest.StartsWith("["u8))
        {
            return false;
        }

        List<string> read = [];
        rest = rest[1..].TrimStart(" \t"u8);

        while (!rest.StartsWith("]"u8))
        {
            if (!TryString(ref rest, out var value))
            {
                return false;
            }

            read.Add(value);
            rest = rest.TrimStart(" \t"u8);

            if (rest.StartsWith(","u8))
            {
                rest = rest[1..].TrimStart(" \t"u8);
            }
            else if (!rest.StartsWith("]"u8))
            {
                return false;
            }
        }

        rest = rest[1..];
        values = read;
        return true;
    }

    private static bool TryString(ref ReadOnlySpan<byte> rest, [NotNullWhen(true)] out string? value)
    {
        value = null;

        if (rest.StartsWith("'"u8))
        {
            var close = rest[1..].IndexOf((byte)'\'');

            if (close < 0 || !TryDecode(rest.Slice(1, close), out value))
            {
                return false;
            }

            rest = rest[(close + 2)..];
            return true;
        }

        if (!rest.StartsWith("\""u8))
        {
            return false;
        }

        var text = new StringBuilder();
        var at = 1;

        while (true)
        {
            var run = rest[at..].IndexOfAny((byte)'"', (byte)'\\');

            if (run < 0 || !TryDecode(rest.Slice(at, run), out var plain))
            {
                return false;
            }

            text.Append(plain);
            at += run;

            if (rest[at] == (byte)'"')
            {
                value = text.ToString();
                rest = rest[(at + 1)..];
                return true;
            }

            if (!TryEscape(rest[(at + 1)..], text, out var length))
            {
                return false;
            }

            at += 1 + length;
        }
    }

    private static bool TryEscape(ReadOnlySpan<byte> escape, StringBuilder text, out int length)
    {
        length = 1;

        if (escape.IsEmpty)
        {
            return false;
        }

        if (Escaped(escape[0]) is { } letter)
        {
            text.Append(letter);
            return true;
        }

        var digits = escape[0] switch
        {
            (byte)'u' => 4,
            (byte)'U' => 8,
            _ => 0,
        };
        length += digits;

        if (digits == 0
            || escape.Length < length
            || !uint.TryParse(Encoding.ASCII.GetString(escape.Slice(1, digits)), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)
            || !Rune.TryCreate(code, out var rune))
        {
            return false;
        }

        text.Append(rune.ToString());
        return true;
    }

    private static char? Escaped(byte letter) => letter switch
    {
        (byte)'b' => '\b',
        (byte)'t' => '\t',
        (byte)'n' => '\n',
        (byte)'f' => '\f',
        (byte)'r' => '\r',
        (byte)'"' => '"',
        (byte)'\\' => '\\',
        _ => null,
    };

    // TOML allows no control character but a tab in a one-line string.
    private static bool TryDecode(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out string? text)
    {
        text = null;

        foreach (var octet in bytes)
        {
            if (octet is (< 0x20 and not 0x09) or 0x7F)
            {
                return false;
            }
        }

        try
        {
            text = _strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>The <c>command</c> and <c>args</c> lines of keypaste's own table, read only when Codex would read them the same way.</summary>
    private sealed class EntryLines
    {
        private string? _command;
        private List<string>? _arguments;
        private bool _unreadable;
        private bool _entered;
        private bool _inside;

        internal McpEntryLaunch? Launch =>
            !_unreadable && _command is { Length: > 0 } command ? new McpEntryLaunch(command, _arguments ?? []) : null;

        // A second [mcp_servers.keypaste] header makes a file Codex refuses, so neither table is believed.
        internal void Enter(bool keypaste)
        {
            _unreadable |= keypaste && _entered;
            _entered |= keypaste;
            _inside = keypaste;
        }

        internal void Take(ReadOnlySpan<byte> line)
        {
            var rest = line;

            if (!_inside || !TryKey(ref rest, out var key, out var escaped))
            {
                return;
            }

            var isCommand = key.SequenceEqual("command"u8);

            if (!escaped && !isCommand && !key.SequenceEqual("args"u8))
            {
                return;
            }

            rest = rest.TrimStart(" \t"u8);

            if (escaped || !rest.StartsWith("="u8))
            {
                _unreadable = true;
                return;
            }

            rest = rest[1..].TrimStart(" \t"u8);

            if (isCommand)
            {
                _unreadable |= _command is not null || !TryString(ref rest, out _command) || !IsEnd(rest);
            }
            else
            {
                _unreadable |= _arguments is not null || !TryArray(ref rest, out _arguments) || !IsEnd(rest);
            }
        }
    }
}
