using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The share envelope (D-0354): what it opens with, what it refuses, and that the browser's code
/// opens what this code seals.
/// </summary>
public sealed class ShareCryptoTests
{
    private const string Secret = "sk_live_51HxTheValueThatMustNotLeak";
    private const string Passphrase = "correct horse battery";

    private static SharePayload Payload(string value = Secret) =>
        new("STRIPE_SECRET_KEY", [new ShareField("password", value)], new DateTimeOffset(2026, 9, 24, 14, 40, 0, TimeSpan.Zero));

    private static SharePayload Open(SealedShare sealedShare, string passphrase = "")
    {
        Assert.True(ShareCrypto.TryOpen(sealedShare.Envelope, sealedShare.Key, passphrase, out var payload, out var error), error);
        return payload;
    }

    private static string Edit(string envelope, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(envelope)!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    private static string Flip(string base64url)
    {
        var bytes = Base64Url.DecodeFromChars(base64url);
        bytes[0] ^= 1;
        return Base64Url.EncodeToString(bytes);
    }

    [Fact]
    public void RoundTrip_NoPassphrase()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), ReadOnlySpan<char>.Empty);

        var payload = Open(sealedShare);

        Assert.False(sealedShare.HasPassphrase);
        Assert.Equal("STRIPE_SECRET_KEY", payload.Title);
        var field = Assert.Single(payload.Fields);
        Assert.Equal("password", field.Name);
        Assert.Equal(Secret, field.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 14, 40, 0, TimeSpan.Zero), payload.Created);
    }

    [Fact]
    public void RoundTrip_Passphrase()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), Passphrase);

        Assert.True(sealedShare.HasPassphrase);
        Assert.Equal(Secret, Assert.Single(Open(sealedShare, Passphrase).Fields).Value);
    }

    [Fact]
    public void WrongKey_Fails()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), ReadOnlySpan<char>.Empty);

        Assert.False(ShareCrypto.TryOpen(sealedShare.Envelope, Flip(sealedShare.Key), ReadOnlySpan<char>.Empty, out var payload, out _));
        Assert.Null(payload);
    }

    [Fact]
    public void WrongPassphrase_Fails()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), Passphrase);

        Assert.False(ShareCrypto.TryOpen(sealedShare.Envelope, sealedShare.Key, "correct horse battery!", out _, out _));
    }

    [Fact]
    public void LinkKeyAlone_DoesNotOpenAPassphraseShare()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), Passphrase);

        Assert.False(ShareCrypto.TryOpen(sealedShare.Envelope, sealedShare.Key, ReadOnlySpan<char>.Empty, out _, out var error));
        Assert.Contains("passphrase", error, StringComparison.Ordinal);

        // Nor does the key with the passphrase left out of the derivation, the no-passphrase way.
        var stripped = Edit(sealedShare.Envelope, envelope => envelope["kdf"] = null);
        Assert.False(ShareCrypto.TryOpen(stripped, sealedShare.Key, ReadOnlySpan<char>.Empty, out _, out _));
    }

    [Fact]
    public void LoweredIterations_FailAuthentication()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), Passphrase);
        var lowered = Edit(sealedShare.Envelope, envelope => envelope["kdf"]!["iterations"] = 1000);

        Assert.False(ShareCrypto.TryOpen(lowered, sealedShare.Key, Passphrase, out _, out _));
    }

    [Fact]
    public void SwappedSalt_Fails()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), Passphrase);
        var swapped = Edit(sealedShare.Envelope, envelope => envelope["kdf"]!["salt"] = Flip(envelope["kdf"]!["salt"]!.GetValue<string>()));

        Assert.False(ShareCrypto.TryOpen(swapped, sealedShare.Key, Passphrase, out _, out _));
    }

    [Fact]
    public void FlippedCiphertextBit_Fails()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), ReadOnlySpan<char>.Empty);
        var flipped = Edit(sealedShare.Envelope, envelope => envelope["ct"] = Flip(envelope["ct"]!.GetValue<string>()));

        Assert.False(ShareCrypto.TryOpen(flipped, sealedShare.Key, ReadOnlySpan<char>.Empty, out _, out var error));
        Assert.Contains("altered", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnvelopeHoldsNoPlaintextTitleOrValue()
    {
        foreach (var passphrase in new[] { string.Empty, Passphrase })
        {
            var sealedShare = ShareCrypto.Seal(Payload(), passphrase);

            Assert.DoesNotContain(Secret, sealedShare.Envelope, StringComparison.Ordinal);
            Assert.DoesNotContain("STRIPE", sealedShare.Envelope, StringComparison.Ordinal);
            Assert.DoesNotContain("password", sealedShare.Envelope, StringComparison.Ordinal);
            Assert.DoesNotContain(sealedShare.Key, sealedShare.Envelope, StringComparison.Ordinal);
            Assert.DoesNotContain(passphrase.Length == 0 ? "\u0000" : passphrase, sealedShare.Envelope, StringComparison.Ordinal);
            Assert.DoesNotContain(sealedShare.Key, sealedShare.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, Payload().ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, Payload().Fields[0].ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EachSealUsesFreshKeyIvAndSalt()
    {
        var first = ShareCrypto.Seal(Payload(), Passphrase);
        var second = ShareCrypto.Seal(Payload(), Passphrase);

        using var a = JsonDocument.Parse(first.Envelope);
        using var b = JsonDocument.Parse(second.Envelope);

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(43, first.Key.Length);
        foreach (var name in new[] { "iv", "ct", "check_iv", "check" })
        {
            Assert.NotEqual(a.RootElement.GetProperty(name).GetString(), b.RootElement.GetProperty(name).GetString());
        }

        Assert.NotEqual(
            a.RootElement.GetProperty("kdf").GetProperty("salt").GetString(),
            b.RootElement.GetProperty("kdf").GetProperty("salt").GetString());
        Assert.Equal(ShareCrypto.Pbkdf2Iterations, a.RootElement.GetProperty("kdf").GetProperty("iterations").GetInt32());
    }

    [Fact]
    public void OversizedPayload_IsRefused()
    {
        var big = Payload(new string('x', ShareCrypto.MaximumPlaintextBytes));

        Assert.False(ShareCrypto.Fits(big));
        Assert.Throws<ArgumentException>(() => ShareCrypto.Seal(big, ReadOnlySpan<char>.Empty));
        Assert.True(ShareCrypto.Fits(Payload(new string('x', ShareCrypto.MaximumPlaintextBytes - 200))));
    }

    [Fact]
    public void APassphraseTheViewerCouldNormalizeDifferently_IsRefused()
    {
        Assert.True(ShareCrypto.AcceptsPassphrase("Correct horse, battery! é"));
        Assert.False(ShareCrypto.AcceptsPassphrase("é"));
        Assert.False(ShareCrypto.AcceptsPassphrase("Å"));
        Assert.Throws<ArgumentException>(() => ShareCrypto.Seal(Payload(), "passphrase ✓"));
    }

    [Fact]
    public void CheckVerifiesThePassphraseWithoutTheCiphertext()
    {
        var sealedShare = ShareCrypto.Seal(Payload(), Passphrase);
        var metadata = Edit(sealedShare.Envelope, envelope =>
        {
            envelope.Remove("iv");
            envelope.Remove("ct");
        });

        Assert.True(ShareCrypto.TryCheck(metadata, sealedShare.Key, Passphrase, out var error), error);
        Assert.False(ShareCrypto.TryCheck(metadata, sealedShare.Key, "not the passphrase", out _));
        Assert.False(ShareCrypto.TryCheck(metadata, Flip(sealedShare.Key), Passphrase, out _));
        Assert.False(ShareCrypto.TryOpen(metadata, sealedShare.Key, Passphrase, out _, out _));
    }

    /// <summary>
    /// The vector <c>site/test/share-crypto.test.mjs</c> opens with WebCrypto. Sealing it again here
    /// with the same inputs must give the same bytes, so the two halves cannot drift apart.
    /// </summary>
    [Fact]
    public void MatchesTheCrossLanguageVector()
    {
        var path = Path.Combine(VendoredWordList.RepoRoot(), "site", "test", "share-vector.json");
        using var vector = JsonDocument.Parse(File.ReadAllText(path));

        var cases = vector.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.Equal(2, cases.Count);

        foreach (var sample in cases)
        {
            var expected = sample.GetProperty("payload");
            var payload = new SharePayload(
                expected.GetProperty("title").GetString()!,
                [.. expected.GetProperty("fields").EnumerateArray().Select(f => new ShareField(f.GetProperty("name").GetString()!, f.GetProperty("value").GetString()!))],
                DateTimeOffset.Parse(expected.GetProperty("created").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            var passphrase = sample.GetProperty("passphrase").GetString()!;
            var envelope = sample.GetProperty("envelope").GetString()!;
            var key = sample.GetProperty("key").GetString()!;

            var resealed = ShareCrypto.Seal(
                payload,
                passphrase,
                Base64Url.DecodeFromChars(key),
                Base64Url.DecodeFromChars(sample.GetProperty("iv").GetString()!),
                Base64Url.DecodeFromChars(sample.GetProperty("check_iv").GetString()!),
                Base64Url.DecodeFromChars(sample.GetProperty("salt").GetString()!),
                sample.GetProperty("iterations").GetInt32());

            Assert.Equal(envelope, resealed.Envelope);
            Assert.Equal(key, resealed.Key);

            Assert.True(ShareCrypto.TryOpen(envelope, key, passphrase, out var opened, out var error), error);
            Assert.Equal(payload.Title, opened.Title);
            Assert.Equal(payload.Fields, opened.Fields);
        }
    }
}
