using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The <c>.env</c> writer, and above all the round trip back through <see cref="DotEnv"/>.
/// </summary>
/// <remarks>
/// <para>
/// The round trip is asserted through the <b>byte</b> layer — format, encode, decode, parse —
/// rather than straight from text to <see cref="DotEnv.TryParse"/>. Two of the ways this can break
/// live only in the bytes: a byte order mark that the reader would strip from the first key, and a
/// file that formats past <see cref="DotEnv.MaximumBytes"/> and so cannot be read back at all.
/// </para>
/// <para>
/// Opening no vault, these cost nothing to be exhaustive about, which is where the value is: a
/// wrongly written secret is not noticed until something else fails.
/// </para>
/// </remarks>
public sealed class DotEnvWriterTests
{
    /// <summary>Every character class that has ever mattered to either side of the grammar.</summary>
    /// <remarks>
    /// Constructed rather than random. Random ASCII would hit the interesting characters rarely and
    /// the interesting <em>combinations</em> essentially never, and a failure that appears once in
    /// a hundred runs is worse than no test.
    /// </remarks>
    internal static readonly string[] Corpus =
    [
        string.Empty,
        " ",
        "  ",
        "\t",
        " \t ",
        "a",
        "8080",
        "postgres://user:p#ss@localhost:5432/app",
        "sk_test_51H8xY2eZvKYlo2C",
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc-_",
        "YmFzZTY0K3ZhbHVlLw==",
        "~/bin:~/local/bin",
        "${NOT_EXPANDED}",
        "$HOME",
        "pa$$w0rd",
        "#",
        "#ff0000",
        "a#b",
        "a #b",
        "=leading-equals",
        "a=b=c",
        "export A=1",
        "C:\\logs\\app",
        "C:\\temp",
        "trailing\\",
        "\\",
        "a\\nb",
        "it's",
        "'quoted'",
        "\"double\"",
        "`backtick`",
        "mixed ' and \" and `",
        "line one\nline two",
        "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----",
        "carriage\rreturn",
        "crlf\r\ninside",
        "\r",
        "tab\there",
        "bell\u0001here",
        "vertical\u000Btab",
        "delete\u007Fhere",
        "nbsp\u00A0here",
        "bom\uFEFFinside",
        "caf\u00e9",
        "astral\U0001F510pair",
        "line\u2028separator",
        "   leading and trailing   ",
        new string('x', 64 * 1024),
    ];

    private static readonly UTF8Encoding _strictUtf8 = new(false, throwOnInvalidBytes: true);

    /// <summary>Formats, asserting success, because most cases here are meant to work.</summary>
    private static DotEnvText Format(params (string Key, string Value)[] variables)
    {
        var input = variables.Select(v => new EnvVariable(v.Key, v.Value)).ToList();

        Assert.True(
            DotEnvWriter.TryFormat(input, out var file, out var error),
            $"formatting failed: {error}");

        return file;
    }

    /// <summary>The whole property: bytes out, bytes in, same variables in the same order.</summary>
    private static void AssertRoundTrips(params (string Key, string Value)[] variables)
    {
        var file = Format(variables);

        Assert.True(DotEnv.TryDecode(file.Utf8.Span, out var text, out var decodeError), decodeError);
        Assert.True(DotEnv.TryParse(text, out var document));

        Assert.Equal(
            variables.Select(v => (v.Key, v.Value)).ToArray(),
            document.Variables.Select(v => (v.Key, v.Value)).ToArray());
    }

    /// <summary>The line a key was written on, without the trailing newline.</summary>
    private static string Line(string key, string value)
    {
        var text = Format((key, value)).Text;
        var body = text[DotEnvWriter.Header.Length..];
        return body.TrimEnd('\n');
    }

    // ---- P1: the round trip ------------------------------------------------------------

    [Fact]
    public void EveryValueInTheCorpus_SurvivesTheRoundTrip()
    {
        foreach (var value in Corpus)
        {
            AssertRoundTrips(("VALUE", value));
        }
    }

    [Fact]
    public void TheWholeCorpusAtOnce_SurvivesInOrder()
    {
        var variables = Corpus
            .Where(v => v.Length < 1024)
            .Select((value, i) => ($"VAR_{i}", value))
            .ToArray();

        AssertRoundTrips(variables);
    }

    [Fact]
    public void KeyOrderIsPreserved_NotSorted()
    {
        var file = Format(("ZEBRA", "1"), ("APPLE", "2"), ("MIDDLE", "3"));

        Assert.True(DotEnv.TryParse(file.Text, out var document));
        Assert.Equal(["ZEBRA", "APPLE", "MIDDLE"], document.Variables.Select(v => v.Key));
    }

