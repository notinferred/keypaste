using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Keypaste.Core.Import;
using Keypaste.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The strict RFC 4180 reader the CSV export readers share: quoted fields hold commas, quotes and line breaks, each
/// record names the line it starts on, and anything malformed is refused naming its line and never its text.
/// </summary>
public sealed class CsvTests
{
    private static readonly Gen<string> _field = Gen.Choose(1, 3).SelectMany(n => Gen.Frequency(
        (4, Gen.Choose(0, 6).SelectMany(length => Gen.Elements("abz09 .-é".ToCharArray()).ArrayOf(length)).Select(chars => new string(chars))),
        (2, Gen.Elements("", ",", "\"", "\"\"", "\n", "\r\n", "\r", "a,b", "say \"hi\"", "\U0001F511"))).ArrayOf(n)).Select(string.Concat);

    private static readonly Gen<string[][]> _records =
        from columns in Gen.Choose(1, 5)
        from rows in Gen.Choose(0, 6)
        from records in _field.ArrayOf(columns).ArrayOf(rows)
        select records;

    private static readonly Gen<(string[][] Records, Noise Noise)> _files = Gen.Zip(_records, Noise.Any);

    [Fact]
    public void Quoted_or_bare_any_field_reads_back_and_each_record_names_the_line_it_starts_on() =>
        Properties.Hold(_files, test =>
        {
            var (bytes, lines) = Written(test.Records, test.Noise, lastBreak: test.Noise.Pick(false, true));

            Assert.True(Csv.TryRead(bytes, out var records, out var error), error);
            Assert.Equal(string.Empty, error);
            Assert.Equal(test.Records, records.Select(record => record.Fields.ToArray()));
            Assert.Equal(lines, records.Select(record => record.Line));
        });

    [Fact]
    public void Malformed_text_is_refused_naming_its_line_and_never_its_text() =>
        Properties.Hold(_files.Where(test => test.Records.Length > 0), test =>
        {
            var (bytes, error) = Malformed(test.Records, test.Noise);

            Assert.False(Csv.TryRead(bytes, out var records, out var said));
            Assert.Equal(error, said);
            Assert.DoesNotContain("secret", said, StringComparison.Ordinal);
            Assert.Empty(records);
        });

    [Fact]
    public void The_first_record_is_read_from_a_files_first_bytes_cut_anywhere_after_its_line() =>
        Properties.Hold(_files.Where(test => test.Records.Length > 0), test =>
        {
            var (bytes, _) = Written(test.Records, test.Noise, lastBreak: test.Noise.Pick(false, true));
            var lineFeed = Array.IndexOf(bytes, (byte)'\n');
            var cut = lineFeed < 0 ? bytes.Length : lineFeed + 1 + test.Noise.Below(bytes.Length - lineFeed);
            var expected = test.Records[0].Any(field => field.Contains('\n', StringComparison.Ordinal)) ? null : test.Records[0];

            Assert.Equal(expected, Csv.FirstRecord(bytes.AsSpan(0, cut))?.ToArray());

            var broken = bytes.ToList();
            broken.Insert(test.Noise.Below((lineFeed < 0 ? bytes.Length : lineFeed) + 1), 0xFF);
            Assert.Null(Csv.FirstRecord(broken.ToArray()));
        });

