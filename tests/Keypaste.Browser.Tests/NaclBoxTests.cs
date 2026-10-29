using System.Security.Cryptography;
using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>
/// The box gives libsodium's known answers and opens and seals what TweetNaCl.js, KeePassXC-Browser's NaCl, sealed (D-0611,
/// D-0613), here through the shared library <c>scripts/build-libsodium.sh</c> builds beside these tests; the NativeAOT
/// <c>keypaste</c> runs the same answers with the archive linked into it.
/// </summary>
public sealed class NaclBoxTests
{
    [Fact]
    public void The_libsodium_these_tests_load_is_the_one_beside_them() =>
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, Sodium.SharedFileName)),
            $"no {Sodium.SharedFileName} beside the tests: run scripts/build-libsodium.sh before building them");

    [Fact]
    public void Alice_s_box_to_Bob_is_libsodium_s_known_answer() =>
        Assert.Equal(KnownAnswers.Box.ToArray(), NaclBox.Seal(KnownAnswers.Message, KnownAnswers.Nonce, KnownAnswers.BobPublicKey, KnownAnswers.AliceSecretKey));

    [Fact]
    public void Bob_and_Alice_each_open_the_box_to_its_message()
    {
        Assert.Equal(KnownAnswers.Message.ToArray(), NaclBox.Open(KnownAnswers.Box, KnownAnswers.Nonce, KnownAnswers.AlicePublicKey, KnownAnswers.BobSecretKey));
        Assert.Equal(KnownAnswers.Message.ToArray(), NaclBox.Open(KnownAnswers.Box, KnownAnswers.Nonce, KnownAnswers.BobPublicKey, KnownAnswers.AliceSecretKey));
    }

    [Fact]
    public void An_empty_message_seals_to_its_tag_alone_and_opens_empty()
    {
        Assert.Equal(KnownAnswers.EmptyBox.ToArray(), NaclBox.Seal([], KnownAnswers.Nonce, KnownAnswers.BobPublicKey, KnownAnswers.AliceSecretKey));
        Assert.Equal(Array.Empty<byte>(), NaclBox.Open(KnownAnswers.EmptyBox, KnownAnswers.Nonce, KnownAnswers.AlicePublicKey, KnownAnswers.BobSecretKey));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(146)]
    public void A_box_with_one_byte_changed_is_refused(int position)
    {
        var changed = KnownAnswers.Box.ToArray();
        changed[position] ^= 0x80;

        Assert.Null(NaclBox.Open(changed, KnownAnswers.Nonce, KnownAnswers.AlicePublicKey, KnownAnswers.BobSecretKey));
    }

    [Fact]
    public void A_box_cut_short_or_shorter_than_its_tag_is_refused()
    {
        Assert.Null(NaclBox.Open(KnownAnswers.Box[..^1], KnownAnswers.Nonce, KnownAnswers.AlicePublicKey, KnownAnswers.BobSecretKey));
        Assert.Null(NaclBox.Open(KnownAnswers.Box[..(NaclBox.TagLength - 1)], KnownAnswers.Nonce, KnownAnswers.AlicePublicKey, KnownAnswers.BobSecretKey));
    }

    [Fact]
    public void A_box_is_refused_under_another_nonce_or_another_sender()
    {
        var nonce = KnownAnswers.Nonce.ToArray();
        nonce[^1] ^= 0x01;

        Assert.Null(NaclBox.Open(KnownAnswers.Box, nonce, KnownAnswers.AlicePublicKey, KnownAnswers.BobSecretKey));
        Assert.Null(NaclBox.Open(KnownAnswers.Box, KnownAnswers.Nonce, KnownAnswers.BobPublicKey, KnownAnswers.BobSecretKey));
    }

    [Fact]
    public void A_public_key_of_small_order_is_refused_both_ways()
    {
        Assert.Null(NaclBox.Open(KnownAnswers.Box, KnownAnswers.Nonce, KnownAnswers.SmallOrderPublicKey, KnownAnswers.BobSecretKey));
        Assert.Throws<CryptographicException>(() => NaclBox.Seal(KnownAnswers.Message, KnownAnswers.Nonce, KnownAnswers.SmallOrderPublicKey, KnownAnswers.AliceSecretKey));
    }

    [Fact]
    public void The_box_TweetNaCl_js_sealed_opens_to_the_extension_s_request() =>
        Assert.Equal(
            KnownAnswers.TweetNaClRequest.ToArray(),
            NaclBox.Open(KnownAnswers.TweetNaClBox, KnownAnswers.TweetNaClNonce, KnownAnswers.TweetNaClExtensionPublicKey, KnownAnswers.TweetNaClHostSecretKey));

    [Fact]
    public void The_request_seals_to_the_box_TweetNaCl_js_sealed() =>
        Assert.Equal(
            KnownAnswers.TweetNaClBox.ToArray(),
            NaclBox.Seal(KnownAnswers.TweetNaClRequest, KnownAnswers.TweetNaClNonce, KnownAnswers.TweetNaClHostPublicKey, KnownAnswers.TweetNaClExtensionSecretKey));

    [Fact]
    public void New_key_pairs_differ_and_each_opens_what_the_other_sealed()
    {
        using var host = NaclBox.NewKeyPair();
        using var extension = NaclBox.NewKeyPair();

        Assert.Equal(NaclBox.KeyLength, host.PublicKey.Length);
        Assert.False(host.PublicKey.SequenceEqual(extension.PublicKey));
        Assert.False(host.SecretKey.SequenceEqual(extension.SecretKey));

        var request = NaclBox.Seal(KnownAnswers.TweetNaClRequest, KnownAnswers.Nonce, host.PublicKey, extension.SecretKey);
        Assert.Equal(KnownAnswers.TweetNaClRequest.ToArray(), NaclBox.Open(request, KnownAnswers.Nonce, extension.PublicKey, host.SecretKey));

        var reply = NaclBox.Seal(KnownAnswers.Message, KnownAnswers.Nonce, extension.PublicKey, host.SecretKey);
        Assert.Equal(KnownAnswers.Message.ToArray(), NaclBox.Open(reply, KnownAnswers.Nonce, host.PublicKey, extension.SecretKey));
    }

    [Fact]
    public void A_disposed_key_pair_gives_no_secret_key()
    {
        var pair = NaclBox.NewKeyPair();
        pair.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pair.SecretKey.Length);
    }

    [Theory]
    [InlineData(23, 32, 32, "nonce")]
    [InlineData(24, 31, 32, "publicKey")]
    [InlineData(24, 32, 33, "secretKey")]
    public void A_nonce_or_key_of_the_wrong_length_is_refused(int nonce, int publicKey, int secretKey, string parameter)
    {
        var seal = Assert.Throws<ArgumentException>(() => NaclBox.Seal(KnownAnswers.Message, new byte[nonce], new byte[publicKey], new byte[secretKey]));
        var open = Assert.Throws<ArgumentException>(() => NaclBox.Open(KnownAnswers.Box, new byte[nonce], new byte[publicKey], new byte[secretKey]));

        Assert.Equal(parameter, seal.ParamName);
        Assert.Equal(parameter, open.ParamName);
    }

    [Fact]
    public void The_selftest_passes_and_says_what_it_checked()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Assert.Equal(0, BrowserEntry.SelfTest(stdout, stderr));
        Assert.StartsWith("keypaste: selftest ok: ", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
        Assert.Null(KnownAnswers.Disagreement());
    }
}
