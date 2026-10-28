using System.Text;
using Keypaste.Core.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The strict RFC 4180 reader the CSV export readers share: quoted fields hold commas, quotes and line breaks, each
/// record names the line it starts on, and anything malformed is refused naming its line and never its text.
/// </summary>
public sealed class CsvTests
{
    [Fact]
    public void Quoted_fields_hold_commas_quotes_and_line_breaks_and_each_record_names_its_line()
    {
        var read = Read("a,b,c\r\n\"one, two\",\"say \"\"hi\"\"\",\"first\nsecond\r\nthird\"\r\nx,,\"\"\n");

        Assert.Equal([1, 2, 5], read.Select(record => record.Line));
        Assert.Equal(["a", "b", "c"], read[0].Fields);
        Assert.Equal(["one, two", "say \"hi\"", "first\nsecond\r\nthird"], read[1].Fields);
        Assert.Equal(["x", string.Empty, string.Empty], read[2].Fields);
    }

    [Fact]
    public void A_byte_order_mark_LF_line_breaks_blank_lines_and_a_missing_last_line_break_change_no_field()
    {
        byte[] marked = [0xEF, 0xBB, 0xBF, .. "h1,h2\n\nv1,\"v,2\""u8];

        Assert.True(Csv.TryRead(marked, out var records, out var error), error);
        var plain = Read("h1,h2\r\nv1,\"v,2\"\r\n");

        Assert.Equal(plain.Select(record => record.Fields), records.Select(record => record.Fields));
        Assert.Equal([1, 3], records.Select(record => record.Line));
    }

    [Fact]
    public void A_trailing_comma_is_an_empty_last_field_and_an_empty_file_has_no_records()
    {
        var read = Read("a,b,\n1,2,\n");

        Assert.Equal(["a", "b", string.Empty], read[0].Fields);
        Assert.Equal(["1", "2", string.Empty], read[1].Fields);
        Assert.Empty(Read(string.Empty));
        Assert.True(Csv.TryRead([0xEF, 0xBB, 0xBF], out var records, out _));
        Assert.Empty(records);
    }

    [Theory]
    [InlineData("a,b\nsecret\"x,y\n", "line 2: a field that does not start with a quote holds one")]
    [InlineData("a,b\n\"secret\"x,y\n", "line 2: text follows a quoted field's closing quote")]
    [InlineData("a,b\nx,\"secret\n\nmore", "line 2: a quoted field is not closed")]
    [InlineData("a,b\nsecret\ry,z\n", "line 2: a carriage return is not followed by a line feed")]
    [InlineData("a,b\n\"x\ny\",secret,z\n", "line 2: it has 3 fields, and the first record has 2 fields")]
    [InlineData("a\nsecret,x\n", "line 2: it has 2 fields, and the first record has 1 field")]
    public void Malformed_text_is_refused_naming_its_line_and_never_its_text(string text, string expected)
    {
        Assert.False(Csv.TryRead(Encoding.UTF8.GetBytes(text), out var records, out var error));

        Assert.Equal(expected, error);
        Assert.DoesNotContain("secret", error, StringComparison.Ordinal);
        Assert.Empty(records);
    }

    [Fact]
    public void Bytes_that_are_not_UTF8_are_refused()
    {
        Assert.False(Csv.TryRead([.. "a,b\n"u8, 0xFF, .. ",c\n"u8], out var records, out var error));

        Assert.Equal("the file is not UTF-8 text", error);
        Assert.Empty(records);
    }

    [Fact]
    public void The_first_record_is_read_from_a_files_first_bytes_cut_anywhere_after_its_line()
    {
        byte[] cut = [.. "\"Group\",\"Title\",url\r\nWork,\"unclosed"u8, 0xE2, 0x82];
        byte[] marked = [0xEF, 0xBB, 0xBF, .. "url,name"u8];

        Assert.Equal(["Group", "Title", "url"], Csv.FirstRecord(cut));
        Assert.Equal(["url", "name"], Csv.FirstRecord(marked));
        Assert.Null(Csv.FirstRecord("\"url,name\n"u8));
        Assert.Null(Csv.FirstRecord([0xFF, .. ",name\n"u8]));
        Assert.Null(Csv.FirstRecord([]));
    }

    [Fact]
    public void A_record_shows_its_line_and_never_its_fields()
    {
        Assert.Equal("line 3", new CsvRecord(3, ["secret"]).ToString());
    }

    private static IReadOnlyList<CsvRecord> Read(string text)
    {
        Assert.True(Csv.TryRead(Encoding.UTF8.GetBytes(text), out var records, out var error), error);
        Assert.Equal(string.Empty, error);
        return records;
    }
}
