using System.Text;
using System.Text.RegularExpressions;
using Keypaste.Core.Launch;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What a run returns has every known form of every injected value replaced (D-0359): literal and
/// escaped as common dumps of an environment print them, in the encodings a child may print in, and
/// with no tail of a value left where the kept output was cut.
/// </summary>
public sealed partial class OutputScrubberTests
{
    /// <summary>A value holding every character the common dumps escape differently.</summary>
    internal const string Hostile = "p\"a's\\s$w0rd\nsecond-line-é%40end";

    private static string Scrub(OutputScrubber scrubber, string text, bool headCut = false) =>
        scrubber.Scrub(new CapturedOutput(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text), headCut)).Text;

    private static OutputScrubber For(params (string Name, string Value)[] variables) =>
        OutputScrubber.For([.. variables.Select(variable => new EnvVariable(variable.Name, variable.Value))]);

    [Fact]
    public void ALiteralValue_BecomesItsName()
    {
        var scrubber = For(("DATABASE_URL", "postgres://u:s3cret@db/app"));

        Assert.Equal("url=[keypaste:DATABASE_URL]\n", Scrub(scrubber, "url=postgres://u:s3cret@db/app\n"));
    }

    [Fact]
    public void JsonEscapedAndPercentEncodedForms_AreReplaced()
    {
        const string value = "a\"b\\c<d>&e f+g/é";
        var scrubber = For(("K", value));

        foreach (var form in new[]
        {
            System.Text.Json.JsonEncodedText.Encode(value).Value,
            System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value,
            Uri.EscapeDataString(value),
            Uri.EscapeDataString(value).ToLowerInvariant(),
            Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal),
        })
        {
            Assert.Equal("<[keypaste:K]>", Scrub(scrubber, $"<{form}>"));
        }
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("json-default")]
    [InlineData("json-relaxed")]
    [InlineData("json-python-ascii")]
    [InlineData("python-repr")]
    [InlineData("node-inspect")]
    [InlineData("posix-single")]
    [InlineData("bash-double")]
    [InlineData("bash-ansi-c")]
    [InlineData("percent-upper")]
    [InlineData("percent-lower")]
    [InlineData("form")]
    [InlineData("crlf")]
    public void EveryCommonDumpForm_IsReplaced(string form)
    {
        var scrubber = For(("SECRET", Hostile));
        var printed = form switch
        {
            "literal" => Hostile,
            "json-default" => System.Text.Json.JsonEncodedText.Encode(Hostile).Value,
            "json-relaxed" => System.Text.Json.JsonEncodedText.Encode(Hostile, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value,
            "json-python-ascii" => "p\\\"a's\\\\s$w0rd\\nsecond-line-\\u00e9%40end",
            "python-repr" => "p\"a\\'s\\\\s$w0rd\\nsecond-line-é%40end",
            // What Node 24's console.log(process.env) prints: backticks, since the value holds both quotes.
            "node-inspect" => "p\"a's\\\\s$w0rd\\nsecond-line-é%40end",
            "posix-single" => "p\"a'\\''s\\s$w0rd\nsecond-line-é%40end",
            "bash-double" => "p\\\"a's\\\\s\\$w0rd\nsecond-line-é%40end",
            "bash-ansi-c" => "p\"a\\'s\\\\s$w0rd\\nsecond-line-é%40end",
            "percent-upper" => Uri.EscapeDataString(Hostile),
            "percent-lower" => LowerHex().Replace(Uri.EscapeDataString(Hostile), match => match.Value.ToLowerInvariant()),
            "form" => Uri.EscapeDataString(Hostile).Replace("%20", "+", StringComparison.Ordinal),
            _ => Hostile.Replace("\n", "\r\n", StringComparison.Ordinal),
        };

        var scrubbed = Scrub(scrubber, $"before {printed} after");

        Assert.Equal("before [keypaste:SECRET] after", scrubbed);
    }

    [Fact]
    public void AJsonDumpEscapingSlashes_IsReplaced()
    {
        // An AWS-style secret key, as PHP's json_encode(getenv()) prints it.
        var scrubber = For(("AWS_SECRET_ACCESS_KEY", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"));

        var scrubbed = Scrub(scrubber, "{\"AWS_SECRET_ACCESS_KEY\":\"wJalrXUtnFEMI\\/K7MDENG\\/bPxRfiCYEXAMPLEKEY\"}");

        Assert.Equal("{\"AWS_SECRET_ACCESS_KEY\":\"[keypaste:AWS_SECRET_ACCESS_KEY]\"}", scrubbed);
    }

    [Fact]
    public void EachLongLineOfAMultiLineValue_IsReplacedOnItsOwn()
    {
        var scrubber = For(("PEM", "-----BEGIN KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\nshort\n-----END KEY-----"));

        var scrubbed = Scrub(scrubber, "line 2 alone: MIIEvQIBADANBgkqhkiG9w0BAQEFAASC and short");

        Assert.Equal("line 2 alone: [keypaste:PEM] and short", scrubbed);
    }

    [Fact]
    public void AUrisPassword_IsReplacedRawAndDecoded()
    {
        var scrubber = For(("DATABASE_URL", "postgres://app:p%40ss-w0rd@db:5432/app"));

        Assert.Equal("password [keypaste:DATABASE_URL]", Scrub(scrubber, "password p%40ss-w0rd"));
        Assert.Equal("password [keypaste:DATABASE_URL]", Scrub(scrubber, "password p@ss-w0rd"));
    }

    [Fact]
    public void Utf16Output_IsScrubbedAsBytes()
    {
        var scrubber = For(("K", "pässwort123"));

        foreach (var encoding in new Encoding[] { Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var bytes = encoding.GetBytes("x=pässwort123;");
            var scrubbed = scrubber.Scrub(new CapturedOutput(bytes, bytes.Length, false));

            Assert.Equal(1, scrubbed.Replacements);
            Assert.DoesNotContain("wort", scrubbed.Text.Replace("\0", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnsiCodePageOutput_IsScrubbedAsBytes_OnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows prints in an ANSI code page");

        var scrubber = For(("K", "pässwort123"));
        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes("x=pässwort123;");

        if (!scrubber.Scrub(new CapturedOutput(bytes, bytes.Length, false)).Text.Contains("[keypaste:K]", StringComparison.Ordinal))
        {
            // Only the machine's own ANSI and OEM pages are matched; 1252 is Windows' default in most locales.
            Assert.Skip("this machine's ANSI code page is not 1252");
        }
    }

    [Fact]
    public void OverlappingValues_TheLongestWins_AndNoPartOfEitherSurvives()
    {
        var scrubber = For(("SHORT", "sk_live"), ("LONG", "sk_live_123"), ("A", "abc"), ("B", "bcdefgh"));

        Assert.Equal("[keypaste:LONG]", Scrub(scrubber, "sk_live_123"));
        Assert.Equal("x[keypaste:A][keypaste:B]y", Scrub(scrubber, "xabcdefghy"));
    }

    [Fact]
    public void AMarker_IsNeverRescanned()
    {
        var scrubber = For(("A", "keypaste"), ("B", "[keypaste:A]"));

        Assert.Equal("[keypaste:A] [keypaste:B]", Scrub(scrubber, "keypaste [keypaste:A]"));
    }

    [Fact]
    public void AOneCharacterValue_IsStillScrubbed()
    {
        var scrubber = For(("PIN", "7"));

        Assert.Equal("a[keypaste:PIN]b[keypaste:PIN]", Scrub(scrubber, "a7b7"));
    }

    [Fact]
    public void EmptyValues_AreIgnored()
    {
        var scrubber = For(("EMPTY", string.Empty));

        Assert.Equal("nothing to hide", Scrub(scrubber, "nothing to hide"));
        Assert.Equal(0, scrubber.LongestForm);
    }

    [Fact]
    public void AHeadCut_LeavesNoSuffixOfAValue()
    {
        const string value = "Q7xZ9pW2mK";
        var scrubber = For(("K", value));
        var full = Encoding.UTF8.GetBytes("...." + value + "::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::");

        for (var cut = 1; cut <= 4 + value.Length; cut++)
        {
            var kept = full[cut..];
            var scrubbed = scrubber.Scrub(new CapturedOutput(kept, full.Length, true)).Text;
            var body = scrubbed[(scrubbed.IndexOf('\n', StringComparison.Ordinal) + 1)..];

            foreach (var length in Enumerable.Range(2, value.Length - 1))
            {
                Assert.DoesNotContain(value[^length..], body, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheReturnedTail_SaysItWasCut()
    {
        var scrubber = For(("K", "unused-value"));
        var text = new string('x', OutputScrubber.ReturnedCharacters + 100);

        var scrubbed = scrubber.Scrub(new CapturedOutput(Encoding.UTF8.GetBytes(text), text.Length, false));

        Assert.True(scrubbed.Truncated);
        Assert.StartsWith("[keypaste: this stream carried", scrubbed.Text, StringComparison.Ordinal);
        Assert.EndsWith(new string('x', OutputScrubber.ReturnedCharacters), scrubbed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacementsAreCounted()
    {
        var scrubber = For(("A", "alpha-secret"), ("B", "beta-secret"));

        var scrubbed = scrubber.Scrub(new CapturedOutput(Encoding.UTF8.GetBytes("alpha-secret beta-secret alpha-secret"), 38, false));

        Assert.Equal(3, scrubbed.Replacements);
    }

    [Fact]
    public void NoValueSurvives_RandomPlacement()
    {
        const string valueAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz0123456789\"'\\$%@é";
        const string filler = "-.,:;~ ";
        var random = new Random(4242);

        for (var run = 0; run < 1000; run++)
        {
            var values = Enumerable.Range(0, random.Next(1, 5))
                .Select(i => (Name: $"V{i}", Value: new string([.. Enumerable.Range(0, random.Next(1, 24)).Select(_ => valueAlphabet[random.Next(valueAlphabet.Length)])])))
                .ToArray();
            var scrubber = For(values);

            var text = new StringBuilder();
            foreach (var _ in Enumerable.Range(0, random.Next(1, 6)))
            {
                text.Append(filler[random.Next(filler.Length)], random.Next(0, 40));
                text.Append(values[random.Next(values.Length)].Value);
            }

            text.Append(filler[0], random.Next(0, 40));

            var bytes = Encoding.UTF8.GetBytes(text.ToString());
            var cut = random.Next(0, 2) == 0 ? 0 : random.Next(1, bytes.Length);
            var scrubbed = scrubber.Scrub(new CapturedOutput(bytes[cut..], bytes.Length, cut > 0)).Text;
            var body = Marker().Replace(cut > 0 ? scrubbed[(scrubbed.IndexOf('\n', StringComparison.Ordinal) + 1)..] : scrubbed, " ");

            foreach (var (_, value) in values)
            {
                Assert.DoesNotContain(value, body, StringComparison.Ordinal);
            }

            // Values and filler share no character, so any value character left is a fragment of one.
            Assert.False(body.Any(c => valueAlphabet.Contains(c, StringComparison.Ordinal)), $"{string.Join(" | ", values.Select(v => v.Value))} ## {text} ## cut {cut} ## {scrubbed}");
        }
    }

    [GeneratedRegex("%[0-9A-F]{2}")]
    private static partial Regex LowerHex();

    [GeneratedRegex(@"\[keypaste:V\d+\]")]
    private static partial Regex Marker();
}
