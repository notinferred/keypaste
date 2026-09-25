using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A bundle opens only with its own token, only whole, and only before its authenticated expiry.
/// </summary>
public sealed class TokenBundleTests
{
    private const string _value = "postgres://bundle-sentinel@db/app";
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _token;
    private readonly TokenInfo _info;
    private readonly byte[] _secret;

    public TokenBundleTests()
    {
        _token = TokenSecret.New(out var id, out _secret);
        Assert.True(TokenScope.TryParseList("read:acme-api/staging/*,read:web/dev/KEY", out var scopes, out _));
        _info = new TokenInfo(id, "ci-staging", scopes, _now, _now.AddDays(30), AllowProd: false);
    }

    private byte[] Sealed() => TokenBundle.Seal(
        _info,
        _secret,
        [
            new BundledSet("acme-api", "staging", [new EnvVariable("DATABASE_URL", _value), new EnvVariable("EMPTY", string.Empty)]),
            new BundledSet("web", "dev", [new EnvVariable("KEY", "web-key")]),
        ],
        _now);

    [Fact]
    public void RoundTrip()
    {
        Assert.True(TokenBundle.TryOpen(Sealed(), _token, _now.AddDays(1), out var contents, out var error), error);

        Assert.Equal(_info.Id, contents.TokenId);
        Assert.Equal(_info.Expires, contents.Expires);
        Assert.Equal([("acme-api", "staging"), ("web", "dev")], contents.Pairs);

        var resolved = contents.Resolve("acme-api", "staging");
        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal("staging", resolved.Profile);
        Assert.Equal([new EnvVariable("DATABASE_URL", _value), new EnvVariable("EMPTY", string.Empty)], resolved.Variables);
    }

    [Fact]
    public void AnotherToken_DoesNotOpen()
    {
        var other = TokenSecret.Prefix + _info.Id + "_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        Assert.False(TokenBundle.TryOpen(Sealed(), other, _now, out var contents, out var error));
        Assert.Null(contents);
        Assert.Equal("the bundle does not open with this token", error);
    }

    [Fact]
    public void AnotherTokensId_IsRefusedBeforeDecrypting()
    {
        var other = TokenSecret.New(out _, out _);

        Assert.False(TokenBundle.TryOpen(Sealed(), other, _now, out _, out var error));
        Assert.Equal("the bundle was made for another token", error);
    }

    [Fact]
    public void TamperedHeader_FailsAuthentication()
    {
        var outer = Outer(Sealed());
        var header = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(outer["header"]));
        var later = header.Replace(TokenStore.Timestamp(_info.Expires), TokenStore.Timestamp(_info.Expires.AddYears(5)), StringComparison.Ordinal);
        Assert.NotEqual(header, later);
        outer["header"] = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(later));

        Assert.False(TokenBundle.TryOpen(Rebuilt(outer), _token, _now, out _, out var error));
        Assert.Equal("the bundle does not open with this token", error);
    }

    [Fact]
    public void TamperedCiphertext_Fails()
    {
        var outer = Outer(Sealed());
        var ciphertext = Base64Url.DecodeFromChars(outer["ct"]);
        ciphertext[3] ^= 0x01;
        outer["ct"] = Base64Url.EncodeToString(ciphertext);

        Assert.False(TokenBundle.TryOpen(Rebuilt(outer), _token, _now, out _, out var error));
        Assert.Equal("the bundle does not open with this token", error);
    }

    [Fact]
    public void Expired_IsRefused()
    {
        Assert.True(TokenBundle.TryOpen(Sealed(), _token, _info.Expires.AddSeconds(-1), out _, out _));
        Assert.False(TokenBundle.TryOpen(Sealed(), _token, _info.Expires, out var contents, out var error));
        Assert.Null(contents);
        Assert.Contains("expired", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileHoldsNoPlaintextValue()
    {
        var file = Sealed();
        var text = Encoding.UTF8.GetString(file);

        Assert.DoesNotContain(_value, text, StringComparison.Ordinal);
        Assert.DoesNotContain("DATABASE_URL", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_token[13..], text, StringComparison.Ordinal);
        Assert.Equal(-1, file.AsSpan().IndexOf(Encoding.UTF8.GetBytes(_value)));

        using var document = JsonDocument.Parse(file);
        Assert.Equal("keypaste-bundle", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void Oversized_IsRefused()
    {
        var file = new byte[TokenBundle.MaximumBytes + 1];

        Assert.False(TokenBundle.TryOpen(file, _token, _now, out _, out var error));
        Assert.Contains("larger", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_IsRefused()
    {
        Assert.False(TokenBundle.TryOpen("{}"u8, _token, _now, out _, out var notABundle));
        Assert.Equal("that is not a keypaste bundle", notABundle);
        Assert.False(TokenBundle.TryOpen(Sealed(), "kpt_not_a_token", _now, out _, out var notAToken));
        Assert.Equal("that is not a keypaste token", notAToken);
    }

    [Fact]
    public void Resolve_PicksThePair_OrRefuses()
    {
        Assert.True(TokenBundle.TryOpen(Sealed(), _token, _now, out var contents, out _));

        Assert.Equal("web-key", Assert.Single(contents.Resolve("web", null).Variables).Value);
        Assert.Equal(EnvOutcome.Resolved, contents.Resolve(null, "dev").Outcome);
        Assert.Equal(EnvOutcome.Invalid, contents.Resolve(null, null).Outcome);
        Assert.Equal(EnvOutcome.NoProject, contents.Resolve("missing", "dev").Outcome);
        Assert.Equal(EnvOutcome.NoProfile, contents.Resolve("web", "staging").Outcome);
        Assert.Empty(contents.Resolve("web", "staging").Variables);

        var single = TokenBundle.Seal(_info, _secret, [new BundledSet("web", "dev", [new EnvVariable("KEY", "v")])], _now);
        Assert.True(TokenBundle.TryOpen(single, _token, _now, out var one, out _));
        Assert.Equal(EnvOutcome.Resolved, one.Resolve(null, null).Outcome);
    }

    private static Dictionary<string, string> Outer(byte[] file)
    {
        using var document = JsonDocument.Parse(file);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["header"] = document.RootElement.GetProperty("header").GetString()!,
            ["nonce"] = document.RootElement.GetProperty("nonce").GetString()!,
            ["ct"] = document.RootElement.GetProperty("ct").GetString()!,
        };
    }

    private static byte[] Rebuilt(Dictionary<string, string> outer) =>
        Encoding.UTF8.GetBytes(
            $$"""{"v":1,"kind":"keypaste-bundle","header":"{{outer["header"]}}","nonce":"{{outer["nonce"]}}","ct":"{{outer["ct"]}}"}""");
}
