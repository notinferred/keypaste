namespace Keypaste.Browser;

/// <summary>
/// What the box must answer before keypaste trusts it with a browser's messages: libsodium's own known answers, and a box
/// TweetNaCl.js sealed as KeePassXC-Browser seals each request (D-0613).
/// </summary>
internal static class KnownAnswers
{
    // NaCl's box vectors as libsodium 1.0.22 holds them in test/default/box_easy.c and box2.c, with box_easy.exp's answers.
    private const string _aliceSecretKey = "77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a";
    private const string _alicePublicKey = "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a";
    private const string _bobSecretKey = "5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb";
    private const string _bobPublicKey = "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f";
    private const string _nonce = "69696ee955b62b73cd62bda875fc73d68219e0036b7a0b37";
    private const string _smallOrderPublicKey = "e0eb7a7c3b41b8ae1656e3faf19fc46ada098deb9c32b1fd866205165f49b800";
    private const string _message =
        "be075fc53c81f2d5cf141316ebeb0c7b5228c52a4c62cbd44b66849b64244ffce5ecbaaf33bd751a1ac728d45e6c61296cdc3c01233561f41db66cce"
        + "314adb310e3be8250c46f06dceea3a7fa1348057e2f6556ad6b1318a024a838f21af1fde048977eb48f59ffd4924ca1c60902e52f0a089bc7689"
        + "7040e082f937763848645e0705";
    private const string _box =
        "f3ffc7703f9400e52a7dfb4b3d3305d98e993b9f48681273c29650ba32fc76ce48332ea7164d96a4476fb8c531a1186ac0dfc17c98dce87b4da7f0"
        + "11ec48c97271d2c20f9b928fe2270d6fb863d51738b48eeee314a7cc8ab932164548e526ae90224368517acfeabd6bb3732bc0e9da99832b61ca"
        + "01b6de56244a9e88d5f9b37973f622a43d14a6599b1f654cb45a74e355a5";
    private const string _emptyBox = "2539121d8e234e652d651fa4c8cff880";

    // Sealed in dev run 40898854142 by the nacl.min.js KeePassXC-Browser 1.10.4.1 carries, TweetNaCl.js 1.0.3's, with
    // nacl.box from a new extension key pair to a new host key pair under a random nonce, as the extension's client seals
    // a request, and printed in base64 as the extension sends it.
    private const string _tweetNaClExtensionPublicKey = "MQu2Pw2EUqX0XIK3ujeb1Gf9xf0LHcNSVW+uaR7kk1c=";
    private const string _tweetNaClExtensionSecretKey = "9BtqluMI0rE18Rh14zMube/pkAkrwqMNOeBaI9JzbPI=";
    private const string _tweetNaClHostPublicKey = "oWWJa7eYMw5i+6Dcedx0lL2vXoT61t3f0iPV/MM1mTs=";
    private const string _tweetNaClHostSecretKey = "C38LTZOWFdNYPTxv5Pp0uGKRjL+qHWoMpcRi3Obtcg8=";
    private const string _tweetNaClNonce = "tBIIloMdX965RtzLBrUZ3iS4n6wjg6jE";
    private const string _tweetNaClBox = "vDAtuSlKypWR7Pb7/mR5yHK5EhtkrXD0TtW2iOLyXjwdK4KJVroRCseUW7RmGLPM3ot6myTNYXyqSe9o3EUI/A==";

    internal static ReadOnlySpan<byte> AliceSecretKey => Convert.FromHexString(_aliceSecretKey);

    internal static ReadOnlySpan<byte> AlicePublicKey => Convert.FromHexString(_alicePublicKey);

    internal static ReadOnlySpan<byte> BobSecretKey => Convert.FromHexString(_bobSecretKey);

    internal static ReadOnlySpan<byte> BobPublicKey => Convert.FromHexString(_bobPublicKey);

    internal static ReadOnlySpan<byte> Nonce => Convert.FromHexString(_nonce);

    internal static ReadOnlySpan<byte> SmallOrderPublicKey => Convert.FromHexString(_smallOrderPublicKey);

    /// <summary>The 131 bytes Alice seals to Bob.</summary>
    internal static ReadOnlySpan<byte> Message => Convert.FromHexString(_message);

