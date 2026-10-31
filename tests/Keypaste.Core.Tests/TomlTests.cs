using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Keypaste.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The strict TOML subset keypaste reads and writes: what the writer writes reads back as written, layout never changes
/// a parse, every construct the reader refuses fails naming its line, and every limit holds exactly.
/// </summary>
/// <remarks>
/// A policy or clients file is an authorization document, so anything keypaste would have to guess the meaning of is a
/// refusal: <see cref="_refused"/> names each such construct, and the generated files it lands in stay inside every limit.
/// </remarks>
public sealed class TomlTests
{
    private static readonly string[] _refused =
    [
        "client.name = \"x\"", "\"client\" = \"x\"", "client \"x\"", "client =", "dup = \"a\"\ndup = \"b\"",
        "[allow]", "[[allow]", "[[]]", "[[allow]] junk", "[[allow.inner]]",
        "client = 'x'", "client = \"\"\"x\"\"\"", "client = \"x", "client = \"a\\tb\"", "client = \"a\" junk",
        "ttl = -1", "ttl = +1", "ttl = 300.0", "ttl = 3_600", "ttl = 0x10", "ttl = 0o17", "ttl = 1e3", "ttl = 0300",
        "ttl = 99999999999", "enabled = true", "enabled = false", "written = 2026-07-26", "written = 14:03:11",
        "rule = { client = \"x\" }", "entries = [\"a\",]", "entries = [[\"a\"]]", "entries = [1, 2]", "entries = [\"a\"",
        "entries = [\"a\" \"b\"]", "entries = [",
    ];

    private static readonly string[] _units = ["a", "Z", "0", " ", "#", "[", "]", "=", ",", "'", "{", "}", ".", "é", "\U0001F511"];

    private static readonly Gen<string> _bare = Chars("abcxyzABZ019_-", 1, 12);

    private static readonly Gen<TomlValue> _value = Gen.OneOf(
        Text(40).Select(TomlValue.OfString),
        Gen.OneOf(Gen.Choose(0, 1_000_000), Gen.Elements(0, 9, 10, int.MaxValue)).Select(TomlValue.OfInteger),
        Gen.Choose(0, 4).SelectMany(n => Text(24).ArrayOf(n)).Select(items => TomlValue.OfArray(items)));

    private static readonly Gen<Section[]> _sections = Gen.Choose(0, 6).SelectMany(n => (
        from name in _bare
        from keys in Gen.Choose(0, 5).SelectMany(count => _bare.ArrayOf(count))
        let distinct = keys.Distinct(StringComparer.Ordinal).ToArray()
        from values in _value.ArrayOf(distinct.Length)
        select new Section(name, [.. distinct.Zip(values, (key, value) => new Pair(key, value))])).ArrayOf(n));

    private static readonly Gen<TomlLimits> _limits = Gen.OneOf(
        Gen.Elements(TomlLimits.Policy, TomlLimits.Paths),
        from lines in Gen.Choose(12, 40)
        from length in Gen.Choose(1, 60)
        from tables in Gen.Choose(1, 8)
        from pairs in Gen.Choose(1, 8)
        from items in Gen.Choose(1, 8)
        from kib in Gen.Choose(1, 4)
        select new TomlLimits
        {
            Bytes = kib * 1024,
            Lines = lines,
            LineLength = length + (items * 6) + 20,
            Tables = tables,
            Pairs = pairs,
            Items = items,
            StringLength = length,
        });

    [Fact]
    public void What_the_writer_writes_reads_back_as_written_through_its_bytes() =>
        Properties.Hold(Gen.Zip(_sections, Noise.Any), test =>
        {
            var (text, expected) = Written(test.Item1, test.Item2);

            Assert.True(Toml.TryDecode(Encoding.UTF8.GetBytes(text), out var decoded, out var error), error);
            Assert.Equal(text, decoded);
            Assert.Equal(expected, Parsed(decoded, test.Item1));
        });

