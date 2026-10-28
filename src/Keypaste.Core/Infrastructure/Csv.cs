using System.Globalization;

namespace Keypaste.Core.Infrastructure;

/// <summary>One record of a CSV file: one line, or several when a quoted field holds a line break.</summary>
/// <param name="Line">The line it starts on, counted from 1.</param>
/// <param name="Fields">Its fields, in order.</param>
public sealed record CsvRecord(int Line, IReadOnlyList<string> Fields)
{
    /// <summary>Where it starts, never what it holds, so a log line or a debugger shows no value.</summary>
    /// <returns><c>line</c> and its number.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"line {Line}");
}

/// <summary>A strict reader of comma-separated values as RFC 4180 defines them, for the files other programs export (D-0573).</summary>
/// <remarks>
/// The bytes are UTF-8, after an optional byte order mark. A record ends at CRLF or LF, the last one optionally; a field
/// is bare, holding no quote, CR or LF, or quoted, where <c>""</c> is a quote and a comma or line break is text; and
/// every record has as many fields as the first. A line with nothing on it is no record. Anything else is refused,
/// naming its line and never its text, rather than guessed at. The decoded text is zeroed once read; the fields are
/// strings, which cannot be.
/// </remarks>
public static class Csv
{
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Reads every record of a file.</summary>
    /// <param name="bytes">The file.</param>
    /// <param name="records">Its records in order, empty on failure.</param>
    /// <param name="error">The first problem, as <c>line 7: …</c>, or empty on success.</param>
    /// <returns><see langword="true"/> when the whole file was read.</returns>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out IReadOnlyList<CsvRecord> records, out string error)
    {
        records = [];

        if (Decode(bytes) is not { } text)
        {
            error = "the file is not UTF-8 text";
            return false;
        }

        try
        {
            var reader = new Reader(text);
            List<CsvRecord> read = [];

            while (reader.TryNext(out var record, out error))
            {
                if (record is null)
                {
                    records = read;
                    return true;
                }

                if (read.Count > 0 && record.Fields.Count != read[0].Fields.Count)
                {
                    error = Problem(record.Line, $"it has {FieldCount(record.Fields.Count)}, and the first record has {FieldCount(read[0].Fields.Count)}");
                    return false;
                }

                read.Add(record);
            }

            return false;
        }
        finally
        {
            Array.Clear(text);
        }
    }

    /// <summary>The fields of a file's first record, read from its first bytes up to the first line feed, to recognize its format by.</summary>
    /// <param name="start">The file's first bytes, or all of it.</param>
    /// <returns>The fields, or null when those bytes do not begin with one record.</returns>
    public static IReadOnlyList<string>? FirstRecord(ReadOnlySpan<byte> start)
    {
        var lineFeed = start.IndexOf((byte)'\n');

        if (Decode(lineFeed < 0 ? start : start[..(lineFeed + 1)]) is not { } text)
        {
            return null;
        }

        try
        {
            var reader = new Reader(text);
            return reader.TryNext(out var record, out _) ? record?.Fields : null;
        }
        finally
        {
            Array.Clear(text);
        }
    }

    private static char[]? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(ByteOrderMark))
        {
            bytes = bytes[ByteOrderMark.Length..];
        }

        try
        {
            var text = new char[_utf8.GetCharCount(bytes)];
            _utf8.GetChars(bytes, text);
            return text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string Problem(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"line {line}: {message}");

    private static string FieldCount(int count) =>
        count == 1 ? "1 field" : string.Create(CultureInfo.InvariantCulture, $"{count} fields");

    private ref struct Reader(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _at;
        private int _line = 1;

        /// <summary>Reads the next record, or gives null at the end of the text.</summary>
        public bool TryNext(out CsvRecord? record, out string error)
        {
            record = null;
            error = string.Empty;

            while (LineBreak() is > 0 and var blank)
            {
                _at += blank;
                _line++;
            }

            if (_at == _text.Length)
            {
                return true;
            }

            var first = _line;
            List<string> fields = [];

            while (true)
            {
                if (!TryField(fields, out error))
                {
                    return false;
                }

                if (_at == _text.Length)
                {
                    break;
                }

                if (_text[_at] == ',')
                {
                    _at++;
                    continue;
                }

                if (LineBreak() is > 0 and var length)
                {
                    _at += length;
                    _line++;
                    break;
                }

                error = Problem(_line, _text[_at] == '\r' ? "a carriage return is not followed by a line feed" : "text follows a quoted field's closing quote");
                return false;
            }

            record = new CsvRecord(first, fields);
            return true;
        }

        private bool TryField(List<string> fields, out string error)
        {
            error = string.Empty;

            if (_at < _text.Length && _text[_at] == '"')
            {
                var opened = _line;
                var start = _at + 1;
                var escaped = false;

                for (var index = start; index < _text.Length; index++)
                {
                    if (_text[index] == '\n')
                    {
                        _line++;
                    }
                    else if (_text[index] == '"' && index + 1 < _text.Length && _text[index + 1] == '"')
                    {
                        escaped = true;
                        index++;
                    }
                    else if (_text[index] == '"')
                    {
                        var value = _text[start..index].ToString();
                        fields.Add(escaped ? value.Replace("\"\"", "\"", StringComparison.Ordinal) : value);
                        _at = index + 1;
                        return true;
                    }
                }

                error = Problem(opened, "a quoted field is not closed");
                return false;
            }

            var end = _at;

            while (end < _text.Length && _text[end] is not (',' or '\r' or '\n'))
            {
                if (_text[end] == '"')
                {
                    error = Problem(_line, "a field that does not start with a quote holds one");
                    return false;
                }

                end++;
            }

            fields.Add(_text[_at..end].ToString());
            _at = end;
            return true;
        }

        /// <summary>The length of the CRLF or LF at the current place, or 0.</summary>
        private readonly int LineBreak() =>
            _at < _text.Length && _text[_at] == '\n' ? 1
            : _at + 1 < _text.Length && _text[_at] == '\r' && _text[_at + 1] == '\n' ? 2
            : 0;
    }
}
