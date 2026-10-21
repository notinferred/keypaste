using Keypaste.Core.Approval;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An item's TOTP reads from KeePassXC's <c>otp</c> URI or KeePass 2's <c>TimeOtp-*</c> fields, computes RFC 6238's
/// codes, and refuses what it cannot compute as written, naming a field and never a value (9.2a).
/// </summary>
public sealed class TotpSpecTests
{
    private const string _seed = "JBSWY3DPEHPK3PXP";
    private const string _seedHex = "48 65 6c 6c 6f 21 de ad be ef";
    private const string _seedBase64 = "SGVsbG8h3q2+7w==";

    private const string _rfcSha1 = "12345678901234567890";
    private const string _rfcSha256 = "12345678901234567890123456789012";
    private const string _rfcSha512 = "1234567890123456789012345678901234567890123456789012345678901234";

    private static readonly string _rfcSha1Base32 = Repeat("GEZDGNBVGY3TQOJQ", 2);
    private static readonly string _rfcSha256Base32 = Repeat("GEZDGNBVGY3TQOJQ", 3) + "GEZA";
    private static readonly string _rfcSha512Base32 = Repeat("GEZDGNBVGY3TQOJQ", 6) + "GEZDGNA";

    [Theory]
    [InlineData(59L, "94287082", "46119246", "90693936")]
    [InlineData(1111111109L, "07081804", "68084774", "25091201")]
    [InlineData(1111111111L, "14050471", "67062674", "99943326")]
    [InlineData(1234567890L, "89005924", "91819424", "93441116")]
    [InlineData(2000000000L, "69279037", "90698825", "38618901")]
    [InlineData(20000000000L, "65353130", "77737706", "47863826")]
    public void KeePass_2_fields_compute_RFC_6238_appendix_B(long seconds, string sha1, string sha256, string sha512)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(seconds);

