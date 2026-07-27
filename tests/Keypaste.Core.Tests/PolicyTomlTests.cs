using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The strict subset reader, tested as syntax alone — no rules, no globs, no vault.
/// </summary>
/// <remarks>
/// The bulk of this class is one <c>[Theory]</c> listing every construct a real TOML document may
/// contain and this reader refuses. That list <em>is</em> the specification: a policy file is an
/// authorization document, so anything keypaste would have to guess the meaning of has to be a
/// refusal, and the only way to keep that true is to name each one and watch it fail.
/// </remarks>
public sealed class PolicyTomlTests
{
    [Fact]
    public void AnEmptyFile_ParsesToNoSections()
    {
        Assert.True(Toml.TryParse(string.Empty, out var document, out var error));
        Assert.Empty(document.Tables);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void AFileOfNothingButCommentsAndBlanks_ParsesToNoSections()
    {
        Assert.True(Toml.TryParse("# a comment\n\n   \n\t# another\n", out var document, out _));
        Assert.Empty(document.Tables);
    }

    [Fact]
    public void ItReadsTheThreeShapesAValueMayTake()
    {
        const string Text = """
            [[allow]]
            client          = "claude-code"
            max_ttl_seconds = 300
            entries         = ["env/dev/**", "env/test/**"]
            """;

        Assert.True(Toml.TryParse(Text, out var document, out var error), error);

        var table = Assert.Single(document.Tables);
        Assert.Equal("allow", table.Name);
        Assert.Equal(1, table.Line);

        Assert.True(table.TryGet("client", out var client));
        Assert.Equal(TomlValueKind.Text, client.Value.Kind);
        Assert.Equal("claude-code", client.Value.Text);
        Assert.Equal(2, client.Line);

        Assert.True(table.TryGet("max_ttl_seconds", out var ttl));
        Assert.Equal(TomlValueKind.Number, ttl.Value.Kind);
        Assert.Equal(300, ttl.Value.Number);

        Assert.True(table.TryGet("entries", out var entries));
        Assert.Equal(TomlValueKind.Array, entries.Value.Kind);
        Assert.Equal(2, entries.Value.Items.Count);
        Assert.Equal("env/dev/**", entries.Value.Items[0]);
        Assert.Equal("env/test/**", entries.Value.Items[1]);
    }

    [Fact]
    public void SectionsKeepTheirOrder_AndRepeatingOneIsHowYouWriteTwoRules()
    {
        const string Text = """
            [[allow]]
            client = "first"

            [[allow]]
            client = "second"
            """;

        Assert.True(Toml.TryParse(Text, out var document, out var error), error);

        Assert.Equal(2, document.Tables.Count);
        Assert.True(document.Tables[0].TryGet("client", out var first));
        Assert.True(document.Tables[1].TryGet("client", out var second));
        Assert.Equal("first", first.Value.Text);
        Assert.Equal("second", second.Value.Text);
    }

    [Fact]
    public void CommentsAndSpacingAroundEverythingAreIgnored()
    {
        const string Text = """
              [[allow]]   # who
              client  =  "claude-code"   # what
              entries = [ "env/**" , "personal/**" ]
              max_per_hour=20
            """;

        Assert.True(Toml.TryParse(Text, out var document, out var error), error);

        var table = Assert.Single(document.Tables);
        Assert.True(table.TryGet("entries", out var entries));
        Assert.Equal(2, entries.Value.Items.Count);
        Assert.Equal("env/**", entries.Value.Items[0]);
        Assert.True(table.TryGet("max_per_hour", out var cap));
        Assert.Equal(20, cap.Value.Number);
    }

    [Fact]
    public void AnEmptyArrayIsSyntax_AndItIsTheRuleLayerThatRefusesIt()
    {
        Assert.True(Toml.TryParse("[[allow]]\nentries = []\n", out var document, out _));

        var table = Assert.Single(document.Tables);
        Assert.True(table.TryGet("entries", out var entries));
        Assert.Empty(entries.Value.Items);
    }

    [Fact]
    public void CrLfLineEndings_ParseTheSameAsLf()
    {
        Assert.True(Toml.TryParse("[[allow]]\r\nclient = \"a\"\r\n", out var crlf, out _));

        Assert.True(Assert.Single(crlf.Tables).TryGet("client", out var pair));
        Assert.Equal("a", pair.Value.Text);
    }

    /// <summary>
    /// Every construct the reader refuses, one file each. A message that does not begin
    /// <c>line N:</c> is a bug on its own: the whole file is about to be ignored, so the one thing
    /// the operator needs is where to look.
    /// </summary>
    [Theory]
    // Keys.
    [InlineData("[[allow]]\nclient.name = \"x\"\n", "a dotted key")]
    [InlineData("[[allow]]\n\"client\" = \"x\"\n", "a quoted key")]
    [InlineData("[[allow]]\nclient \"x\"\n", "no equals sign")]
    [InlineData("[[allow]]\nclient =\n", "no value")]
    [InlineData("[[allow]]\nclient = \"a\"\nclient = \"b\"\n", "a key set twice")]
    [InlineData("client = \"x\"\n[[allow]]\n", "a key outside every section")]
    // Headers.
    [InlineData("[allow]\nclient = \"x\"\n", "a singular table header")]
    [InlineData("[[allow]\nclient = \"x\"\n", "one closing bracket")]
    [InlineData("[[]]\nclient = \"x\"\n", "an unnamed section")]
    [InlineData("[[allow]] junk\nclient = \"x\"\n", "text after a header")]
    [InlineData("[[allow.inner]]\nclient = \"x\"\n", "a dotted section name")]
    // Strings.
    [InlineData("[[allow]]\nclient = 'x'\n", "a literal string")]
    [InlineData("[[allow]]\nclient = \"\"\"x\"\"\"\n", "a multi-line string")]
    [InlineData("[[allow]]\nclient = \"x\n", "an unterminated string")]
    [InlineData("[[allow]]\nclient = \"a\\tb\"\n", "a backslash escape")]
    [InlineData("[[allow]]\nclient = \"a\" junk\n", "text after a value")]
    // Numbers.
    [InlineData("[[allow]]\nmax_ttl_seconds = -1\n", "a negative number")]
    [InlineData("[[allow]]\nmax_ttl_seconds = +1\n", "an explicitly positive number")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 300.0\n", "a decimal")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 3_600\n", "a digit separator")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 0x10\n", "hexadecimal")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 0o17\n", "octal")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 1e3\n", "an exponent")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 0300\n", "a leading zero")]
    [InlineData("[[allow]]\nmax_ttl_seconds = 99999999999\n", "a number wider than an int")]
    // Everything else TOML has.
    [InlineData("[[allow]]\nenabled = true\n", "a boolean")]
    [InlineData("[[allow]]\nenabled = false\n", "the other boolean")]
    [InlineData("[[allow]]\nwritten = 2026-07-26\n", "a date")]
    [InlineData("[[allow]]\nwritten = 14:03:11\n", "a time")]
    [InlineData("[[allow]]\nrule = { client = \"x\" }\n", "an inline table")]
    // Arrays.
    [InlineData("[[allow]]\nentries = [\"a\",]\n", "a trailing comma")]
    [InlineData("[[allow]]\nentries = [[\"a\"]]\n", "a nested array")]
    [InlineData("[[allow]]\nentries = [1, 2]\n", "an array of numbers")]
    [InlineData("[[allow]]\nentries = [\"a\"\n", "an unterminated array")]
    [InlineData("[[allow]]\nentries = [\"a\" \"b\"]\n", "a missing comma")]
    [InlineData("[[allow]]\nentries = [\n  \"a\"\n]\n", "an array spanning lines")]
    public void EveryConstructTheReaderRefuses_FailsAndNamesItsLine(string text, string what)
    {
        Assert.False(Toml.TryParse(text, out var document, out var error), what);
        Assert.Empty(document.Tables);
        Assert.StartsWith("line ", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The type error the theory above deliberately does not make: a number written as a string is
    /// perfectly good syntax, and it is the rule layer's job to say so. Kept as its own test so the
    /// boundary between the two layers is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void AStringWhereANumberBelongs_IsSyntaxTheRuleLayerMustRefuse()
    {
        Assert.True(Toml.TryParse("[[allow]]\nmax_ttl_seconds = \"300\"\n", out var document, out _));

        var table = Assert.Single(document.Tables);
        Assert.True(table.TryGet("max_ttl_seconds", out var pair));
        Assert.Equal(TomlValueKind.Text, pair.Value.Kind);
    }

    [Fact]
    public void AFileOverTheSizeCap_DoesNotDecode()
    {
        var bytes = new byte[Toml.MaximumBytes + 1];
        Array.Fill(bytes, (byte)'#');

        Assert.False(Toml.TryDecode(bytes, out var text, out var error));
        Assert.Empty(text);
        Assert.Contains("policy file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotUtf8_DoesNotDecode()
    {
        var utf16 = new byte[] { 0xFF, 0xFE, 0x41, 0x00 };

        Assert.False(Toml.TryDecode(utf16, out _, out var error));
        Assert.Contains("UTF-8", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AByteOrderMark_IsNotMistakenForPartOfTheFirstSection()
    {
        var body = "[[allow]]\nclient = \"x\"\n"u8.ToArray();
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);

        Assert.True(Toml.TryDecode(bytes, out var text, out _));
        Assert.True(Toml.TryParse(text, out var document, out var error), error);
        Assert.Equal("allow", Assert.Single(document.Tables).Name);
    }

    [Fact]
    public void MoreLinesThanTheCap_IsRefused()
    {
        var text = string.Join('\n', Enumerable.Repeat("# filler", Toml.MaximumLines + 1));

        Assert.False(Toml.TryParse(text, out _, out var error));
        Assert.Contains("lines", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALineLongerThanTheCap_IsRefusedAndNamesItsLine()
    {
        var text = "[[allow]]\nclient = \"" + new string('x', Toml.MaximumLineLength) + "\"\n";

        Assert.False(Toml.TryParse(text, out _, out var error));
        Assert.StartsWith("line 2:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AStringLongerThanTheCap_IsRefused()
    {
        var text = "[[allow]]\nclient = \"" + new string('x', Toml.MaximumStringLength + 1) + "\"\n";

        Assert.False(Toml.TryParse(text, out _, out var error));
        Assert.StartsWith("line 2:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreSectionsThanTheCap_IsRefused()
    {
        var text = string.Join('\n', Enumerable.Repeat("[[allow]]", Toml.MaximumTables + 1));

        Assert.False(Toml.TryParse(text, out _, out var error));
        Assert.Contains("sections", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreKeysInOneSectionThanTheCap_IsRefused()
    {
        var keys = Enumerable.Range(0, Toml.MaximumPairs + 1).Select(i => $"k{i} = 1");
        var text = "[[allow]]\n" + string.Join('\n', keys);

        Assert.False(Toml.TryParse(text, out _, out var error));
        Assert.Contains("keys", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreItemsInOneArrayThanTheCap_IsRefused()
    {
        var items = Enumerable.Repeat("\"env/**\"", Toml.MaximumItems + 1);
        var text = "[[allow]]\nentries = [" + string.Join(", ", items) + "]";

        Assert.False(Toml.TryParse(text, out _, out var error));
        Assert.Contains("items", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The forward-compatibility direction, and the one that has to fail closed. A rule shape from
    /// a later keypaste — or a <c>[[deny]]</c> section somebody assumed would work — must invalidate
    /// the whole file, never be quietly skipped while the <c>[[allow]]</c> rules stay in force.
    /// </summary>
    /// <remarks>
    /// The reader itself is syntax-only and accepts any bare section name, so this asserts the
    /// section survives parsing with its name intact and its line recorded — which is what the rule
    /// layer then refuses. Splitting it this way is what keeps every syntax question answerable
    /// without a policy in the room.
    /// </remarks>
    [Fact]
    public void AnUnknownSection_ReachesTheRuleLayerWithItsNameAndLine()
    {
        const string Text = """
            [[allow]]
            client = "claude-code"

            [[deny]]
            client = "other"
            """;

        Assert.True(Toml.TryParse(Text, out var document, out var error), error);

        Assert.Equal(2, document.Tables.Count);
        Assert.Equal("deny", document.Tables[1].Name);
        Assert.Equal(4, document.Tables[1].Line);
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
}