    // ---- P2: byte invariants -----------------------------------------------------------

    /// <summary>
    /// No byte order mark, ever. <see cref="Encoding.UTF8"/> emits one, so the obvious
    /// <c>File.WriteAllText(path, text, Encoding.UTF8)</c> would — and the reader would then strip
    /// it from the first key rather than from the file.
    /// </summary>
    [Fact]
    public void TheBytesNeverStartWithAByteOrderMark()
    {
        var bytes = Format(("A", "1")).Utf8.Span;

        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 BOM");
        Assert.False(bytes[0] == 0xFF && bytes[1] == 0xFE, "UTF-16LE BOM");
        Assert.False(bytes[0] == 0xFE && bytes[1] == 0xFF, "UTF-16BE BOM");
    }

    /// <summary>
    /// A raw carriage return is never written, which is what makes excluding it from the unquoted
    /// form load-bearing rather than decorative: the reader collapses only the two-character
    /// <c>\r\n</c>, so a bare CR would survive as an ordinary character and then be trimmed off the
    /// ends of an unquoted value.
    /// </summary>
    [Fact]
    public void ACarriageReturnIsNeverWrittenRaw()
    {
        foreach (var value in Corpus)
        {
            var bytes = Format(("VALUE", value)).Utf8.ToArray();
            Assert.DoesNotContain((byte)'\r', bytes);
        }
    }