        Assert.Equal(sha1, KeePass2Code(_rfcSha1, "HMAC-SHA-1", at));
        Assert.Equal(sha256, KeePass2Code(_rfcSha256, "HMAC-SHA-256", at));
        Assert.Equal(sha512, KeePass2Code(_rfcSha512, "HMAC-SHA-512", at));
    }

    [Theory]
    [InlineData(59L, "94287082", "46119246", "90693936")]
    [InlineData(1111111109L, "07081804", "68084774", "25091201")]
    [InlineData(1111111111L, "14050471", "67062674", "99943326")]
    [InlineData(1234567890L, "89005924", "91819424", "93441116")]
    [InlineData(2000000000L, "69279037", "90698825", "38618901")]
    [InlineData(20000000000L, "65353130", "77737706", "47863826")]
    public void An_otp_uri_computes_RFC_6238_appendix_B(long seconds, string sha1, string sha256, string sha512)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(seconds);

        Assert.Equal(sha1, UriCode($"otpauth://totp/RFC?secret={_rfcSha1Base32}&digits=8", at));
        Assert.Equal(sha256, UriCode($"otpauth://totp/RFC?secret={_rfcSha256Base32}&algorithm=SHA256&digits=8", at));
        Assert.Equal(sha512, UriCode($"otpauth://totp/RFC?secret={_rfcSha512Base32}&algorithm=SHA512&digits=8", at));
    }

    [Fact]
    public void Six_and_seven_digits_are_the_low_digits_of_RFC_4226s_value_and_the_remaining_time_counts_down()
    {
        using var six = Parsed($"otpauth://totp/RFC?secret={_rfcSha1Base32}");
        using var seven = Parsed($"otpauth://totp/RFC?secret={_rfcSha1Base32}&digits=7");

        var start = six.Code(DateTimeOffset.FromUnixTimeSeconds(30));
        var end = six.Code(DateTimeOffset.FromUnixTimeMilliseconds(59_500));

        Assert.Equal("287082", start.Value);
        Assert.Equal(TimeSpan.FromSeconds(30), start.Remaining);
        Assert.Equal("287082", end.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(500), end.Remaining);
        Assert.Equal("755224", six.Code(DateTimeOffset.UnixEpoch).Value);
        Assert.Equal("4287082", seven.Code(DateTimeOffset.FromUnixTimeSeconds(59)).Value);
    }

    [Fact]
    public void A_code_is_read_from_the_injected_clock()
    {
        using var spec = Parsed($"otpauth://totp/RFC?secret={_rfcSha1Base32}&digits=8");
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(59));

        Assert.Equal("94287082", spec.Code(clock).Value);

        clock.Advance(TimeSpan.FromSeconds(1111111109 - 59));
        Assert.Equal("07081804", spec.Code(clock).Value);
    }

    [Theory]
    [InlineData(0L, "GG5F5")]
    [InlineData(59L, "PV9M4")]
    public void A_steam_encoder_gives_five_of_steams_characters(long seconds, string code)
    {
        using var spec = Parsed($"otpauth://totp/Steam:alice?secret={_rfcSha1Base32}&issuer=Steam&encoder=steam");
        using var withDigits = Parsed($"otpauth://totp/Steam:alice?secret={_rfcSha1Base32}&encoder=Steam&digits=5");

        Assert.True(spec.IsSteam);
        Assert.Equal(TotpSpec.SteamDigits, spec.Digits);
        Assert.Equal(code, spec.Code(DateTimeOffset.FromUnixTimeSeconds(seconds)).Value);
        Assert.Equal(code, withDigits.Code(DateTimeOffset.FromUnixTimeSeconds(seconds)).Value);
    }

    [Theory]
    [InlineData("otpauth://totp/Bank?secret=" + _seed, TotpAlgorithm.Sha1, 6, 30)]
    [InlineData("OTPAUTH://TOTP/Bank?SECRET=jbswy3dpehpk3pxp&Algorithm=sha256&Digits=8&Period=60", TotpAlgorithm.Sha256, 8, 60)]
    [InlineData("otpauth://totp/ACME%20Co:alice@example.com?secret=" + _seed + "&issuer=ACME%20Co&algorithm=SHA512&digits=7&period=15", TotpAlgorithm.Sha512, 7, 15)]
    [InlineData("otpauth://totp?secret=JBSW%20Y3DP%20EHPK%203PXP", TotpAlgorithm.Sha1, 6, 30)]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "====&digits=&period=", TotpAlgorithm.Sha1, 6, 30)]
    [InlineData("  otpauth://totp/Bank?image=https://example.com/a.png&secret=" + _seed + "#ignored ", TotpAlgorithm.Sha1, 6, 30)]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&period=86400", TotpAlgorithm.Sha1, 6, 86400)]
    public void An_otp_uri_reads_its_parameters_in_any_case_with_RFC_defaults(string uri, TotpAlgorithm algorithm, int digits, int period)
    {
        using var spec = Parsed(uri);
        using var plain = Parsed("otpauth://totp/Bank?secret=" + _seed + $"&algorithm={algorithm}&digits={digits}&period={period}");

        Assert.Equal(algorithm, spec.Algorithm);
        Assert.Equal(digits, spec.Digits);
        Assert.Equal(TimeSpan.FromSeconds(period), spec.Period);
        Assert.False(spec.IsSteam);

        var at = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        Assert.Equal(plain.Code(at).Value, spec.Code(at).Value);
    }

    [Theory]
    [InlineData(_seed, "The otp field is not an otpauth://totp/ URI.")]
    [InlineData("https://example.com/?secret=" + _seed, "The otp field is not an otpauth://totp/ URI.")]
    [InlineData("otpauth://totpx/Bank?secret=" + _seed, "The otp field is not an otpauth://totp/ URI.")]
    [InlineData("otpauth://hotp/Bank?secret=" + _seed + "&counter=1", "The otp field holds a counter-based HOTP, which keypaste does not compute.")]
    [InlineData("otpauth://totp/Bank", "The otp field has no secret.")]
    [InlineData("otpauth://totp/Bank?issuer=" + _seed, "The otp field has no secret.")]
    [InlineData("otpauth://totp/Bank?secret=&issuer=Bank", "The otp field has no secret.")]
    [InlineData("otpauth://totp/Bank?secret=JBSWY3DP0HPK3PXP", "The otp field's secret is not base32.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "A", "The otp field's secret is not base32.")]
    [InlineData("otpauth://totp/Bank?secret=JBSWY3DP%3DEHPK3PXP", "The otp field's secret is not base32.")]
    [InlineData("otpauth://totp/Bank?secret=%20%20", "The otp field's secret is not base32.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&algorithm=MD5", "The otp field's algorithm must be SHA1, SHA256 or SHA512.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&algorithm=" + _seed, "The otp field's algorithm must be SHA1, SHA256 or SHA512.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&digits=5", "The otp field's digits must be 6, 7 or 8.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&digits=9", "The otp field's digits must be 6, 7 or 8.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&digits=-6", "The otp field's digits must be 6, 7 or 8.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&digits=six", "The otp field's digits must be 6, 7 or 8.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&period=0", "The otp field's period must be a whole number of seconds from 1 to 86400.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&period=86401", "The otp field's period must be a whole number of seconds from 1 to 86400.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&period=30.5", "The otp field's period must be a whole number of seconds from 1 to 86400.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&encoder=base26", "The otp field's encoder must be steam, the only one keypaste computes.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&encoder=steam&digits=6", "The otp field's digits must be 5 for Steam.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&secret=" + _seed, "The otp field gives its secret more than once.")]
    [InlineData("otpauth://totp/Bank?secret=" + _seed + "&digits=6&DIGITS=8", "The otp field gives its digits more than once.")]
    public void A_malformed_otp_uri_is_refused_naming_the_field_and_never_a_value(string uri, string refusal)
    {
        using var spec = TotpSpec.FromUri(uri, out var reason);

        Assert.Null(spec);
        Assert.Equal(refusal, reason);
        Assert.DoesNotContain(_seed, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JBSWY3DP", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TimeOtp-Secret-Base32", _seed)]
    [InlineData("TimeOtp-Secret-Hex", _seedHex)]
    [InlineData("TimeOtp-Secret-Base64", _seedBase64)]
    public void KeePass_2s_encodings_of_one_seed_compute_one_code(string field, string value)
    {
        using var spec = KeePass2(new() { [field] = value, ["TimeOtp-Secret"] = string.Empty });
        using var reference = Parsed("otpauth://totp/Bank?secret=" + _seed);

        Assert.Equal(TotpAlgorithm.Sha1, spec.Algorithm);
        Assert.Equal(6, spec.Digits);
        Assert.Equal(TimeSpan.FromSeconds(30), spec.Period);

        foreach (var seconds in new long[] { 0, 59, 1111111109, 2000000000 })
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(seconds);
            Assert.Equal(reference.Code(at).Value, spec.Code(at).Value);
        }
    }

    [Fact]
    public void KeePass_2s_algorithm_length_and_period_are_read()
    {
        using var spec = KeePass2(new()
        {
            ["TimeOtp-Secret-Base32"] = _seed,
            ["TimeOtp-Algorithm"] = "hmac-sha-256",
            ["TimeOtp-Length"] = "7",
            ["TimeOtp-Period"] = " 45 ",
        });

        Assert.Equal(TotpAlgorithm.Sha256, spec.Algorithm);
        Assert.Equal(7, spec.Digits);
        Assert.Equal(TimeSpan.FromSeconds(45), spec.Period);
    }

    [Theory]
    [InlineData("TimeOtp-Secret-Hex", "48656", "TimeOtp-Secret-Hex is not hex.")]
    [InlineData("TimeOtp-Secret-Hex", "zz" + _seed, "TimeOtp-Secret-Hex is not hex.")]
    [InlineData("TimeOtp-Secret-Base32", "JBSWY3DP0HPK3PXP", "TimeOtp-Secret-Base32 is not base32.")]
    [InlineData("TimeOtp-Secret-Base64", "not base64 " + _seed, "TimeOtp-Secret-Base64 is not base64.")]
    [InlineData("TimeOtp-Algorithm", "SHA1", "TimeOtp-Algorithm must be HMAC-SHA-1, HMAC-SHA-256 or HMAC-SHA-512.")]
    [InlineData("TimeOtp-Algorithm", "HMAC-MD5", "TimeOtp-Algorithm must be HMAC-SHA-1, HMAC-SHA-256 or HMAC-SHA-512.")]
    [InlineData("TimeOtp-Length", "5", "TimeOtp-Length must be 6, 7 or 8.")]
    [InlineData("TimeOtp-Length", "9", "TimeOtp-Length must be 6, 7 or 8.")]
    [InlineData("TimeOtp-Length", "+6", "TimeOtp-Length must be 6, 7 or 8.")]
    [InlineData("TimeOtp-Period", "0", "TimeOtp-Period must be a whole number of seconds from 1 to 86400.")]
    [InlineData("TimeOtp-Period", "-30", "TimeOtp-Period must be a whole number of seconds from 1 to 86400.")]
    [InlineData("TimeOtp-Period", "86401", "TimeOtp-Period must be a whole number of seconds from 1 to 86400.")]
    [InlineData("TimeOtp-Secret-Hex", _seedHex, "The item has more than one TimeOtp-Secret field.")]
    [InlineData("TimeOtp-Secret", _seed, "The item has more than one TimeOtp-Secret field.")]
    public void A_malformed_KeePass_2_field_is_refused_naming_the_field_and_never_a_value(string field, string value, string refusal)
    {
        Dictionary<string, string> fields = field.StartsWith("TimeOtp-Secret", StringComparison.Ordinal) && refusal.StartsWith(field, StringComparison.Ordinal)
            ? new() { [field] = value }
            : new() { ["TimeOtp-Secret-Base32"] = _seed, [field] = value };

        using var spec = TotpSpec.Read(fields, out var reason);

        Assert.Null(spec);
        Assert.Equal(refusal, reason);
        Assert.DoesNotContain(_seed, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JBSWY3DP", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TimeOtp-Length", "8")]
    [InlineData("TimeOtp-Secret", "")]
    [InlineData("otp", "  ")]
    [InlineData("TOTP Seed", _seed)]
    public void An_item_with_no_secret_has_no_TOTP_and_no_refusal(string field, string value)
    {
        using var spec = TotpSpec.Read(new Dictionary<string, string> { [field] = value }, out var refusal);

        Assert.Null(spec);
        Assert.Null(refusal);
    }

    [Fact]
    public void Otp_is_read_before_KeePass_2s_fields_and_its_refusal_stands()
    {
        using var spec = TotpSpec.Read(
            new Dictionary<string, string> { ["otp"] = "otpauth://totp/Bank?secret=" + _seed + "&digits=8", ["TimeOtp-Secret-Base32"] = _seed },
            out var refusal);

        Assert.Null(refusal);
        Assert.Equal(8, spec!.Digits);

        using var refused = TotpSpec.Read(
            new Dictionary<string, string> { ["otp"] = "otpauth://totp/Bank?secret=" + _seed + "&digits=9", ["TimeOtp-Secret-Base32"] = _seed },
            out var malformed);

        Assert.Null(refused);
        Assert.Equal("The otp field's digits must be 6, 7 or 8.", malformed);
    }

    [Theory]
    [InlineData("otp", true)]
    [InlineData("TimeOtp-Secret", true)]
    [InlineData("TimeOtp-Period", true)]
    [InlineData("OTP", false)]
    [InlineData("timeotp-secret", false)]
    [InlineData("HmacOtp-Secret", false)]
    [InlineData("TOTP Seed", false)]
    public void Only_otp_and_TimeOtp_fields_are_read_matched_exactly(string name, bool read) =>
        Assert.Equal(read, TotpSpec.IsSourceField(name));

    [Theory]
    [InlineData("otp")]
    [InlineData("TimeOtp-Secret")]
    [InlineData("TimeOtp-Secret-Hex")]
    [InlineData("TimeOtp-Secret-Base32")]
    [InlineData("TimeOtp-Secret-Base64")]
    public void No_field_a_seed_is_read_from_leaves_by_name(string name) =>
        Assert.False(CredentialFields.IsReleasable(name));

    [Theory]
    [InlineData("MY", new byte[] { 0x66 })]
    [InlineData("MZXQ", new byte[] { 0x66, 0x6F })]
    [InlineData("MZXW6", new byte[] { 0x66, 0x6F, 0x6F })]
    [InlineData("MZXW6YQ", new byte[] { 0x66, 0x6F, 0x6F, 0x62 })]
    [InlineData("MZXW6YTB", new byte[] { 0x66, 0x6F, 0x6F, 0x62, 0x61 })]
    [InlineData("mzxw6ytboi======", new byte[] { 0x66, 0x6F, 0x6F, 0x62, 0x61, 0x72 })]
    [InlineData("MZXW 6YTB OI", new byte[] { 0x66, 0x6F, 0x6F, 0x62, 0x61, 0x72 })]
    public void Base32_decodes_RFC_4648s_vectors_in_either_case_ignoring_spaces_and_padding(string text, byte[] bytes) =>
        Assert.Equal(bytes, TotpSpec.Base32(text));

    [Theory]
    [InlineData("")]
    [InlineData("M")]
    [InlineData("MZX")]
    [InlineData("MZXW6Y")]
    [InlineData("MZ=XW")]
    [InlineData("MZXW1")]
    [InlineData("MZXW-6YTB")]
    [InlineData("====")]
    public void Base32_refuses_other_characters_and_groups_no_encoder_writes(string text) =>
        Assert.Null(TotpSpec.Base32(text));

    [Fact]
    public void Neither_a_spec_nor_a_code_shows_the_seed_or_the_code()
    {
        using var spec = Parsed($"otpauth://totp/Bank?secret={_rfcSha1Base32}&digits=8");
        var code = spec.Code(DateTimeOffset.FromUnixTimeSeconds(59));

        Assert.Equal("TOTP: Sha1, 8 digits every 30 s", spec.ToString());
        Assert.Equal("TOTP code", code.ToString());
        Assert.DoesNotContain("94287082", code.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_rfcSha1Base32, spec.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_rfcSha1, spec.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Disposing_zeroes_the_seed_and_refuses_another_code()
    {
        var spec = Parsed("otpauth://totp/Bank?secret=" + _seed);
        Assert.False(spec.SeedIsZeroed);

        spec.Dispose();
        spec.Dispose();

        Assert.True(spec.SeedIsZeroed);
        var refused = Assert.Throws<ObjectDisposedException>(() => spec.Code(DateTimeOffset.UnixEpoch));
        Assert.DoesNotContain(_seed, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_moment_before_1970_is_refused_without_the_seed()
    {
        using var spec = Parsed("otpauth://totp/Bank?secret=" + _seed);

        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => spec.Code(DateTimeOffset.UnixEpoch.AddTicks(-1)));
        Assert.DoesNotContain(_seed, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TotpSpec Parsed(string uri)
    {
        var spec = TotpSpec.FromUri(uri, out var refusal);
        Assert.Null(refusal);
        return spec!;
    }

    private static TotpSpec KeePass2(Dictionary<string, string> fields)
    {
        var spec = TotpSpec.FromKeePass2(fields, out var refusal);
        Assert.Null(refusal);
        return spec!;
    }

    private static string UriCode(string uri, DateTimeOffset at)
    {
        using var spec = Parsed(uri);
        return spec.Code(at).Value;
    }

    private static string KeePass2Code(string seed, string algorithm, DateTimeOffset at)
    {
        using var spec = KeePass2(new() { ["TimeOtp-Secret"] = seed, ["TimeOtp-Algorithm"] = algorithm, ["TimeOtp-Length"] = "8" });
        return spec.Code(at).Value;
    }

    private static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
}