    [Fact]
    public void Every_export_reader_refuses_a_file_it_cannot_read_whole_with_the_reason()
    {
        (IExportReader Reader, string[] Header, bool AnyCase)[] readers =
        [
            (new KeePassXcCsvReader(), ["Group", "Title", "Username", "Password", "URL", "Notes"], false),
            (new OnePasswordCsvReader(), ["title", "url", "username", "password", "notes"], true),
            (new LastPassCsvReader(), ["url", "username", "password", "extra", "name", "grouping"], false),
        ];
        var cases =
            from reader in Gen.Elements(readers)
            from count in Gen.Choose(0, 3)
            from rows in _field.ArrayOf(reader.Header.Length).ArrayOf(count)
            from noise in Noise.Any
            select (reader, rows, noise);

        Properties.Hold(cases, test =>
        {
            var (reader, header, anyCase) = test.reader;
            var column = test.noise.Below(header.Length);
            var (malformed, error) = Malformed([header, .. test.rows], test.noise);
            var twice = anyCase ? header[column].ToUpperInvariant() : header[column];

            Assert.Equal(error, Refused(reader, malformed));
            Assert.Equal("it is empty", Refused(reader, test.noise.Pick<byte[]>([], [0xEF, 0xBB, 0xBF], "\n\r\n"u8.ToArray())));
            Assert.Equal($"its header names the {twice} column twice", Refused(reader, Written([[.. header, twice]], test.noise, true).Bytes));
            Assert.Equal($"its header has no {header[column]} column", Refused(reader, Written([[.. header.Where((_, at) => at != column)]], test.noise, true).Bytes));
        });
    }

    [Fact]
    public void A_record_shows_its_line_and_never_its_fields()
    {
        Assert.Equal("line 3", new CsvRecord(3, ["secret"]).ToString());
    }

    private static string Refused(IExportReader reader, byte[] file) => Assert.Throws<ExportFormatException>(() => reader.Read(file)).Message;

    private static (byte[] Bytes, List<int> Lines) Written(string[][] records, Noise noise, bool lastBreak)
    {
        var text = new StringBuilder();
        var lines = new List<int>();

        for (var row = 0; row < records.Length; row++)
        {
            if (row > 0)
            {
                text.Append(noise.Pick("\n", "\r\n"));
                for (var blank = noise.Pick(0, 0, 1, 2); blank > 0; blank--)
                {
                    text.Append(noise.Pick("\n", "\r\n"));
                }
            }

            lines.Add(1 + text.ToString().Count(c => c == '\n'));
            text.Append(Record(records[row], noise));
        }

        if (lastBreak && records.Length > 0)
        {
            text.Append(noise.Pick("\n", "\r\n"));
        }

        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        return (noise.Pick(false, true) ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes, lines);
    }

    private static (byte[] Bytes, string Error) Malformed(string[][] records, Noise noise)
    {
        var columns = records[0].Length;
        var row = noise.Below(records.Length);
        var prefix = Encoding.UTF8.GetString(Written(records[..row], noise, lastBreak: true).Bytes).TrimStart('﻿');
        var line = 1 + prefix.Count(c => c == '\n');
        var rest = new string(',', columns - 1) + "\n";

        var (bad, error) = noise.Below(row > 0 ? 6 : 5) switch
        {
            0 => ("secret\"x" + rest, "a field that does not start with a quote holds one"),
            1 => ("\"secret\"x" + rest, "text follows a quoted field's closing quote"),
            2 => ("\"secret\n\nmore", "a quoted field is not closed"),
            3 => ("secret\ry" + rest, "a carriage return is not followed by a line feed"),
            4 => (string.Empty, string.Empty),
            _ => Mismatched(columns, noise),
        };

        if (bad.Length == 0)
        {
            var bytes = Written(records, noise, lastBreak: true).Bytes.ToList();
            bytes.Insert(noise.Below(bytes.Count + 1), 0xFF);
            return ([.. bytes], "the file is not UTF-8 text");
        }

        return (Encoding.UTF8.GetBytes(prefix + bad), $"line {line}: {error}");
    }

    private static (string Bad, string Error) Mismatched(int columns, Noise noise)
    {
        var count = columns == 1 || noise.Pick(true, false) ? columns + 1 : columns - 1;
        return (string.Join(',', Enumerable.Repeat("x", count)) + "\n", $"it has {Fields(count)}, and the first record has {Fields(columns)}");
    }

    private static string Fields(int count) => count == 1 ? "1 field" : $"{count} fields";

    private static string Record(string[] fields, Noise noise) => string.Join(',', fields.Select(field =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 || (fields.Length == 1 && field.Length == 0) || noise.Pick(false, true)
            ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : field));
}