    [Fact]
    public void TheFileEndsWithExactlyOneNewline()
    {
        var text = Format(("A", "1"), ("B", "2")).Text;

        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTextAndTheBytesAgree() =>
        Assert.Equal(Format(("A", "caf\u00e9")).Text, _strictUtf8.GetString(Format(("A", "caf\u00e9")).Utf8.Span));

    // ---- P3: minimality ----------------------------------------------------------------

    /// <summary>
    /// Double quotes are used <b>only</b> for a value containing an apostrophe or a carriage
    /// return.
    /// </summary>
    /// <remarks>
    /// This is the test that protects the design. Always-double-quoting round-trips perfectly
    /// through keypaste and silently breaks <c>motdotla/dotenv</c>, which expands only <c>\n</c>
    /// and <c>\r</c> — so every other test in this file would stay green while exported Windows
    /// paths came back with doubled backslashes.
    /// </remarks>
    [Fact]
    public void DoubleQuotesAreUsedOnlyWhenNothingElseWorks()
    {
        foreach (var value in Corpus)
        {
            var needsEscaping = value.Contains('\'', StringComparison.Ordinal)
                || value.Contains('\r', StringComparison.Ordinal);

            var line = Line("VALUE", value);
            var isDoubleQuoted = line.StartsWith("VALUE=\"", StringComparison.Ordinal);

            Assert.True(
                needsEscaping == isDoubleQuoted,
                $"double-quoting decision is wrong for a value of {value.Length} characters: {line.Length} written");
        }
    }

    [Theory]
    [InlineData("8080", "PORT=8080")]
    [InlineData("postgres://u:p@h:5432/db", "PORT=postgres://u:p@h:5432/db")]
    [InlineData("a=b=c", "PORT=a=b=c")]
    [InlineData("=leading", "PORT==leading")]
    [InlineData("base64+value/x==", "PORT=base64+value/x==")]
    [InlineData("", "PORT=")]
    public void OrdinaryValuesAreWrittenBare(string value, string expected) =>
        Assert.Equal(expected, Line("PORT", value));

    /// <summary>
    /// A Windows path is single-quoted, not backslash-escaped. Escaping it round-trips through
    /// keypaste and comes back doubled in <c>motdotla/dotenv</c>, which is the whole reason single
    /// quotes are preferred.
    /// </summary>
    [Fact]
    public void AWindowsPathIsSingleQuoted_NotBackslashEscaped()
    {
        Assert.Equal("PATHY='C:\\logs\\app'", Line("PATHY", "C:\\logs\\app"));
        Assert.Equal("PATHY='C:\\temp'", Line("PATHY", "C:\\temp"));
    }

    /// <summary>
    /// Interpolation-shaped values are single-quoted, which is the one form that suppresses
    /// expansion in <c>godotenv</c> and <c>python-dotenv</c> as well as keeping it literal here.
    /// </summary>
    [Theory]
    [InlineData("${NOT_EXPANDED}")]
    [InlineData("$HOME")]
    [InlineData("pa$$w0rd")]
    public void ADollarValueIsSingleQuoted(string value) =>
        Assert.Equal($"V='{value}'", Line("V", value));

    /// <summary>
    /// <c>~</c> is kept out of the unquoted set although the reader accepts it there: a shell that
    /// sources the file expands it after every <c>:</c>, baking one machine's home directory into
    /// the value — the hazard this codebase already refuses for <c>$</c>.
    /// </summary>
    [Fact]
    public void ATildeValueIsQuoted() =>
        Assert.Equal("V='~/bin:~/local/bin'", Line("V", "~/bin:~/local/bin"));

    [Theory]
    [InlineData("#", "V='#'")]
    [InlineData("#ff0000", "V='#ff0000'")]
    [InlineData("a#b", "V='a#b'")]
    [InlineData("a #b", "V='a #b'")]
    [InlineData(" ", "V=' '")]
    [InlineData("   leading and trailing   ", "V='   leading and trailing   '")]
    [InlineData("line one\nline two", "V='line one\nline two'")]
    public void EverythingElseIsSingleQuoted(string value, string expected) =>
        Assert.Equal(expected, Line("V", value));

    [Fact]
    public void OnlyApostrophesAndCarriageReturnsReachTheEscapedForm()
    {
        Assert.Equal("V=\"it's\"", Line("V", "it's"));
        Assert.Equal("V=\"a\\rb\"", Line("V", "a\rb"));
        Assert.Equal("V=\"it's a \\\"quote\\\" and a \\\\\"", Line("V", "it's a \"quote\" and a \\"));
    }

    // ---- P4, P5: determinism and idempotence -------------------------------------------

    /// <summary>
    /// Two exports of the same data are byte-identical. A timestamp in the header would break this,
    /// which is exactly why there is not one.
    /// </summary>
    [Fact]
    public void FormattingIsDeterministic() =>
        Assert.Equal(Format(("A", "1"), ("B", "x y")).Utf8.ToArray(), Format(("A", "1"), ("B", "x y")).Utf8.ToArray());

    [Fact]
    public void FormattingIsIdempotentThroughTheReader()
    {
        var once = Format(Corpus.Where(v => v.Length < 1024).Select((v, i) => ($"V{i}", v)).ToArray());

        Assert.True(DotEnv.TryParse(once.Text, out var document));
        var twice = Format(document.Variables.Select(v => (v.Key, v.Value)).ToArray());

        Assert.Equal(once.Text, twice.Text);
    }

    [Fact]
    public void TheHeaderIsACommentTheReaderSkips()
    {
        var file = Format(("A", "1"));

        Assert.StartsWith("#", file.Text, StringComparison.Ordinal);
        Assert.True(DotEnv.TryParse(file.Text, out var document));
        Assert.Equal("A", Assert.Single(document.Variables).Key);
    }

    // ---- hard errors --------------------------------------------------------------------

    /// <summary>Nothing is written, and the reason never quotes the value.</summary>
    private static string Refused(params (string Key, string Value)[] variables)
    {
        var input = variables.Select(v => new EnvVariable(v.Key, v.Value)).ToList();

        Assert.False(DotEnvWriter.TryFormat(input, out var file, out var error));
        Assert.Null(file);
        Assert.NotEmpty(error);

        foreach (var (_, value) in variables)
        {
            if (value.Length >= 4)
            {
                Assert.DoesNotContain(value, error, StringComparison.Ordinal);
            }
        }

        return error;
    }

    [Fact]
    public void AKeyThatIsNotAnEnvironmentVariableNameIsRefused_NamingAllOfThem()
    {
        var error = Refused(("GOOD", "1"), ("BAD-NAME", "2"), ("also.bad", "3"));

        Assert.Contains("BAD-NAME", error, StringComparison.Ordinal);
        Assert.Contains("also.bad", error, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysDifferingOnlyInCaseAreRefused()
    {
        var error = Refused(("PATH", "1"), ("Path", "2"));

        Assert.Contains("PATH", error, StringComparison.Ordinal);
        Assert.Contains("Path", error, StringComparison.Ordinal);
        Assert.Contains("case", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A duplicate is fine to inject and impossible to write: the reader refuses a key set twice.
    /// </summary>
    [Fact]
    public void AKeyListedTwiceIsRefused() =>
        Assert.Contains("SAME", Refused(("SAME", "1"), ("SAME", "2")), StringComparison.Ordinal);

    [Fact]
    public void ANulInAValueIsRefused() =>
        Assert.Contains("NULLY", Refused(("NULLY", "a\0b")), StringComparison.Ordinal);

    [Fact]
    public void ALoneSurrogateIsRefused() =>
        Assert.Contains("BROKEN", Refused(("BROKEN", "a\uD800b")), StringComparison.Ordinal);

    /// <summary>
    /// keypaste must not write a file keypaste refuses to read. The escaped form can nearly double
    /// a value's length and non-ASCII costs up to four bytes a character, so the two limits have to
    /// be the same limit.
    /// </summary>
    [Fact]
    public void AFileTooLargeForTheReaderIsRefused()
    {
        var error = Refused(("HUGE", new string('x', DotEnv.MaximumBytes + 1)));

        Assert.Contains("KiB", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileJustUnderTheLimitIsAccepted()
    {
        var value = new string('x', DotEnv.MaximumBytes - 4096);

        Assert.True(DotEnvWriter.TryFormat([new EnvVariable("BIG", value)], out var file, out var error), error);
        Assert.True(DotEnv.TryDecode(file.Utf8.Span, out var text, out _));
        Assert.True(DotEnv.TryParse(text, out var document));
        Assert.Equal(value, Assert.Single(document.Variables).Value);
    }

    [Fact]
    public void NullVariablesThrows() =>
        Assert.Throws<ArgumentNullException>(() => DotEnvWriter.TryFormat(null!, out _, out _));

    [Fact]
    public void NothingIsWritten_ForAnEmptyProject()
    {
        var file = Format();

        Assert.Equal(DotEnvWriter.Header, file.Text);
        Assert.True(DotEnv.TryParse(file.Text, out var document));
        Assert.Empty(document.Variables);
    }

    // ---- notes --------------------------------------------------------------------------

    [Fact]
    public void TheEscapedFormEarnsANote()
    {
        var file = Format(("PLAIN", "ok"), ("APOSTROPHE", "it's"));

        var note = Assert.Single(file.Notes, n => n.Kind == DotEnvWriteNoteKind.EscapeDialect);
        Assert.Equal("APOSTROPHE", note.Key);
    }

    /// <summary>
    /// A backslash sitting against the closing quote. It round-trips, and it earns no warning: a
    /// note was written for it and then removed, because the reader it was meant to warn about —
    /// <c>motdotla/dotenv</c> 17.4.2 — reads it correctly in both quote styles, as does <c>sh</c>.
    /// A warning that fires on a case that works is how warnings stop being read.
    /// </summary>
    [Theory]
    [InlineData("trailing\\")]
    [InlineData("it's trailing\\")]
    public void ATrailingBackslash_RoundTripsWithoutComplaint(string value)
    {
        AssertRoundTrips(("SLASHY", value));

        var notes = Format(("SLASHY", value)).Notes;
        Assert.All(notes, n => Assert.Equal(DotEnvWriteNoteKind.EscapeDialect, n.Kind));
    }

    [Fact]
    public void AnOrdinaryFileHasNoNotes() =>
        Assert.Empty(Format(("A", "1"), ("B", "hello world"), ("C", "C:\\x")).Notes);

    // ---- the golden file ------------------------------------------------------------------

    /// <summary>
    /// The exact mirror of <see cref="DotEnvTests.ParsesARealisticFile"/>: the same variables,
    /// written out. Asserted against the literal text as well as the reparsed map, so a change to
    /// quoting or to the header is visible in the diff rather than only as a pass or a fail.
    /// </summary>
    [Fact]
    public void WritesARealisticFile()
    {
        var file = Format(
            ("DATABASE_URL", "postgres://user:p#ss@localhost:5432/app"),
            ("PORT", "8080"),
            ("NODE_ENV", "production"),
            ("EMPTY", string.Empty),
            ("MESSAGE", "line one\nline two"),
            ("WINDOWS_PATH", "C:\\logs\\app"),
            ("LITERAL_TEMPLATE", "${NOT_EXPANDED}"),
            ("PRIVATE_KEY", "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----"),
            ("SHELL_ISH", "a 'b' \"c\""));

        Assert.Equal(
            DotEnvWriter.Header + string.Join('\n',
                "DATABASE_URL='postgres://user:p#ss@localhost:5432/app'",
                "PORT=8080",
                "NODE_ENV=production",
                "EMPTY=",
                "MESSAGE='line one\nline two'",
                "WINDOWS_PATH='C:\\logs\\app'",
                "LITERAL_TEMPLATE='${NOT_EXPANDED}'",
                "PRIVATE_KEY='-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----'",
                "SHELL_ISH=\"a 'b' \\\"c\\\"\"",
                string.Empty),
            file.Text);

        AssertRoundTrips(
            ("DATABASE_URL", "postgres://user:p#ss@localhost:5432/app"),
            ("PORT", "8080"),
            ("NODE_ENV", "production"),
            ("EMPTY", string.Empty),
            ("MESSAGE", "line one\nline two"),
            ("WINDOWS_PATH", "C:\\logs\\app"),
            ("LITERAL_TEMPLATE", "${NOT_EXPANDED}"),
            ("PRIVATE_KEY", "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----"),
            ("SHELL_ISH", "a 'b' \"c\""));
    }
}
