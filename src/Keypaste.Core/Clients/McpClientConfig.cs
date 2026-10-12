using System.Buffers;

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
public sealed record McpEntryReading(McpEntryPresence Presence, string? Path, string? Reason);

/// <summary>Reads whether a tool's own configuration holds keypaste, changing nothing.</summary>
/// <remarks>
/// The file is opened read-only and is never written, backed up or copied. Only the presence is kept: the bytes are
/// cleared once read and never shown, because <c>~/.claude.json</c> also holds Claude Code's sign-in. A reading that
/// cannot be sure says Unknown, never Absent, so a tool is called not connected only when its own file says so (D-0437).
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
            var presence = location.Format == McpConfigFormat.Toml
                ? CodexConfigScan.Scan(text, location.ServersKey, out problem)
                : McpConfigFile.Locate(text, location.ServersKey, out problem);

            return presence == McpEntryPresence.Unknown
                ? Unknown(path, $"{path} {problem}")
                : new McpEntryReading(presence, path, Reason: null);
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
/// multi-line string, inside which a header would be text, makes the answer Unknown.
/// </remarks>
internal static class CodexConfigScan
{
    private static readonly SearchValues<byte> _bareKey =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-"u8);

    private enum Header
    {
        None,
        Escaped,
        OtherTable,
        Servers,
        Keypaste,
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
    /// <returns>Present, Absent, or Unknown.</returns>
    internal static McpEntryPresence Scan(ReadOnlySpan<byte> text, string serversKey, out string? problem)
    {
        ArgumentException.ThrowIfNullOrEmpty(serversKey);

        ReadOnlySpan<byte> servers = Encoding.UTF8.GetBytes(serversKey);
        var rest = text.StartsWith(ByteOrderMark) ? text[ByteOrderMark.Length..] : text;
        var presence = McpEntryPresence.Absent;
        problem = null;

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

            switch (HeaderOf(line, servers))
            {
                case Header.Keypaste:
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
        }

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

        return keys == 1 ? Header.Servers : named ? Header.Keypaste : Header.OtherServer;
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
}