    /// <summary>Alice's box of <see cref="Message"/> to Bob: its tag, then its ciphertext.</summary>
    internal static ReadOnlySpan<byte> Box => Convert.FromHexString(_box);

    /// <summary>Alice's box of no bytes to Bob, which is its tag alone.</summary>
    internal static ReadOnlySpan<byte> EmptyBox => Convert.FromHexString(_emptyBox);

    internal static ReadOnlySpan<byte> TweetNaClExtensionPublicKey => Convert.FromBase64String(_tweetNaClExtensionPublicKey);

    internal static ReadOnlySpan<byte> TweetNaClExtensionSecretKey => Convert.FromBase64String(_tweetNaClExtensionSecretKey);

    internal static ReadOnlySpan<byte> TweetNaClHostPublicKey => Convert.FromBase64String(_tweetNaClHostPublicKey);

    internal static ReadOnlySpan<byte> TweetNaClHostSecretKey => Convert.FromBase64String(_tweetNaClHostSecretKey);

    internal static ReadOnlySpan<byte> TweetNaClNonce => Convert.FromBase64String(_tweetNaClNonce);

    internal static ReadOnlySpan<byte> TweetNaClBox => Convert.FromBase64String(_tweetNaClBox);

    /// <summary>The request the extension sealed: its <c>get-databasehash</c> before any pairing, as JSON in UTF-8.</summary>
    internal static ReadOnlySpan<byte> TweetNaClRequest => """{"action":"get-databasehash","connectedKeys":[]}"""u8;

    /// <summary>The first answer the box got wrong, or <see langword="null"/> when it gave every one.</summary>
    internal static string? Disagreement()
    {
        if (!NaclBox.Seal(Message, Nonce, BobPublicKey, AliceSecretKey).AsSpan().SequenceEqual(Box))
        {
            return "Alice's box to Bob is not libsodium's known answer";
        }

        if (!Opens(Box, Nonce, AlicePublicKey, BobSecretKey, Message))
        {
            return "Bob did not open libsodium's known box to its message";
        }

        if (!NaclBox.Seal([], Nonce, BobPublicKey, AliceSecretKey).AsSpan().SequenceEqual(EmptyBox) || !Opens(EmptyBox, Nonce, AlicePublicKey, BobSecretKey, []))
        {
            return "an empty message did not seal to libsodium's known tag and open again";
        }

        foreach (var position in (int[])[0, NaclBox.TagLength, Box.Length - 1])
        {
            var changed = Box.ToArray();
            changed[position] ^= 0x01;

            if (NaclBox.Open(changed, Nonce, AlicePublicKey, BobSecretKey) is not null)
            {
                return $"a box with byte {position} changed was opened";
            }
        }

        if (NaclBox.Open(Box, Nonce, SmallOrderPublicKey, BobSecretKey) is not null || SealsTo(SmallOrderPublicKey))
        {
            return "a public key of small order was accepted";
        }

        if (!Opens(TweetNaClBox, TweetNaClNonce, TweetNaClExtensionPublicKey, TweetNaClHostSecretKey, TweetNaClRequest))
        {
            return "the box TweetNaCl.js sealed did not open to its request";
        }

        if (!NaclBox.Seal(TweetNaClRequest, TweetNaClNonce, TweetNaClHostPublicKey, TweetNaClExtensionSecretKey).AsSpan().SequenceEqual(TweetNaClBox))
        {
            return "the request did not seal to the box TweetNaCl.js sealed";
        }

        using var ours = NaclBox.NewKeyPair();
        using var theirs = NaclBox.NewKeyPair();

        if (ours.PublicKey.SequenceEqual(theirs.PublicKey))
        {
            return "two new key pairs have one public key";
        }

        return Opens(NaclBox.Seal(Message, Nonce, theirs.PublicKey, ours.SecretKey), Nonce, ours.PublicKey, theirs.SecretKey, Message)
            ? null
            : "a box between two new key pairs did not open";
    }

    private static bool Opens(ReadOnlySpan<byte> box, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey, ReadOnlySpan<byte> message) =>
        NaclBox.Open(box, nonce, publicKey, secretKey) is { } opened && opened.AsSpan().SequenceEqual(message);

    private static bool SealsTo(ReadOnlySpan<byte> publicKey)
    {
        try
        {
            NaclBox.Seal(Message, Nonce, publicKey, BobSecretKey);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
