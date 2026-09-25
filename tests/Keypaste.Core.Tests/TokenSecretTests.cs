using System.Buffers.Text;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A scoped token's text: one spelling per token, read by position, and two derivations its secret never meets twice.</summary>
public sealed class TokenSecretTests
{
    private static readonly string _sample = "kpt_7d2e91c0_" + Base64Url.EncodeToString(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void New_IsPrefixIdAndA43CharSecret_FromDistinctDraws()
    {
        var first = TokenSecret.New(out var id, out var secret);
        var second = TokenSecret.New(out var otherId, out var otherSecret);

        Assert.Equal(TokenSecret.Length, first.Length);
        Assert.StartsWith("kpt_" + id + "_", first, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{8}$", id);
        Assert.Equal(43, first[13..].Length);
        Assert.Equal(32, secret.Length);
        Assert.NotEqual(first, second);
        Assert.False(secret.AsSpan().SequenceEqual(otherSecret));
        Assert.True(id != otherId || first[13..] != second[13..]);
    }

    public static TheoryData<string> Malformed => new()
    {
        "kpx_7d2e91c0_" + _sample[13..],
        "KPT_7d2e91c0_" + _sample[13..],
        "kpt_7d2e91c_" + _sample[13..] + "A",
        "kpt_7d2e91c00_" + _sample[14..],
        "kpt_7D2E91C0_" + _sample[13..],
        _sample[..^1],
        _sample + "A",
        "kpt_7d2e91c0_" + new string('!', 43),
        "kpt_7d2e91c0-" + _sample[13..],
        _sample[..^1] + "9",
        string.Empty,
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void TryParse_Rejects(string token)
    {
        Assert.False(TokenSecret.TryParse(token, out var id, out var secret));
        Assert.Null(id);
        Assert.Null(secret);
    }

    [Fact]
    public void ASecretContainingUnderscoreAndDash_Parses()
    {
        var secret = Enumerable.Repeat((byte)0xFB, 32).ToArray();
        secret[5] = 0xFF;
        var text = Base64Url.EncodeToString(secret);
        Assert.Contains('_', text);
        Assert.Contains('-', text);

        Assert.True(TokenSecret.TryParse("kpt_0badcafe_" + text, out var id, out var parsed));
        Assert.Equal("0badcafe", id);
        Assert.Equal(secret, parsed);
    }

    [Fact]
    public void EveryMintedToken_Parses()
    {
        for (var i = 0; i < 1000; i++)
        {
            var token = TokenSecret.New(out var id, out var secret);

            Assert.True(TokenSecret.TryParse(token, out var parsedId, out var parsed), token);
            Assert.Equal(id, parsedId);
            Assert.Equal(secret, parsed);
        }
    }

    [Fact]
    public void VerifierAndBundleKey_AreDifferentDerivations()
    {
        Assert.True(TokenSecret.TryParse(_sample, out var id, out var secret));

        var verifier = Convert.FromHexString(TokenSecret.Verifier(id, secret));
        var key = TokenSecret.BundleKey(secret, System.Text.Encoding.UTF8.GetBytes("keypaste token " + id));

        Assert.Equal(32, verifier.Length);
        Assert.NotEqual(verifier, key);
        Assert.NotEqual(secret, verifier);
    }

    [Fact]
    public void Verifier_DependsOnTheId()
    {
        Assert.True(TokenSecret.TryParse(_sample, out var id, out var secret));

        Assert.Equal(TokenSecret.Verifier(id, secret), TokenSecret.Verifier(id, secret));
        Assert.NotEqual(TokenSecret.Verifier(id, secret), TokenSecret.Verifier("00000000", secret));
    }
}
