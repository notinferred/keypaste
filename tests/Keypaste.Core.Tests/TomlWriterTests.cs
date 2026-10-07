using Keypaste.Core.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The writer writes only what <see cref="Toml"/> reads back as written.</summary>
public sealed class TomlWriterTests
{
    [Fact]
    public void What_it_writes_reads_back_as_written()
    {
        var text = new TomlWriter(TomlLimits.Policy)
            .Comment("a \"quoted\" comment")
            .BlankLine()
            .Table("allow")
            .Text("client", "caf\u00e9 \ud83d\udd11", "the label")
            .Number("max_ttl_seconds", 60)
            .ToString();

        Assert.True(Toml.TryParse(text, out var document, out var error), error);
        var table = Assert.Single(document.Tables);
        Assert.Equal("allow", table.Name);
        Assert.True(table.TryGet("client", out var client));
        Assert.Equal("caf\u00e9 \ud83d\udd11", client.Value.Text);
        Assert.True(table.TryGet("max_ttl_seconds", out var ttl));
        Assert.Equal(60, ttl.Value.Number);
    }

    [Fact]
    public void A_string_the_reader_would_refuse_or_read_as_something_else_is_not_written()
    {
        string[] values =
        [
            "say \"hi\"",
            "C:\\Users",
            "line\nbreak",
            "bell\u0007",
            "lone \ud800 surrogate",
            new string('a', TomlLimits.Policy.StringLength + 1),
        ];

        foreach (var value in values)
        {
            var writer = new TomlWriter(TomlLimits.Policy);

            Assert.False(writer.CanWrite("glob", value), value);
            Assert.Throws<ArgumentException>(() => writer.Text("glob", value));
            Assert.Equal(string.Empty, writer.ToString());
        }

        Assert.True(new TomlWriter(TomlLimits.Policy).CanWrite("glob", new string('a', TomlLimits.Policy.StringLength)));
    }

    [Fact]
    public void A_line_longer_than_the_reader_takes_is_not_written()
    {
        var value = new string('a', TomlLimits.Policy.StringLength);
        var comment = new string('c', TomlLimits.Policy.LineLength);

        Assert.False(new TomlWriter(TomlLimits.Policy).CanWrite("glob", value, comment));
        Assert.Throws<ArgumentException>(() => new TomlWriter(TomlLimits.Policy).Comment(comment));
    }
}