    [Fact]
    public void Spacing_comments_blank_lines_crlf_and_a_byte_order_mark_change_no_parse() =>
        Properties.Hold(Gen.Zip(_sections, Noise.Any), test =>
        {
            var (text, expected) = Laid(test.Item1, test.Item2);
            var bytes = Encoding.UTF8.GetBytes(text);

            Assert.Equal(expected, Parsed(text, test.Item1));
            Assert.True(Toml.TryDecode(bytes, out var decoded, out var error), error);
            Assert.Equal(text.StartsWith('﻿') ? text[1..] : text, decoded);
            Assert.Equal(string.Empty, error);

            var broken = bytes.ToList();
            broken.Insert(test.Item2.Below(bytes.Length + 1), 0xFF);
            Assert.False(Toml.TryDecode(broken.ToArray(), out decoded, out error));
            Assert.Empty(decoded);
            Assert.Equal("the file is not valid UTF-8 text", error);
        });

    [Fact]
    public void Every_construct_the_reader_refuses_fails_the_whole_file_naming_its_line()
    {
        var cases =
            from sections in _sections.Where(sections => sections.Length > 0)
            from refused in Gen.Elements<(string Text, bool Outside)>([.. _refused.Select(text => (text, false)), ("client = \"x\"", true)])
            from limits in Gen.Elements(TomlLimits.Policy, TomlLimits.Paths)
            from noise in Noise.Any
            select (sections, refused, limits, noise);

        Properties.Hold(cases, test =>
        {
            var lines = Written(test.sections, test.noise).Text.Split('\n')[..^1].ToList();
            var header = lines.FindIndex(line => line.StartsWith("[[", StringComparison.Ordinal));
            var at = test.refused.Outside ? test.noise.Below(header + 1) : header + 1 + test.noise.Below(lines.Count - header);
            var inserted = test.refused.Text.Split('\n');
            lines.InsertRange(at, inserted);

            Assert.False(Toml.TryParse(string.Join('\n', lines), test.limits, out var document, out var error), test.refused.Text);
            Assert.Empty(document.Tables);
            Assert.StartsWith($"line {at + inserted.Length}: ", error, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Each_limit_takes_exactly_its_number_and_refuses_one_more() =>
        Properties.Hold(_limits, limits =>
        {
            Takes(limits, Lines("#", limits.Lines), 0);
            Refuses(limits, Lines("#", limits.Lines + 1), $"the file has more than {limits.Lines} lines");

            Takes(limits, "[[s]]\n#" + new string('x', limits.LineLength - 1), 1);
            Refuses(limits, "[[s]]\n#" + new string('x', limits.LineLength), $"line 2: the line is longer than {limits.LineLength} characters");

            Takes(limits, $"[[s]]\nk = \"{new string('a', limits.StringLength)}\"", 1);
            Refuses(limits, $"[[s]]\nk = \"{new string('a', limits.StringLength + 1)}\"", $"line 2: a value may be at most {limits.StringLength} characters");
            Refuses(limits, $"[[s]]\nk = [\"{new string('a', limits.StringLength + 1)}\"]", $"line 2: a value may be at most {limits.StringLength} characters");
            Refuses(limits, "[[s]]\nk = \"C:\\Users\"", "line 2: a backslash is not allowed inside a value");

            Takes(limits, Lines("[[s]]", limits.Tables), limits.Tables);
            Refuses(limits, Lines("[[s]]", limits.Tables + 1), $"line {limits.Tables + 1}: at most {limits.Tables} sections are allowed");

            Takes(limits, Keys(limits.Pairs), 1);
            Refuses(limits, Keys(limits.Pairs + 1), $"line {limits.Pairs + 2}: a section may set at most {limits.Pairs} keys");

            Takes(limits, Items(limits.Items), 1);
            Refuses(limits, Items(limits.Items + 1), $"line 2: an array may hold at most {limits.Items} items");

            Assert.True(Toml.TryDecode(Hashes(limits.Bytes), limits, out var text, out var error), error);
            Assert.Equal(limits.Bytes, text.Length);
            Assert.False(Toml.TryDecode(Hashes(limits.Bytes + 1), limits, out text, out error));
            Assert.Empty(text);
            Assert.Equal($"the file is larger than {limits.Bytes / 1024} KiB, which is not a policy file", error);
        },
        count: 60);

    [Fact]
    public void The_writer_writes_a_value_exactly_when_it_reads_back_as_itself_within_the_limits()
    {
        var anything = Gen.Choose(0, 70).SelectMany(n => Gen.Frequency(
            (12, Gen.Elements(_units)),
            (1, Gen.Elements("\"", "\\", "\n", "\t", "\r", "\u0007", "\ud800", "\udc00", "\ud83d"))).ArrayOf(n)).Select(string.Concat);
        var items = Gen.Choose(0, 9).SelectMany(n => Gen.Frequency((4, Text(12)), (1, anything)).ArrayOf(n));
        var comments = Gen.OneOf(Gen.Constant<string?>(null), Text(30).Select(text => (string?)("\"" + text)), Gen.Choose(0, 270).Select(n => (string?)new string('c', n)));
        var cases =
            from limits in _limits
            from key in _bare
            from value in Gen.OneOf(anything, Gen.Choose(-1, 1).Select(offset => new string('a', limits.StringLength + offset)))
            from comment in comments
            from strings in items
            select (limits, key, value, comment, items: strings);

        Properties.Hold(cases, test =>
        {
            var line = $"{test.key} = \"{test.value}\"" + (test.comment is null ? string.Empty : $"  # {test.comment}");
            var writes = test.value.Length <= test.limits.StringLength && Writable(test.value) && line.Length <= test.limits.LineLength;
            var writer = new TomlWriter(test.limits);

            Assert.Equal(writes, writer.CanWrite(test.key, test.value, test.comment));

            if (writes)
            {
                Assert.Equal("text " + test.value, ReadBack(test.limits, writer.Table("s").Text(test.key, test.value, test.comment)));
            }
            else
            {
                Assert.Throws<ArgumentException>(() => writer.Text(test.key, test.value, test.comment));
                Assert.Empty(writer.ToString());
            }

            var array = $"{test.key} = [{string.Join(", ", test.items.Select(item => $"\"{item}\""))}]";
            var writesAll = test.items.Length <= test.limits.Items
                && test.items.All(item => item.Length <= test.limits.StringLength && Writable(item))
                && array.Length <= test.limits.LineLength;
            writer = new TomlWriter(test.limits);

            Assert.Equal(writesAll, writer.CanWriteStrings(test.key, test.items));

            if (writesAll)
            {
                Assert.Equal(Shape(TomlValue.OfArray(test.items)), ReadBack(test.limits, writer.Table("s").Strings(test.key, test.items)));
            }
            else
            {
                Assert.Throws<ArgumentException>(() => writer.Strings(test.key, test.items));
                Assert.Empty(writer.ToString());
            }
        });
    }

