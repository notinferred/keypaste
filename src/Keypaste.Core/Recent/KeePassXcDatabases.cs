namespace Keypaste.Core.Recent;

/// <summary>The platforms whose KeePassXC keeps its local settings in a different place.</summary>
internal enum KeePassXcPlatform
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// The databases KeePassXC last opened on this machine, which the desktop offers on its first run.
/// </summary>
/// <remarks>
/// Read from KeePassXC's local <c>keepassxc.ini</c>, and only its <c>LastActiveDatabase</c>,
/// <c>LastOpenedDatabases</c> and <c>LastDatabases</c>; the file is never written (D-0377, T-24).
/// </remarks>
public static class KeePassXcDatabases
{
    /// <summary>How many databases are offered.</summary>
    public const int Capacity = RecentVaults.Capacity;

    /// <summary>The largest settings file read. Anything larger is treated as no databases.</summary>
    public const int MaximumBytes = 1024 * 1024;

    internal const string ActiveKey = "LastActiveDatabase";

    internal const string OpenedKey = "LastOpenedDatabases";

    internal const string RecentKey = "LastDatabases";

    private static readonly string[] _keys = [ActiveKey, OpenedKey, RecentKey];

    /// <summary>Where KeePassXC 2.7 keeps its local settings for this user, or null when that cannot be said.</summary>
    /// <returns>The file's path. It is not checked for existence.</returns>
    public static string? LocalConfigPath() =>
        LocalConfigPath(
            OperatingSystem.IsWindows() ? KeePassXcPlatform.Windows
                : OperatingSystem.IsMacOS() ? KeePassXcPlatform.MacOS
                : KeePassXcPlatform.Linux,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"));

    /// <summary>The databases KeePassXC last opened that still exist, the last active first.</summary>
    /// <param name="configPath">KeePassXC's local settings file, or null for none.</param>
    /// <returns>Each existing file once, at most <see cref="Capacity"/>; empty when the file is missing, unreadable or not a settings file.</returns>
    public static IReadOnlyList<string> Read(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || ReadBytes(configPath) is not { } bytes)
        {
            return [];
        }

        var offered = new List<string>(Capacity);

        foreach (var candidate in Parse(bytes))
        {
            if (offered.Count == Capacity)
            {
                break;
            }

            if (FullPath(candidate) is { } full
                && File.Exists(full)
                && !offered.Exists(path => string.Equals(path, full, PathIdentity.Comparison)))
            {
                offered.Add(full);
            }
        }

        return offered;
    }

