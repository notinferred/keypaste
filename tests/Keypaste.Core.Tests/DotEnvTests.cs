using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The <c>.env</c> grammar, exhaustively. These tests open no vault, so thoroughness costs
/// nothing here — unlike in <see cref="EnvStoreTests"/>, where every case pays for Argon2.
/// </summary>
public sealed class DotEnvTests
{
    /// <summary>Parses without asserting success, so the problem cases can inspect the result.</summary>
    private static DotEnvDocument Parse(string text)
    {
        var wellFormed = DotEnv.TryParse(text, out var document);

        // The return value and the problem list must never disagree; asserting it here means
        // every test in the file checks it, rather than one test checking it once.
        Assert.Equal(wellFormed, document.Problems.Count == 0);
        return document;
    }

    private static Dictionary<string, string> Map(string text)
    {
        var document = Parse(text);
        Assert.Empty(document.Problems);
        return document.Variables.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
    }

    private static string Only(string text)
    {
        var map = Map(text);
        return Assert.Single(map).Value;
    }

    // ---- decoding ----------------------------------------------------------------------

    [Fact]
    public void Utf8Bom_IsStripped_SoTheFirstKeyIsUsable()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "A=1"u8];

        Assert.True(DotEnv.TryDecode(bytes, out var text, out var error));
        Assert.Empty(error);
        Assert.Equal("1", Only(text));
    }

    /// <summary>
    /// Windows PowerShell 5.1 writes UTF-16LE from <c>&gt;</c> and <c>Set-Content</c>. Without
    /// this, every line of such a file is reported as malformed and the user has no idea why.
    /// </summary>
    [Fact]
    public void Utf16_Decodes_InBothByteOrders()
    {
        Assert.True(DotEnv.TryDecode(Encoding.Unicode.GetPreamble().Concat(
            Encoding.Unicode.GetBytes("A=1")).ToArray(), out var little, out _));
        Assert.Equal("1", Only(little));

        Assert.True(DotEnv.TryDecode(Encoding.BigEndianUnicode.GetPreamble().Concat(
            Encoding.BigEndianUnicode.GetBytes("A=1")).ToArray(), out var big, out _));
        Assert.Equal("1", Only(big));
    }

    /// <summary>
    /// A secret quietly rewritten to <c>U+FFFD</c> would be stored, injected, and rejected by
    /// whatever it authenticates against, with nothing anywhere saying why.
    /// </summary>
    [Fact]
    public void InvalidUtf8_IsRejected_NotReplaced()
    {
        byte[] bytes = [.. "A="u8, 0xC3, 0x28];

        Assert.False(DotEnv.TryDecode(bytes, out var text, out var error));
        Assert.Empty(text);
        Assert.Contains("UTF-8", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainUtf8_DecodesWithoutABom()
    {
        Assert.True(DotEnv.TryDecode("A=é"u8, out var text, out _));
        Assert.Equal("é", Only(text));
    }

    [Fact]
    public void AFileLargerThanTheLimit_IsRejectedWithoutBeingParsed()
    {
        var bytes = new byte[DotEnv.MaximumBytes + 1];

        Assert.False(DotEnv.TryDecode(bytes, out _, out var error));
        Assert.Contains("not a .env file", error, StringComparison.Ordinal);
    }

    // ---- structure ---------------------------------------------------------------------

    [Fact]
    public void ParsesASimpleAssignment() => Assert.Equal("value", Only("KEY=value"));

    [Fact]
    public void TrimsWhitespaceAroundTheKeyAndTheValue() =>
        Assert.Equal("value", Only("  KEY  =  value  "));

    [Theory]
    [InlineData("export KEY=v")]
    [InlineData("export\tKEY=v")]
    [InlineData("export   KEY=v")]
    [InlineData("  export KEY=v")]
    public void AcceptsAnExportPrefix(string line) => Assert.Equal("v", Only(line));

    /// <summary><c>exportKEY</c> is a legal variable name, so it is one — not a prefix.</summary>
    [Fact]
    public void ExportWithoutWhitespace_IsPartOfTheKey() =>
        Assert.Equal("exportKEY", Assert.Single(Map("exportKEY=v")).Key);

    [Fact]
    public void SkipsBlankLinesAndComments()
    {
        var map = Map("# leading\n\n   \nA=1\n   # indented\nB=2\n");

        Assert.Equal(2, map.Count);
        Assert.Equal("1", map["A"]);
        Assert.Equal("2", map["B"]);
    }

    [Fact]
    public void AnEmptyFile_HasNoVariablesAndNoProblems()
    {
        var document = Parse(string.Empty);

        Assert.Empty(document.Variables);
        Assert.Empty(document.Problems);
    }

    [Fact]
    public void PreservesTheOrderOfAppearance()
    {
        var document = Parse("Z=1\nA=2\nM=3");

        Assert.Equal(["Z", "A", "M"], document.Variables.Select(v => v.Key));
        Assert.Equal([1, 2, 3], document.Variables.Select(v => v.Line));
    }

    [Fact]
    public void CrlfParsesIdenticallyToLf()
    {
        const string Body = "A=1\nB=\"two\nlines\"\nC=3";

        Assert.Equal(Map(Body), Map(Body.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    // ---- values ------------------------------------------------------------------------

    [Theory]
    [InlineData("A='a\\nb'", "a\\nb")]
    [InlineData("A='has \"double\" quotes'", "has \"double\" quotes")]
    [InlineData("A='  padded  '", "  padded  ")]
    public void SingleQuotedValues_AreLiteral(string line, string expected) =>
        Assert.Equal(expected, Only(line));

    [Fact]
    public void BacktickQuotedValues_AreLiteral() =>
        Assert.Equal("both ' and \" inside", Only("A=`both ' and \" inside`"));

    [Theory]
    [InlineData("A=\"a\\nb\"", "a\nb")]
    [InlineData("A=\"a\\rb\"", "a\rb")]
    [InlineData("A=\"a\\tb\"", "a\tb")]
    [InlineData("A=\"a\\\\b\"", "a\\b")]
    [InlineData("A=\"a\\\"b\"", "a\"b")]
    public void DoubleQuotedValues_ExpandTheFiveEscapes(string line, string expected) =>
        Assert.Equal(expected, Only(line));

    /// <summary>
    /// Anything outside the five keeps its backslash, so a Windows path is not silently mangled
    /// into something shorter.
    /// </summary>
    [Theory]
    [InlineData("A=\"C:\\logs\\app\"", "C:\\logs\\app")]
    [InlineData("A=\"\\u00e9\"", "\\u00e9")]
    [InlineData("A=\"\\q\"", "\\q")]
    public void DoubleQuotedValues_LeaveEveryOtherEscapeVerbatim(string line, string expected) =>
        Assert.Equal(expected, Only(line));

    /// <summary>
    /// The sharp edge of supporting <c>\t</c> at all, pinned rather than discovered: inside double
    /// quotes <c>C:\temp</c> is <c>C:</c> followed by a tab, exactly as it would be in C, Python,
    /// or a shell's <c>$'...'</c>. The other two quoting styles are literal and are the fix, so
    /// this is documented in the README rather than special-cased in the scanner — a path-shaped
    /// exception to an escape rule would be a worse surprise than the escape rule itself.
    /// </summary>
    [Fact]
    public void DoubleQuotesExpandBackslashT_EvenInSomethingThatLooksLikeAPath()
    {
        Assert.Equal("C:\temp", Only("A=\"C:\\temp\""));
        Assert.Equal("C:\\temp", Only("A='C:\\temp'"));
        Assert.Equal("C:\\temp", Only("A=C:\\temp"));
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void AQuotedValueMaySpanLines(char quote)
    {
        var text = $"KEY={quote}-----BEGIN KEY-----\nabc\ndef\n-----END KEY-----{quote}\nAFTER=1";
        var map = Map(text);

        Assert.Equal("-----BEGIN KEY-----\nabc\ndef\n-----END KEY-----", map["KEY"]);
        Assert.Equal("1", map["AFTER"]);
    }

    [Fact]
    public void AMultilineValue_UsesLfEvenWhenTheFileUsedCrlf() =>
        Assert.Equal("a\nb", Only("K=\"a\r\nb\""));

    [Theory]
    [InlineData("A=")]
    [InlineData("A=   ")]
    [InlineData("A=\"\"")]
    [InlineData("A= # a comment")]
    public void AnEmptyValueIsLegal(string line) => Assert.Equal(string.Empty, Only(line));

    /// <summary>
    /// dotenv (JS) truncates at any <c>#</c>, which turns <c>hunter2#42</c> into <c>hunter2</c>.
    /// That is a shortened secret failing much later, somewhere else.
    /// </summary>
    [Theory]
    [InlineData("A=hunter2#42", "hunter2#42")]
    [InlineData("A=#ff0000", "#ff0000")]
    [InlineData("A=a#b#c", "a#b#c")]
    public void AHashWithNoWhitespaceBeforeIt_StaysInTheValue(string line, string expected) =>
        Assert.Equal(expected, Only(line));

    [Fact]
    public void AHashAfterWhitespace_IsAComment_AndIsNoted()
    {
        var document = Parse("A=value # trailing");

        Assert.Equal("value", Assert.Single(document.Variables).Value);
        var note = Assert.Single(document.Notes);
        Assert.Equal(DotEnvNoteKind.InlineCommentRemoved, note.Kind);
        Assert.Equal("A", note.Key);
    }

    [Fact]
    public void AQuotedValueMayHaveATrailingComment() =>
        Assert.Equal("v", Only("A=\"v\"   # why"));

    [Fact]
    public void AQuotedValue_KeepsAHashItContains()
    {
        var document = Parse("A=\"a # b\"");

        Assert.Equal("a # b", Assert.Single(document.Variables).Value);
        Assert.Empty(document.Notes);
    }

    // ---- problems ----------------------------------------------------------------------

    [Fact]
    public void TextAfterTheClosingQuote_IsAProblem()
    {
        var document = Parse("A=\"a\"b");

        Assert.Contains("after the closing quote", Assert.Single(document.Problems).Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    public void AnUnterminatedQuote_IsAProblem_NamingTheLineItOpenedOn(char quote)
    {
        var document = Parse($"A=1\nB={quote}never closed\nC=3\n");

        var problem = Assert.Single(document.Problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("never closed", problem.Message, StringComparison.Ordinal);
        Assert.Contains("'B'", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("KEY")]
    [InlineData("export KEY")]
    [InlineData("KEY: value")]
    public void ALineWithNoEquals_IsAProblem(string line)
    {
        Assert.Contains("expected KEY=value", Assert.Single(Parse(line).Problems).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyKey_IsAProblem() =>
        Assert.Contains("name is empty", Assert.Single(Parse("=value").Problems).Message,
            StringComparison.Ordinal);

    /// <summary>
    /// dotenv's key pattern allows <c>-</c> and <c>.</c>. A variable named that way cannot be
    /// exported to a child process, which is the only reason to store it (D-0014).
    /// </summary>
    [Theory]
    [InlineData("FOO-BAR=v")]
    [InlineData("foo.bar=v")]
    [InlineData("1FOO=v")]
    [InlineData("FOO BAR=v")]
    public void AKeyOutsideThePosixRule_IsAProblem(string line) =>
        Assert.Contains("not a valid environment variable name",
            Assert.Single(Parse(line).Problems).Message, StringComparison.Ordinal);

    /// <summary>
    /// dotenv keeps the first, godotenv keeps the last. Since the two disagree there is no answer
    /// to give, so it fails closed — the same reasoning as <see cref="EnvStore.Read"/>.
    /// </summary>
    [Fact]
    public void ADuplicateKey_IsAProblem_NamingBothLines()
    {
        var problem = Assert.Single(Parse("A=1\nB=2\nA=3\n").Problems);

        Assert.Equal(3, problem.Line);
        Assert.Contains("more than once", problem.Message, StringComparison.Ordinal);
        Assert.Contains("line 1", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANulInAValue_IsAProblem() =>
        Assert.Contains("NUL", Assert.Single(Parse("A=\"a\0b\"").Problems).Message,
            StringComparison.Ordinal);

    /// <summary>
    /// Fail-closed plus one-error-per-run would be five round trips on a forty-line file, so the
    /// scanner resumes at the next line rather than giving up.
    /// </summary>
    [Fact]
    public void EveryProblemIsReported_NotJustTheFirst()
    {
        var document = Parse("A-B=1\nGOOD=2\n=3\nnoequals\n");

        Assert.Equal(3, document.Problems.Count);
        Assert.Equal([1, 3, 4], document.Problems.Select(p => p.Line));
        Assert.Equal("GOOD", Assert.Single(document.Variables).Key);
    }

    /// <summary>
    /// The backstop for the rule that makes fail-closed safe to print: the obvious phrasing of
    /// "unterminated quote on line 7" includes the line, and the line is the secret.
    /// </summary>
    [Fact]
    public void ProblemMessages_NeverContainAValue()
    {
        const string Sentinel = "SENTINEL-VALUE-8c41";

        var document = Parse(
            $"A-B=\"{Sentinel}\"\n" +
            $"=\"{Sentinel}\"\n" +
            $"no equals {Sentinel}\n" +
            $"DUP={Sentinel}\nDUP={Sentinel}\n" +
            $"OPEN=\"{Sentinel}\n");

        Assert.NotEmpty(document.Problems);
        foreach (var problem in document.Problems)
        {
            Assert.DoesNotContain(Sentinel, problem.Message, StringComparison.Ordinal);
        }
    }

    // ---- notes -------------------------------------------------------------------------

    /// <summary>
    /// Expanding against the importing machine would bake one laptop's environment into a vault
    /// that is synced elsewhere; expanding against the vault invents an evaluation order KDBX
    /// does not have. Both are guessing about a secret, so the text is stored as written.
    /// </summary>
    [Theory]
    [InlineData("A=${FOO}/x", "${FOO}/x")]
    [InlineData("A=$FOO", "$FOO")]
    [InlineData("A=\"prefix-${FOO}\"", "prefix-${FOO}")]
    public void InterpolationIsStoredLiterally_AndNoted(string line, string expected)
    {
        var document = Parse(line);

        Assert.Equal(expected, Assert.Single(document.Variables).Value);
        Assert.Equal(DotEnvNoteKind.LiteralInterpolation, Assert.Single(document.Notes).Kind);
    }

    [Theory]
    [InlineData("A=costs $5")]
    [InlineData("A=trailing$")]
    public void ADollarThatIsNotAVariableReference_IsNotNoted(string line) =>
        Assert.Empty(Parse(line).Notes);

    [Fact]
    public void NullText_Throws() =>
        Assert.Throws<ArgumentNullException>(() => DotEnv.TryParse(null!, out _));

    // ---- the golden file ---------------------------------------------------------------

    /// <summary>
    /// One file exercising every rule at once, because the rules interact and a table of
    /// single-line cases would not catch that.
    /// </summary>
    [Fact]
    public void ParsesARealisticFile()
    {
        var body = string.Join("\r\n",
            "# Database",
            "export DATABASE_URL=postgres://user:p#ss@localhost:5432/app",
            "",
            "  PORT = 8080   # the dev port",
            "NODE_ENV='production'",
            "EMPTY=",
            "MESSAGE=\"line one\\nline two\"",
            "WINDOWS_PATH=\"C:\\logs\\app\"",
            "LITERAL_TEMPLATE=${NOT_EXPANDED}",
            "PRIVATE_KEY=\"-----BEGIN PRIVATE KEY-----",
            "MIIEvQIBADANBg",
            "-----END PRIVATE KEY-----\"",
            "SHELL_ISH=`a 'b' \"c\"`",
            "");

        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(body)];
        Assert.True(DotEnv.TryDecode(bytes, out var text, out _));

        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DATABASE_URL"] = "postgres://user:p#ss@localhost:5432/app",
            ["PORT"] = "8080",
            ["NODE_ENV"] = "production",
            ["EMPTY"] = string.Empty,
            ["MESSAGE"] = "line one\nline two",
            ["WINDOWS_PATH"] = "C:\\logs\\app",
            ["LITERAL_TEMPLATE"] = "${NOT_EXPANDED}",
            ["PRIVATE_KEY"] = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----",
            ["SHELL_ISH"] = "a 'b' \"c\"",
        }, Map(text));
    }
}
