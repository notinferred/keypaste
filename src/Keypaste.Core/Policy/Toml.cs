using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Policy;

/// <summary>Which of the three shapes a <see cref="TomlValue"/> holds.</summary>
public enum TomlValueKind
{
    /// <summary>A double-quoted string.</summary>
    Text = 1,

    /// <summary>A non-negative whole number.</summary>
    Number = 2,

    /// <summary>An array of double-quoted strings.</summary>
    Array = 3,
}

/// <summary>One value on the right of an <c>=</c>.</summary>
/// <remarks>
/// Three shapes and no more, because three is what a policy rule is made of. Anything a real TOML
/// document could hold and this cannot — a float, a boolean, a date, a table — is refused by the
/// reader rather than modelled here, so there is no representation for a thing keypaste would then
/// have to decide what to do with.
/// </remarks>
public sealed class TomlValue
{
    private static readonly string[] _noItems = [];

    private TomlValue(TomlValueKind kind, string text, int number, IReadOnlyList<string> items)
    {
        Kind = kind;
        Text = text;
        Number = number;
        Items = items;
    }

    /// <summary>Which shape this is.</summary>
    public TomlValueKind Kind { get; }

    /// <summary>The string, when <see cref="Kind"/> is <see cref="TomlValueKind.Text"/>.</summary>
    public string Text { get; }

    /// <summary>The number, when <see cref="Kind"/> is <see cref="TomlValueKind.Number"/>.</summary>
    public int Number { get; }

    /// <summary>The items, when <see cref="Kind"/> is <see cref="TomlValueKind.Array"/>.</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>Builds a string value.</summary>
    /// <param name="text">The text between the quotes.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static TomlValue OfString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TomlValue(TomlValueKind.Text, text, 0, _noItems);
    }

    /// <summary>Builds an integer value.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The value.</returns>
    public static TomlValue OfInteger(int number) =>
        new(TomlValueKind.Number, string.Empty, number, _noItems);

    /// <summary>Builds an array value.</summary>
    /// <param name="items">The strings, in the order written.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static TomlValue OfArray(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new TomlValue(TomlValueKind.Array, string.Empty, 0, items);
    }

    /// <summary>Names this shape the way an error message would.</summary>
    /// <returns>A short human-readable noun.</returns>
    public string Describe() => Kind switch
    {
        TomlValueKind.Text => "a string",
        TomlValueKind.Number => "a whole number",
        _ => "an array",
    };
}

/// <summary>One <c>key = value</c> pair.</summary>
/// <param name="Key">The bare key, as written.</param>
/// <param name="Value">What it was set to.</param>
/// <param name="Line">The 1-based line it is on.</param>
public sealed record TomlPair(string Key, TomlValue Value, int Line);

/// <summary>One <c>[[name]]</c> section and everything set inside it.</summary>
/// <param name="Name">The bare name between the brackets.</param>
/// <param name="Line">The 1-based line the header is on.</param>
/// <param name="Pairs">The pairs, in the order written.</param>
public sealed record TomlTable(string Name, int Line, IReadOnlyList<TomlPair> Pairs)
{
    /// <summary>Finds a pair by key.</summary>
    /// <param name="key">The bare key.</param>
    /// <param name="pair">The pair, when the key is set.</param>
    /// <returns><see langword="true"/> if the key is set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public bool TryGet(string key, [NotNullWhen(true)] out TomlPair? pair)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var candidate in Pairs)
        {
            if (string.Equals(candidate.Key, key, StringComparison.Ordinal))
            {
                pair = candidate;
                return true;
            }
        }

        pair = null;
        return false;
    }
}

/// <summary>Everything a policy file said, as syntax.</summary>
/// <param name="Tables">The sections, in the order written.</param>
public sealed record TomlDocument(IReadOnlyList<TomlTable> Tables)
{
    /// <summary>A document with nothing in it.</summary>
    public static TomlDocument Empty { get; } = new([]);
}