    /// <summary>Qt's <c>QStandardPaths</c> locations KeePassXC 2.7's <c>Config</c> puts its local file in.</summary>
    internal static string? LocalConfigPath(KeePassXcPlatform platform, string? home, string? localAppData, string? xdgCacheHome)
    {
        switch (platform)
        {
            case KeePassXcPlatform.Windows:
                return string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "KeePassXC", "keepassxc.ini");

            case KeePassXcPlatform.MacOS:
                return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Library", "Caches", "KeePassXC", "keepassxc.ini");

            default:
                // Qt ignores a relative XDG_CACHE_HOME, as the XDG specification asks.
                var cache = xdgCacheHome is { Length: > 0 } && xdgCacheHome[0] == '/'
                    ? xdgCacheHome
                    : string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".cache");
                return cache is null ? null : Path.Combine(cache, "keepassxc", "keepassxc.ini");
        }
    }

    /// <summary>The paths the three keys name, in the order they are offered, before any is checked.</summary>
    internal static IReadOnlyList<string> Parse(ReadOnlySpan<byte> bytes)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inGeneral = true;

        foreach (var line in Lines(bytes))
        {
            var text = line.Trim();

            if (text.Length == 0 || text[0] == ';' || text[0] == '#')
            {
                continue;
            }

            if (text[0] == '[')
            {
                if (text[^1] == ']')
                {
                    inGeneral = string.Equals(text[1..^1], "General", StringComparison.Ordinal);
                }

                continue;
            }

            var equals = text.IndexOf('=', StringComparison.Ordinal);

            if (!inGeneral || equals <= 0)
            {
                continue;
            }

            var key = text[..equals].TrimEnd();

            if (Array.IndexOf(_keys, key) >= 0)
            {
                values[key] = Paths(Unescape(text[(equals + 1)..]));
            }
        }

        return [.. _keys.SelectMany(key => values.TryGetValue(key, out var paths) ? paths : [])];
    }

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > MaximumBytes)
            {
                return null;
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Each line that is valid UTF-8; one that is not is skipped and the rest are kept.</summary>
    private static List<string> Lines(ReadOnlySpan<byte> bytes)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var lines = new List<string>();

        if (bytes.StartsWith("﻿"u8))
        {
            bytes = bytes[3..];
        }

        while (!bytes.IsEmpty)
        {
            var end = bytes.IndexOfAny((byte)'\n', (byte)'\r');
            var line = end < 0 ? bytes : bytes[..end];

            try
            {
                lines.Add(strict.GetString(line));
            }
            catch (DecoderFallbackException)
            {
            }

            bytes = end < 0 ? [] : bytes[(end + 1)..];
        }

        return lines;
    }

    /// <summary>A value's items as QSettings' <c>iniUnescapedStringList</c> reads them.</summary>
    private static List<string> Unescape(string value)
    {
        var items = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var inQuotes = false;
        var chopLimit = 0;
        var i = 0;

        SkipSpaces(value, ref i);

        while (i < value.Length)
        {
            var ch = value[i];

            if (ch == '\\')
            {
                i++;

                if (i >= value.Length)
                {
                    break;
                }

                var escaped = value[i++];

                switch (escaped)
                {
                    case 'a': current.Append('\a'); break;
                    case 'b': current.Append('\b'); break;
                    case 'f': current.Append('\f'); break;
                    case 'n': current.Append('\n'); break;
                    case 'r': current.Append('\r'); break;
                    case 't': current.Append('\t'); break;
                    case 'v': current.Append('\v'); break;
                    case '"' or '?' or '\'' or '\\': current.Append(escaped); break;
                    case 'x' when i < value.Length && Digit(value[i], 16) >= 0:
                        current.Append(Code(value, ref i, 16));
                        break;
                    case >= '0' and <= '7':
                        i--;
                        current.Append(Code(value, ref i, 8));
                        break;
                }

                chopLimit = current.Length;
            }
            else if (ch == '"')
            {
                i++;
                quoted = true;
                inQuotes = !inQuotes;

                if (!inQuotes)
                {
                    SkipSpaces(value, ref i);
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                items.Add(Finish(current, quoted, chopLimit));
                current.Clear();
                quoted = false;
                chopLimit = 0;
                i++;
                SkipSpaces(value, ref i);
            }
            else
            {
                current.Append(ch);
                i++;
            }
        }

        items.Add(Finish(current, quoted, chopLimit));
        return items;
    }

    private static void SkipSpaces(string value, ref int i)
    {
        while (i < value.Length && value[i] is ' ' or '\t')
        {
            i++;
        }
    }

    /// <summary>A hex or octal escape's code unit; Qt reads digits until the first that is not one.</summary>
    private static char Code(string value, ref int i, int radix)
    {
        var code = 0;

        while (i < value.Length && Digit(value[i], radix) is >= 0 and var digit)
        {
            code = ((code * radix) + digit) & 0xFFFF;
            i++;
        }

        return (char)code;
    }

    private static int Digit(char ch, int radix)
    {
        var digit = ch switch
        {
            >= '0' and <= '9' => ch - '0',
            >= 'a' and <= 'f' => ch - 'a' + 10,
            >= 'A' and <= 'F' => ch - 'A' + 10,
            _ => -1,
        };

        return digit < radix ? digit : -1;
    }

    private static string Finish(StringBuilder current, bool quoted, int chopLimit)
    {
        var length = current.Length;

        while (!quoted && length > chopLimit && current[length - 1] is ' ' or '\t')
        {
            length--;
        }

        return current.ToString(0, length);
    }

    /// <summary>The items that are paths: <c>@@</c> escapes an <c>@</c>, and any other <c>@</c> value is a type QSettings wrote, never a path.</summary>
    private static List<string> Paths(List<string> items)
    {
        var paths = new List<string>(items.Count);

        foreach (var item in items)
        {
            if (item.StartsWith("@@", StringComparison.Ordinal))
            {
                paths.Add(item[1..]);
            }
            else if (item.Length > 0 && item[0] != '@')
            {
                paths.Add(item);
            }
        }

        return paths;
    }

    private static string? FullPath(string text)
    {
        try
        {
            return Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