    [Fact]
    public void The_writer_takes_only_bare_names_whole_numbers_and_comments_the_reader_skips()
    {
        var names = Chars("abZ09_- .\"[é", 0, 14);
        var comments = Gen.OneOf(Chars("ab #\"[\t\u0007", 0, 20), Gen.Choose(0, 300).Select(n => new string('c', n)));
        var cases = from limits in _limits from name in names from comment in comments from number in Gen.Choose(-3, 3) select (limits, name, comment, number);

        Properties.Hold(cases, test =>
        {
            var bare = test.name.Length > 0 && test.name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

            TomlWriter Writer() => new(test.limits);

            if (bare)
            {
                Assert.Equal($"[[{test.name}]]\n", Writer().Table(test.name).ToString());
            }
            else
            {
                Assert.Throws<ArgumentException>(() => Writer().Table(test.name));
                Assert.Throws<ArgumentException>(() => Writer().Text(test.name, "v"));
                Assert.Throws<ArgumentException>(() => Writer().Number(test.name, 1));
                Assert.Throws<ArgumentException>(() => Writer().Strings(test.name, ["v"]));
            }

            if (test.number < 0)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Number("k", test.number));
            }
            else
            {
                Assert.Equal($"number {test.number}", ReadBack(test.limits, Writer().Table("s").Number("k", test.number)));
            }

            var writer = Writer();

            if (!test.comment.Any(char.IsControl) && test.comment.Length + 2 <= test.limits.LineLength)
            {
                Assert.Equal($"# {test.comment}\n\n", writer.Comment(test.comment).BlankLine().ToString());
                Assert.True(Toml.TryParse(writer.ToString(), test.limits, out var document, out var error), error);
                Assert.Empty(document.Tables);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => writer.Comment(test.comment));
                Assert.Empty(writer.ToString());
            }
        });
    }

    [Fact]
    public void The_policy_limits_are_exactly_what_they_were()
    {
        Assert.Equal(Toml.MaximumBytes, TomlLimits.Policy.Bytes);
        Assert.Equal(Toml.MaximumLines, TomlLimits.Policy.Lines);
        Assert.Equal(Toml.MaximumLineLength, TomlLimits.Policy.LineLength);
        Assert.Equal(Toml.MaximumTables, TomlLimits.Policy.Tables);
        Assert.Equal(Toml.MaximumPairs, TomlLimits.Policy.Pairs);
        Assert.Equal(Toml.MaximumItems, TomlLimits.Policy.Items);
        Assert.Equal(Toml.MaximumStringLength, TomlLimits.Policy.StringLength);
    }

    [Fact]
    public void TryGet_RejectsNull()
    {
        Assert.True(Toml.TryParse("[[allow]]\n", out var document, out _));
        Assert.Throws<ArgumentNullException>(() => Assert.Single(document.Tables).TryGet(null!, out _));
    }

    [Fact]
    public void TryParse_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => Toml.TryParse(null!, out _, out _));

    private static Gen<string> Chars(string pool, int least, int most) =>
        Gen.Choose(least, most).SelectMany(n => Gen.Elements(pool.ToCharArray()).ArrayOf(n)).Select(chars => new string(chars));

    private static Gen<string> Text(int most) =>
        Gen.Choose(0, most / 2).SelectMany(n => Gen.Elements(_units).ArrayOf(n)).Select(string.Concat);

    private static (string Text, List<string> Expected) Written(Section[] sections, Noise noise)
    {
        var writer = new TomlWriter(TomlLimits.Policy);
        var expected = new List<string>();
        var line = 0;

        void Filler()
        {
            for (var n = noise.Pick(0, 0, 1, 2); n > 0; n--, line++)
            {
                _ = noise.Pick(true, false) ? writer.BlankLine() : writer.Comment(noise.Pick("", "a \"quoted\" comment", "[[not]] = \"a section\""));
            }
        }

        foreach (var section in sections)
        {
            Filler();
            writer.Table(section.Name);
            expected.Add($"{++line} [[{section.Name}]]");

            foreach (var pair in section.Pairs)
            {
                Filler();
                var comment = noise.Pick<string?>(null, "the label", "# \"x\" = [y]");
                _ = pair.Value.Kind switch
                {
                    TomlValueKind.Text => writer.Text(pair.Key, pair.Value.Text, comment),
                    TomlValueKind.Number => writer.Number(pair.Key, pair.Value.Number, comment),
                    _ => writer.Strings(pair.Key, pair.Value.Items),
                };
                expected.Add($"{++line} {pair.Key} {Shape(pair.Value)}");
            }
        }

        Filler();
        return (writer.ToString(), expected);
    }

    private static (string Text, List<string> Expected) Laid(Section[] sections, Noise noise)
    {
        var lines = new List<string>();
        var expected = new List<string>();

        void Filler()
        {
            for (var n = noise.Pick(0, 0, 1, 2); n > 0; n--)
            {
                lines.Add(noise.Pick("", "  ", "\t", "# c", "  # [[not]] = \"a section\""));
            }
        }

        foreach (var section in sections)
        {
            Filler();
            lines.Add(noise.Pick("", " ", "\t", " \t ") + $"[[{section.Name}]]" + noise.Pick("", " ", "\t# [[x]] = \"y\""));
            expected.Add($"{lines.Count} [[{section.Name}]]");

            foreach (var pair in section.Pairs)
            {
                Filler();
                var value = pair.Value.Kind switch
                {
                    TomlValueKind.Text => $"\"{pair.Value.Text}\"",
                    TomlValueKind.Number => pair.Value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => noise.Pick("[", "[ ", "[\t") + string.Join(noise.Pick(",", ", ", " ,\t"), pair.Value.Items.Select(item => $"\"{item}\"")) + noise.Pick("]", " ]"),
                };
                lines.Add(noise.Pick("", " ", "\t") + pair.Key + noise.Pick("=", " = ", "\t=  ") + value + noise.Pick("", " ", "  # a \"note\" ]"));
                expected.Add($"{lines.Count} {pair.Key} {Shape(pair.Value)}");
            }
        }

        Filler();
        return (noise.Pick("", "﻿") + string.Join(noise.Pick("\n", "\r\n"), lines) + noise.Pick("", "\n", "\r\n"), expected);
    }

    private static List<string> Parsed(string text, Section[] sections)
    {
        Assert.True(Toml.TryParse(text, out var document, out var error), error);
        Assert.Equal(string.Empty, error);
        Assert.Equal(sections.Length, document.Tables.Count);

        foreach (var (table, section) in document.Tables.Zip(sections))
        {
            foreach (var pair in section.Pairs)
            {
                Assert.True(table.TryGet(pair.Key, out var read));
                Assert.Equal(Shape(pair.Value), Shape(read.Value));
            }

            Assert.False(table.TryGet("missing", out var none));
            Assert.Null(none);
        }

        return Shapes(document);
    }

    private static List<string> Shapes(TomlDocument document) =>
        [.. document.Tables.SelectMany(table => table.Pairs
            .Select(pair => $"{pair.Line} {pair.Key} {Shape(pair.Value)}")
            .Prepend($"{table.Line} [[{table.Name}]]"))];

    private static string Shape(TomlValue value) => value.Kind switch
    {
        TomlValueKind.Text => "text " + value.Text,
        TomlValueKind.Number => $"number {value.Number}",
        _ => $"array {value.Items.Count} {string.Join('\u0001', value.Items)}",
    };

    private static string ReadBack(TomlLimits limits, TomlWriter writer)
    {
        Assert.True(Toml.TryDecode(Encoding.UTF8.GetBytes(writer.ToString()), limits, out var text, out var error), error);
        Assert.True(Toml.TryParse(text, limits, out var document, out error), error);
        return Shape(Assert.Single(Assert.Single(document.Tables).Pairs).Value);
    }

    private static bool Writable(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '"' or '\\' || char.IsControl(text[i]))
            {
                return false;
            }

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void Takes(TomlLimits limits, string text, int tables)
    {
        Assert.True(Toml.TryParse(text, limits, out var document, out var error), error);
        Assert.Equal(tables, document.Tables.Count);
        Assert.Equal(string.Empty, error);
    }

    private static void Refuses(TomlLimits limits, string text, string error)
    {
        Assert.False(Toml.TryParse(text, limits, out var document, out var said));
        Assert.Empty(document.Tables);
        Assert.Equal(error, said);
    }

    private static string Lines(string line, int count) => string.Join('\n', Enumerable.Repeat(line, count));

    private static string Keys(int count) => "[[s]]\n" + string.Join('\n', Enumerable.Range(0, count).Select(i => $"k{i} = 1"));

    private static string Items(int count) => $"[[s]]\nk = [{string.Join(", ", Enumerable.Repeat("\"a\"", count))}]";

    private static byte[] Hashes(int count) => Enumerable.Repeat((byte)'#', count).ToArray();

    private sealed record Pair(string Key, TomlValue Value);

    private sealed record Section(string Name, Pair[] Pairs);
}