/// <summary>
/// Reads the strict subset of TOML that a policy file is allowed to be written in.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a TOML parser and does not try to be one.</b> It accepts comments, <c>[[name]]</c>
/// section headers, and <c>key = value</c> where the value is a double-quoted string, a non-negative
/// whole number, or an array of double-quoted strings. Every other construct a real TOML document
/// may contain — dotted keys, inline tables, literal and multi-line strings, floats, booleans,
/// dates, singular <c>[table]</c> headers — is a parse error naming the construct.
/// </para>
/// <para>
/// Hand-rolled rather than taken as a package because <c>Keypaste.Core</c> carries no dependencies
/// at all (CORE.md law 3.9, DECISIONS.md D-0004 and D-0019), and because the strictness is the
/// point rather than a limitation: a policy file is an authorization document, so a construct
/// keypaste would have to guess the meaning of is one it must refuse. The same argument produced
/// <see cref="DotEnv"/>, <see cref="Keypaste.Core.Ipc.MessageFramer"/> and the CLI's own
/// argument parser.
/// </para>
/// <para>
/// <b>There is no branch here that skips a line it did not classify.</b> Every line takes exactly
/// one of five first-character paths and each one either produces a header, produces a pair, or
/// fails — there is no <c>default: continue</c>. That is what makes "it refuses a file it cannot
/// fully understand" a property of the shape of the code rather than a promise about it.
/// </para>
/// <para>
/// <b>One problem is reported, not a list.</b> <see cref="DotEnv"/> collects every problem because
/// the user is triaging an import and wants to fix the file in one pass. A policy file is used whole
/// or ignored whole, so the first thing wrong is the only thing that changes the outcome — and a
/// list of problems invites a caller to filter it, which is exactly what this stage forbids.
/// </para>
/// <para>
/// <b>It knows nothing about rules.</b> Which section names and which keys mean something is the
/// rule layer's business, and an unknown one arrives there intact rather than being dropped here.
/// Keeping the layers apart is what lets the syntax be tested exhaustively without a vault, an
/// entry name or an exposure anywhere in sight.
/// </para>
/// </remarks>
public static class Toml
{
    /// <summary>The largest policy file the reader will accept, in bytes.</summary>
    public const int MaximumBytes = 16 * 1024;

    /// <summary>The most lines a policy file may have.</summary>
    public const int MaximumLines = 512;

    /// <summary>The longest a single line may be, in UTF-16 code units.</summary>
    public const int MaximumLineLength = 256;

    /// <summary>The most sections a policy file may have.</summary>
    public const int MaximumTables = 32;

    /// <summary>The most keys one section may set.</summary>
    public const int MaximumPairs = 8;

    /// <summary>The most items one array may hold.</summary>
    public const int MaximumItems = 16;

    /// <summary>The longest a single string may be, in UTF-16 code units.</summary>
    public const int MaximumStringLength = 128;

