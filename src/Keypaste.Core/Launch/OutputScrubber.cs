using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Keypaste.Core.Launch;

/// <summary>A stream's output with every known form of every injected value replaced.</summary>
/// <param name="Text">What may be returned to the agent.</param>
/// <param name="Replacements">How many occurrences were replaced.</param>
/// <param name="Truncated">Whether the start of the stream is missing from <paramref name="Text"/>.</param>
public sealed record ScrubbedText(string Text, int Replacements, bool Truncated);

/// <summary>
/// Removes injected values from what a run printed before any of it is returned (D-0359).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bytes, before decoding.</b> A child on Windows may print in its ANSI or OEM code page, or in
/// UTF-16; decoded as UTF-8 first, a value with one non-ASCII character or every other byte zero would
/// match nothing. So every form is matched as bytes in UTF-8, UTF-16LE, UTF-16BE and, on Windows, the
/// two code pages, and the text is decoded as UTF-8 afterwards.
/// </para>
/// <para>
/// <b>The forms</b> are each value as it is and as the common dumps of an environment escape it: JSON
/// from several encoders with <c>\u</c> hex in either case and <c>/</c> as it is or as <c>\/</c>, C-style
/// backslash escapes with neither, either or both quotes escaped, POSIX single quotes, bash double quotes, percent-encoding in either case with <c>%20</c>
/// or <c>+</c>, a URI's userinfo password, and each line of 8 or more characters of a multi-line value,
/// all again with <c>\n</c> as <c>\r\n</c>. Anything else — base64, a reversed or split value, a
/// substring, a file, the network — is not caught; the person's approval of the exact command is the
/// control (THREATS.md T-35).
/// </para>
/// <para>
/// <b>Every occurrence of every form is covered</b>, overlaps included, and each covered run of bytes
/// is replaced by <c>[keypaste:NAME]</c> once, in one pass over the original bytes: a marker is never
/// scanned again, and no part of an overlapping value survives because a shorter one started first.
/// Every non-empty value is scrubbed however short; a one-character value mangles output rather than
/// leaking.
/// </para>
/// <para>
/// A stream whose start was dropped while it ran also loses whatever of its first
/// <see cref="LongestForm"/> − 1 bytes no match covers, so no tail of a value cut in half survives the
/// cut, and a value that starts after it is still replaced whole.
/// </para>
/// </remarks>
public sealed class OutputScrubber
{
    /// <summary>The most characters of a stream returned, from its end.</summary>
    public const int ReturnedCharacters = 16384;

    /// <summary>The shortest line of a multi-line value scrubbed on its own.</summary>
    public const int MinimumLineLength = 8;

    private readonly Dictionary<byte, Pattern[]> _byFirstByte;

    private OutputScrubber(IReadOnlyList<Pattern> patterns)
    {
        _byFirstByte = patterns
            .GroupBy(pattern => pattern.Bytes[0])
            .ToDictionary(group => group.Key, group => group.OrderByDescending(pattern => pattern.Bytes.Length).ToArray());
        LongestForm = patterns.Count == 0 ? 0 : patterns.Max(pattern => pattern.Bytes.Length);
    }

    /// <summary>The longest form of any value, in bytes.</summary>
    public int LongestForm { get; }

    /// <summary>Builds the scrubber for a run's variables.</summary>
    /// <param name="injected">The variables the child was given.</param>
    /// <returns>A scrubber matching every form of every non-empty value.</returns>
    public static OutputScrubber For(IReadOnlyList<EnvVariable> injected)
    {
        ArgumentNullException.ThrowIfNull(injected);

        var encodings = Encodings();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new List<Pattern>();

        foreach (var variable in injected.Where(variable => variable.Value.Length > 0))
        {
            var marker = Encoding.UTF8.GetBytes($"[keypaste:{variable.Key}]");

            foreach (var form in Forms(variable.Value))
            {
                foreach (var encoding in encodings)
                {
                    if (TryEncode(encoding, form, out var bytes) && bytes.Length > 0
                        && seen.Add(variable.Key + "\0" + Convert.ToHexString(bytes)))
                    {
                        patterns.Add(new Pattern(bytes, marker));
                    }
                }
            }
        }

        return new OutputScrubber(patterns);
    }