    /// <summary>Decodes the bytes of a policy file to text.</summary>
    /// <param name="bytes">The file's contents.</param>
    /// <param name="text">The decoded text, or an empty string on failure.</param>
    /// <param name="error">What went wrong, or an empty string on success.</param>
    /// <returns><see langword="true"/> if the bytes decoded.</returns>
    /// <remarks>
    /// UTF-8 only, with or without a byte order mark. Unlike the <c>.env</c> reader there is no
    /// UTF-16 branch: a <c>.env</c> may well have been written by Windows PowerShell 5.1 and handed
    /// over, whereas a policy file is written by the person running keypaste, told to write UTF-8,
    /// and safest refused when it is something else.
    /// </remarks>
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text, out string error)
    {
        text = string.Empty;

        if (bytes.Length > MaximumBytes)
        {
            error = $"the file is larger than {MaximumBytes / 1024} KiB, which is not a policy file";
            return false;
        }

        // Written out rather than compared against a literal array: a constant array passed as an
        // argument is a build error here (CA1861).
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            error = string.Empty;
            return true;
        }
        catch (DecoderFallbackException)
        {
            error = "the file is not valid UTF-8 text";
            return false;
        }
    }

    /// <summary>Parses the text of a policy file.</summary>
    /// <param name="text">The decoded file contents.</param>
    /// <param name="document">The sections found, or <see cref="TomlDocument.Empty"/> on failure.</param>
    /// <param name="error">
    /// The first problem, in the shape <c>line 7: ...</c>, or an empty string on success.
    /// </param>
    /// <returns><see langword="true"/> only when the whole file was understood.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static bool TryParse(string text, out TomlDocument document, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        document = TomlDocument.Empty;
        error = string.Empty;

        var lines = Normalize(text).Split('\n');

        if (lines.Length > MaximumLines)
        {
            error = $"the file has more than {MaximumLines} lines";
            return false;
        }

        var tables = new List<TomlTable>();
        List<TomlPair>? pairs = null;
        var name = string.Empty;
        var headerLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var number = i + 1;

            if (line.Length > MaximumLineLength)
            {
                error = Problem(number, $"the line is longer than {MaximumLineLength} characters");
                return false;
            }

            var at = SkipSpaces(line, 0);

            if (at == line.Length || line[at] == '#')
            {
                continue;
            }

            if (line[at] == '[')
            {
                // Counting what is already open rather than what has been flushed: the section
                // being built has not reached the list yet, and the cap is on sections written,
                // not on sections finished.
                if (tables.Count + (pairs is null ? 0 : 1) == MaximumTables)
                {
                    error = Problem(number, $"at most {MaximumTables} sections are allowed");
                    return false;
                }

                if (!TryReadHeader(line, at, number, out var next, out error))
                {
                    return false;
                }

                if (pairs is not null)
                {
                    tables.Add(new TomlTable(name, headerLine, pairs));
                }

                name = next;
                headerLine = number;
                pairs = [];
                continue;
            }

            if (pairs is null)
            {
                error = Problem(number, "this setting is not inside any [[section]]");
                return false;
            }

            if (!TryReadPair(line, at, number, out var pair, out error))
            {
                return false;
            }

            foreach (var seen in pairs)
            {
                if (string.Equals(seen.Key, pair.Key, StringComparison.Ordinal))
                {
                    error = Problem(number, $"'{pair.Key}' is set twice in the same section");
                    return false;
                }
            }

            if (pairs.Count == MaximumPairs)
            {
                error = Problem(number, $"a section may set at most {MaximumPairs} keys");
                return false;
            }

            pairs.Add(pair);
        }

        if (pairs is not null)
        {
            tables.Add(new TomlTable(name, headerLine, pairs));
        }

        document = new TomlDocument(tables);
        return true;
    }

    /// <summary>Collapses CRLF to LF so no rule below has to mention a carriage return.</summary>
    private static string Normalize(string text)
    {
        if (text.StartsWith(DotEnv.ByteOrderMark))
        {
            text = text[1..];
        }

        return text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal)
            : text;
    }

    /// <summary>Reads a <c>[[name]]</c> header, which must be the whole line bar a comment.</summary>
    private static bool TryReadHeader(string line, int at, int number, out string name, out string error)
    {
        name = string.Empty;

        if (at + 1 >= line.Length || line[at + 1] != '[')
        {
            error = Problem(number, "a [section] header is not allowed here; write [[section]]");
            return false;
        }

        var start = at + 2;
        var end = start;

        while (end < line.Length && IsBareKeyCharacter(line[end]))
        {
            end++;
        }

        if (end == start)
        {
            error = Problem(number, "a section header must name a section");
            return false;
        }

        if (end + 1 >= line.Length || line[end] != ']' || line[end + 1] != ']')
        {
            error = Problem(number, "a section header must end with ]]");
            return false;
        }

        name = line[start..end];
        return TryFinishLine(line, end + 2, number, out error);
    }

    /// <summary>Reads one <c>key = value</c> pair.</summary>
    private static bool TryReadPair(
        string line,
        int at,
        int number,
        [NotNullWhen(true)] out TomlPair? pair,
        out string error)
    {
        pair = null;

        if (line[at] is '"' or '\'')
        {
            error = Problem(number, "a quoted key is not allowed; write the name on its own");
            return false;
        }

        var start = at;
        while (at < line.Length && IsBareKeyCharacter(line[at]))
        {
            at++;
        }

        if (at == start)
        {
            error = Problem(number, "expected a setting, a [[section]] header, or a # comment");
            return false;
        }

        var key = line[start..at];

        if (at < line.Length && line[at] == '.')
        {
            error = Problem(number, "a dotted key is not allowed");
            return false;
        }

        at = SkipSpaces(line, at);

        if (at == line.Length || line[at] != '=')
        {
            error = Problem(number, $"expected '=' after '{key}'");
            return false;
        }

        at = SkipSpaces(line, at + 1);

        if (!TryReadValue(line, at, number, out var value, out at, out error))
        {
            return false;
        }

        if (!TryFinishLine(line, at, number, out error))
        {
            return false;
        }

        pair = new TomlPair(key, value, number);
        return true;
    }

    /// <summary>
    /// Reads a value, dispatching on its first character. Every branch either produces a value or
    /// fails; there is deliberately no fall-through.
    /// </summary>
    private static bool TryReadValue(
        string line,
        int at,
        int number,
        [NotNullWhen(true)] out TomlValue? value,
        out int end,
        out string error)
    {
        value = null;
        end = at;

        if (at == line.Length)
        {
            error = Problem(number, "the value is missing");
            return false;
        }

        switch (line[at])
        {
            case '"':
                if (!TryReadString(line, at, number, out var text, out end, out error))
                {
                    return false;
                }

                value = TomlValue.OfString(text);
                return true;

            case '[':
                return TryReadArray(line, at, number, out value, out end, out error);

            case '\'':
                error = Problem(number, "a literal string is not allowed; use double quotes");
                return false;

            case '{':
                error = Problem(number, "an inline table is not allowed");
                return false;

            case 't':
            case 'f':
                error = Problem(number, "true and false are not allowed here");
                return false;

            case '-':
            case '+':
                error = Problem(number, "a signed number is not allowed; write a whole number");
                return false;

            default:
                if (!char.IsAsciiDigit(line[at]))
                {
                    error = Problem(
                        number,
                        "expected a \"string\", a whole number, or [\"an\", \"array\"]");
                    return false;
                }

                if (!TryReadInteger(line, at, number, out var read, out end, out error))
                {
                    return false;
                }

                value = TomlValue.OfInteger(read);
                return true;
        }
    }

    /// <summary>Reads a double-quoted string, which may not span lines and carries no escapes.</summary>
    /// <remarks>
    /// A backslash is refused rather than interpreted. Nothing a policy file holds — a glob, a field
    /// name, a client label — can legitimately contain one; <see cref="EntryExposure"/> already
    /// rejects it inside a pattern, and an escape sequence is a second way to write a character,
    /// which on a file whose whole job is to be read literally is a way to write one thing and mean
    /// another.
    /// </remarks>
    private static bool TryReadString(string line, int at, int number, out string text, out int end, out string error)
    {
        text = string.Empty;
        end = at;

        if (at + 2 < line.Length && line[at + 1] == '"' && line[at + 2] == '"')
        {
            error = Problem(number, "a multi-line string is not allowed");
            return false;
        }

        var start = at + 1;
        var scan = start;

        while (scan < line.Length && line[scan] != '"')
        {
            if (line[scan] == '\\')
            {
                error = Problem(number, "a backslash is not allowed inside a value");
                return false;
            }

            scan++;
        }

        if (scan == line.Length)
        {
            error = Problem(number, "the string is never closed");
            return false;
        }

        if (scan - start > MaximumStringLength)
        {
            error = Problem(number, $"a value may be at most {MaximumStringLength} characters");
            return false;
        }

        text = line[start..scan];
        end = scan + 1;
        error = string.Empty;
        return true;
    }

    /// <summary>Reads a non-negative whole number in plain decimal and nothing else.</summary>
    private static bool TryReadInteger(string line, int at, int number, out int read, out int end, out string error)
    {
        read = 0;

        var start = at;
        while (at < line.Length && char.IsAsciiDigit(line[at]))
        {
            at++;
        }

        end = at;
        var digits = line[start..at];

        if (at < line.Length && (line[at] is '-' or ':' or '.'))
        {
            error = Problem(number, "dates, times and decimals are not allowed");
            return false;
        }

        if (at < line.Length && (line[at] is '_' or 'x' or 'o' or 'b' or 'e' or 'E'))
        {
            error = Problem(number, "only plain decimal digits are allowed in a number");
            return false;
        }

        if (digits.Length > 1 && digits[0] == '0')
        {
            error = Problem(number, "a number may not start with a zero");
            return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out read))
        {
            error = Problem(number, "the number is too large");
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Reads an array of strings, which must fit on one line.</summary>
    private static bool TryReadArray(
        string line,
        int at,
        int number,
        [NotNullWhen(true)] out TomlValue? value,
        out int end,
        out string error)
    {
        value = null;
        end = at;

        var items = new List<string>();
        at = SkipSpaces(line, at + 1);

        while (true)
        {
            if (at == line.Length)
            {
                error = Problem(number, "an array must fit on one line");
                return false;
            }

            if (line[at] == ']')
            {
                end = at + 1;
                value = TomlValue.OfArray(items);
                error = string.Empty;
                return true;
            }

            if (line[at] != '"')
            {
                error = Problem(number, "an array may only contain \"strings\"");
                return false;
            }

            if (items.Count == MaximumItems)
            {
                error = Problem(number, $"an array may hold at most {MaximumItems} items");
                return false;
            }

            if (!TryReadString(line, at, number, out var text, out at, out error))
            {
                return false;
            }

            items.Add(text);
            at = SkipSpaces(line, at);

            if (at < line.Length && line[at] == ',')
            {
                at = SkipSpaces(line, at + 1);

                if (at < line.Length && line[at] == ']')
                {
                    error = Problem(number, "a trailing comma is not allowed");
                    return false;
                }

                continue;
            }

            if (at == line.Length || line[at] != ']')
            {
                error = Problem(number, "expected ',' or ']' in the array");
                return false;
            }
        }
    }

    /// <summary>Requires that nothing but whitespace and a comment follows.</summary>
    private static bool TryFinishLine(string line, int at, int number, out string error)
    {
        at = SkipSpaces(line, at);

        if (at != line.Length && line[at] != '#')
        {
            error = Problem(number, "there is text after the value");
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static int SkipSpaces(string line, int at)
    {
        while (at < line.Length && (line[at] is ' ' or '\t'))
        {
            at++;
        }

        return at;
    }

    private static bool IsBareKeyCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    private static string Problem(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"line {line}: {message}");
}