    /// <summary>Scrubs one stream, keeps its end, and says so when anything before it is missing.</summary>
    /// <param name="stream">What the stream printed.</param>
    /// <returns>The text that may be returned.</returns>
    public ScrubbedText Scrub(CapturedOutput stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Matched first, dropped second: a value that starts after the cut is replaced whole, and what is
        // left unmatched of the first LongestForm - 1 bytes may be the tail of one that started before it.
        var dropped = stream.HeadCut ? Math.Max(0, LongestForm - 1) : 0;
        var (scrubbed, replacements) = Replace(stream.Bytes, dropped);
        var start = 0;

        while (stream.HeadCut && start < scrubbed.Length && (scrubbed[start] & 0xC0) == 0x80)
        {
            start++;
        }

        var text = Encoding.UTF8.GetString(scrubbed, start, scrubbed.Length - start);
        var truncated = stream.HeadCut;

        if (text.Length > ReturnedCharacters)
        {
            text = text[^ReturnedCharacters..];

            if (text.Length > 0 && char.IsLowSurrogate(text[0]))
            {
                text = text[1..];
            }

            truncated = true;
        }

        if (truncated)
        {
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"[keypaste: this stream carried {stream.TotalBytes} bytes; only its end is shown]\n") + text;
        }

        return new ScrubbedText(text, replacements, truncated);
    }

    /// <summary>Covers every occurrence of every pattern, then writes each covered run as its markers, leaving out uncovered bytes before <paramref name="dropped"/>.</summary>
    private (byte[] Bytes, int Replacements) Replace(ReadOnlySpan<byte> input, int dropped)
    {
        var ends = new int[input.Length];
        var markers = new byte[input.Length][];
        var replacements = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (!_byFirstByte.TryGetValue(input[i], out var candidates))
            {
                continue;
            }

            foreach (var pattern in candidates)
            {
                if (input[i..].StartsWith(pattern.Bytes))
                {
                    ends[i] = i + pattern.Bytes.Length;
                    markers[i] = pattern.Marker;
                    break;
                }
            }
        }

        var output = new List<byte>(input.Length);
        var position = 0;

        while (position < input.Length)
        {
            if (ends[position] == 0)
            {
                if (position >= dropped)
                {
                    output.Add(input[position]);
                }

                position++;
                continue;
            }

            // One marker per match that carries the covered run further; a match nested inside it adds nothing.
            var end = ends[position];
            var last = markers[position];
            output.AddRange(last);
            replacements++;

            for (var i = position + 1; i < end; i++)
            {
                if (ends[i] <= end)
                {
                    continue;
                }

                end = ends[i];

                if (!ReferenceEquals(markers[i], last))
                {
                    last = markers[i];
                    output.AddRange(last);
                    replacements++;
                }
            }

            position = end;
        }

        return ([.. output], replacements);
    }

    /// <summary>Every text form of one value that a dump of an environment commonly prints.</summary>
    private static HashSet<string> Forms(string value)
    {
        List<string> bases = [value];

        if (value.Contains('\n', StringComparison.Ordinal))
        {
            bases.AddRange(value.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length >= MinimumLineLength));
        }

        if (Password(value) is { } password)
        {
            bases.Add(password);
            bases.Add(Uri.UnescapeDataString(password));
        }

        var forms = new HashSet<string>(StringComparer.Ordinal);

        foreach (var text in bases.SelectMany(LineEndings))
        {
            forms.Add(text);

            // PHP's json_encode also writes '/' as '\/'.
            foreach (var json in new[]
            {
                JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value,
                JsonEncodedText.Encode(text).Value,
                Json(text, asciiOnly: false, upper: false),
                Json(text, asciiOnly: false, upper: true),
                Json(text, asciiOnly: true, upper: false),
                Json(text, asciiOnly: true, upper: true),
            })
            {
                forms.Add(json);
                forms.Add(json.Replace("/", "\\/", StringComparison.Ordinal));
            }

            // Node quotes a string holding both quotes with backticks and escapes neither.
            forms.Add(Backslashed(text, single: false, @double: false));
            forms.Add(Backslashed(text, single: true, @double: false));
            forms.Add(Backslashed(text, single: false, @double: true));
            forms.Add(Backslashed(text, single: true, @double: true));
            forms.Add(text.Replace("'", "'\\''", StringComparison.Ordinal));
            forms.Add(BashDoubleQuoted(text));

            var escaped = Uri.EscapeDataString(text);

            foreach (var percent in new[] { escaped, Unreserved(escaped, "!'()*"), escaped.Replace("%2F", "/", StringComparison.Ordinal) })
            {
                forms.Add(percent);
                forms.Add(percent.Replace("%20", "+", StringComparison.Ordinal));
                forms.Add(LowerHex(percent, '%', 2));
                forms.Add(LowerHex(percent, '%', 2).Replace("%20", "+", StringComparison.Ordinal));
            }
        }

        forms.RemoveWhere(form => form.Length == 0);
        return forms;
    }

    private static IEnumerable<string> LineEndings(string text)
    {
        yield return text;

        if (text.Contains('\n', StringComparison.Ordinal))
        {
            yield return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        }
    }

    /// <summary>The password of a URI with userinfo, as written in it, or null.</summary>
    private static string? Password(string value)
    {
        var scheme = value.IndexOf("://", StringComparison.Ordinal);

        if (scheme <= 0)
        {
            return null;
        }

        var authority = value[(scheme + 3)..];
        var end = authority.IndexOfAny(['/', '?', '#']);
        authority = end < 0 ? authority : authority[..end];

        var at = authority.LastIndexOf('@');
        var colon = at < 0 ? -1 : authority.IndexOf(':', StringComparison.Ordinal);

        return colon >= 0 && colon < at - 1 ? authority[(colon + 1)..at] : null;
    }

    /// <summary>JSON string content as encoders print it: quotes, backslashes and controls escaped; non-ASCII as <c>\u</c> when asked.</summary>
    private static string Json(string text, bool asciiOnly, bool upper)
    {
        var json = new StringBuilder(text.Length);
        var hex = upper ? "X4" : "x4";

        foreach (var c in text)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                default:
                    if (c < 0x20 || (asciiOnly && c > 0x7E))
                    {
                        json.Append("\\u").Append(((int)c).ToString(hex, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        json.Append(c);
                    }

                    break;
            }
        }

        return json.ToString();
    }

    /// <summary>A C, Python or JavaScript string literal's content: backslash, line breaks and the chosen quotes escaped.</summary>
    private static string Backslashed(string text, bool single, bool @double)
    {
        var escaped = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            escaped.Append(c switch
            {
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\'' when single => "\\'",
                '"' when @double => "\\\"",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    /// <summary>A bash double-quoted string's content, as <c>declare -p</c> and <c>export -p</c> print it.</summary>
    private static string BashDoubleQuoted(string text)
    {
        var escaped = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (c is '\\' or '"' or '$' or '`')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    private static string Unreserved(string escaped, string characters)
    {
        foreach (var c in characters)
        {
            escaped = escaped.Replace("%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture), c.ToString(), StringComparison.Ordinal);
        }

        return escaped;
    }

    private static string LowerHex(string text, char introducer, int digits)
    {
        var lowered = text.ToCharArray();

        for (var i = 0; i + digits < lowered.Length; i++)
        {
            if (lowered[i] == introducer)
            {
                for (var j = 1; j <= digits; j++)
                {
                    lowered[i + j] = char.ToLowerInvariant(lowered[i + j]);
                }

                i += digits;
            }
        }

        return new string(lowered);
    }

    private static List<Encoding> Encodings()
    {
        List<Encoding> encodings =
        [
            new UTF8Encoding(false, throwOnInvalidBytes: true),
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
            new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
        ];

        if (OperatingSystem.IsWindows())
        {
            foreach (var page in CodePages())
            {
                if (CodePagesEncodingProvider.Instance.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) is { } encoding)
                {
                    encodings.Add(encoding);
                }
            }
        }

        return encodings;
    }

    private static IEnumerable<int> CodePages()
    {
        int ansi, oem;

        try
        {
            ansi = (int)GetACP();
            oem = (int)GetOEMCP();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            yield break;
        }

        yield return ansi;

        if (oem != ansi)
        {
            yield return oem;
        }
    }

    private static bool TryEncode(Encoding encoding, string form, out byte[] bytes)
    {
        try
        {
            bytes = encoding.GetBytes(form);
            return true;
        }
        catch (EncoderFallbackException)
        {
            bytes = [];
            return false;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetOEMCP();

    private sealed record Pattern(byte[] Bytes, byte[] Marker);
}
