using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.FakeBrowser;
using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>
/// The relay as KeePassXC-Browser meets it, with a fake owner on a real pipe behind it: framing and its bound, the key exchange
/// and the nonces, the errors it answers itself, what reaches the owner, and the lock signals (D-0625 to D-0628).
/// </summary>
public sealed class RelayTests
{
    private const string _pairing = "keypaste on this computer";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_message_is_its_length_in_native_byte_order_then_its_bytes_and_so_is_the_reply()
    {
        await using var rig = new RelayRig();
        var request = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["action"] = "change-public-keys",
            ["publicKey"] = rig.Browser.PublicKey,
            ["nonce"] = ExtensionClient.NewNonce(),
            ["clientID"] = ExtensionClient.NewNonce(),
        }.ToJsonString());
        var header = new byte[sizeof(uint)];
        MemoryMarshal.Write(header, (uint)request.Length);

        // The length and the message arrive apart, as a pipe may deliver them.
        await rig.ToRelay.WriteAsync(header, Token);
        await rig.ToRelay.FlushAsync(Token);
        await rig.ToRelay.WriteAsync(request, Token);
        await rig.ToRelay.FlushAsync(Token);

        var replyHeader = new byte[sizeof(uint)];
        await rig.FromRelay.ReadExactlyAsync(replyHeader, Token);
        var reply = new byte[MemoryMarshal.Read<uint>(replyHeader)];
        await rig.FromRelay.ReadExactlyAsync(reply, Token);

        Assert.True(BitConverter.IsLittleEndian, "every target keypaste builds is little-endian");
        Assert.Equal(new byte[] { (byte)reply.Length, (byte)(reply.Length >> 8), 0, 0 }, replyHeader);
        Assert.Equal("change-public-keys", (string?)JsonNode.Parse(reply)!["action"]);
    }

    [Fact]
    public async Task A_message_over_a_mebibyte_ends_the_relay_before_any_of_it_is_read()
    {
        await using var rig = new RelayRig();
        var header = new byte[sizeof(uint)];
        MemoryMarshal.Write(header, (uint)NativeMessages.MaximumBytes + 1);

        await rig.ToRelay.WriteAsync(header, Token);
        await rig.ToRelay.FlushAsync(Token);

        Assert.Equal(1, await rig.Running.WaitAsync(_wait, Token));
        Assert.Contains($"{NativeMessages.MaximumBytes + 1} bytes", rig.Error.ToString(), StringComparison.Ordinal);

        rig.CloseOutput();
        Assert.Null(await rig.Browser.ReceiveAsync(Token));
        Assert.Empty(rig.Owner.Heard);
    }

    [Fact]
    public async Task A_message_of_a_mebibyte_is_read_and_answered()
    {
        await using var rig = new RelayRig();
        var head = "{\"action\":\"get-totp\",\"padding\":\""u8;
        var tail = "\"}"u8;
        var message = new byte[NativeMessages.MaximumBytes];
        head.CopyTo(message);
        message.AsSpan(head.Length, message.Length - head.Length - tail.Length).Fill((byte)'a');
        tail.CopyTo(message.AsSpan(message.Length - tail.Length));

        var sending = NativeMessages.WriteAsync(rig.ToRelay, message, Token);
        var reply = await rig.Browser.ReceiveAsync(Token);
        await sending;

        Assert.Equal("6", (string?)reply!["errorCode"]);
    }

    [Fact]
    public async Task Each_key_exchange_reports_version_2_6_0_a_fresh_host_key_and_the_nonce_incremented()
    {
        await using var rig = new RelayRig();
        var nonce = ExtensionClient.NewNonce();

        var first = await rig.Browser.ChangePublicKeysAsync(nonce, Token);
        var firstKey = rig.Browser.HostKey;
        var second = await rig.Browser.ChangePublicKeysAsync(Token);

        Assert.Equal("2.6.0", (string?)first["version"]);
        Assert.Equal("true", (string?)first["success"]);
        Assert.Equal(ExtensionClient.Incremented(nonce), (string?)first["nonce"]);
        Assert.NotNull(firstKey);
        Assert.NotEqual(Convert.ToBase64String(firstKey), (string?)second["publicKey"]);
        Assert.Equal("2.6.0", (string?)second["version"]);
        Assert.Empty(rig.Owner.Heard);
    }

    [Theory]
    [InlineData("000000000000000000000000000000000000000000000000", "010000000000000000000000000000000000000000000000")]
    [InlineData("ff0000000000000000000000000000000000000000000000", "000100000000000000000000000000000000000000000000")]
    [InlineData("ffff00000000000000000000000000000000000000000000", "000001000000000000000000000000000000000000000000")]
    [InlineData("fe01000000000000000000000000000000000000000000ff", "ff01000000000000000000000000000000000000000000ff")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffff", "000000000000000000000000000000000000000000000000")]
    public void A_nonce_is_incremented_as_a_little_endian_number_that_wraps(string nonce, string incremented) =>
        Assert.Equal(Convert.FromHexString(incremented), NonceLedger.Incremented(Convert.FromHexString(nonce)));

    [Fact]
    public async Task An_answer_is_sealed_under_the_request_s_nonce_incremented()
    {
        await using var rig = await RelayRig.KeyedAsync();
        var nonce = ExtensionClient.NewNonce();

        var answer = await rig.Browser.RequestAsync("test-associate", Pairing(Key()), nonce, triggerUnlock: false, Token);

        Assert.Equal(ExtensionClient.Incremented(nonce), (string?)answer.Reply["nonce"]);
        Assert.NotNull(answer.Message);
        Assert.Equal(ExtensionClient.Incremented(nonce), (string?)answer.Message["nonce"]);
        Assert.Equal("2.6.0", (string?)answer.Message["version"]);
        Assert.Equal("true", (string?)answer.Message["success"]);
    }

    /// <summary>
    /// A request under a nonce the session used, its own or a reply's, or whose reply would come under one, is refused before it
    /// is opened, since a box key is the same both ways and its answer would reuse a nonce under it (D-0625).
    /// </summary>
    [Fact]
    public async Task A_nonce_the_session_used_is_refused_with_error_4_and_reaches_no_owner()
    {
        await using var rig = await RelayRig.KeyedAsync();
        var before = ExtensionClient.NewNonce();
        var nonce = ExtensionClient.Incremented(before);
        var key = Key();

        var first = await rig.Browser.RequestAsync("test-associate", Pairing(key), nonce, triggerUnlock: false, Token);
        var repeated = await rig.Browser.RequestAsync("test-associate", Pairing(key), nonce, triggerUnlock: false, Token);
        var underTheReply = await rig.Browser.RequestAsync("test-associate", Pairing(key), ExtensionClient.Incremented(nonce), triggerUnlock: false, Token);
        var replyingUnderIt = await rig.Browser.RequestAsync("test-associate", Pairing(key), before, triggerUnlock: false, Token);

        Assert.NotNull(first.Message);
        Assert.All(new[] { repeated, underTheReply, replyingUnderIt }, answer =>
        {
            Assert.Equal("4", answer.ErrorCode);
            Assert.Null(answer.Reply["nonce"]);
        });
        Assert.IsType<BrowserCheckRequest>(Assert.Single(rig.Owner.Asked));
    }

    [Fact]
    public async Task Undecryptable_input_is_answered_with_error_4_and_reaches_no_owner()
    {
        await using var rig = await RelayRig.KeyedAsync();
        using var stranger = NaclBox.NewKeyPair();
        var strangersNonce = ExtensionClient.NewNonce();
        var strangers = Convert.ToBase64String(NaclBox.Seal(
            Encoding.UTF8.GetBytes("{\"action\":\"get-databasehash\"}"), Convert.FromBase64String(strangersNonce), stranger.PublicKey, stranger.SecretKey));
        var notJsonNonce = ExtensionClient.NewNonce();
        var notJson = rig.Browser.Seal(Encoding.UTF8.GetBytes("get-databasehash"), notJsonNonce);
        var shortNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NaclBox.NonceLength - 1));

        JsonObject[] requests =
        [
            Request("get-databasehash", strangers, strangersNonce),
            Request("get-databasehash", "not base64 at all", ExtensionClient.NewNonce()),
            Request("get-databasehash", rig.Browser.Seal(new JsonObject { ["action"] = "get-databasehash" }, ExtensionClient.NewNonce()), shortNonce),
            Request("get-databasehash", notJson, notJsonNonce),
            new JsonObject { ["action"] = "get-databasehash", ["nonce"] = ExtensionClient.NewNonce() },
        ];

        foreach (var request in requests)
        {
            await rig.Browser.SendAsync(request, Token);
            var reply = await rig.Browser.ReceiveAsync(Token);

            Assert.Equal("4", (string?)reply!["errorCode"]);
            Assert.Equal("Cannot decrypt message", (string?)reply["error"]);
        }

        Assert.Empty(rig.Owner.Heard);
    }

    [Fact]
    public async Task Before_a_key_exchange_a_sealed_request_is_answered_with_error_3()
    {
        await using var rig = new RelayRig();

        await rig.Browser.SendAsync(Request("get-databasehash", "AAAA", ExtensionClient.NewNonce()), Token);
        var reply = await rig.Browser.ReceiveAsync(Token);

        Assert.Equal("3", (string?)reply!["errorCode"]);
        Assert.Empty(rig.Owner.Heard);
    }

    [Theory]
    [InlineData("get-totp", "6")]
    [InlineData("generate-password", "6")]
    [InlineData("set-login", "6")]
    [InlineData("delete-entry", "6")]
    [InlineData("request-autotype", "6")]
    [InlineData("create-new-group", "17")]
    [InlineData("get-database-groups", "16")]
    [InlineData("get-database-entries", "19")]
    [InlineData("passkeys-get", "22")]
    [InlineData("passkeys-register", "22")]
    public async Task Each_action_keypaste_refuses_is_answered_with_KeePassXC_s_code_and_reaches_no_owner(string action, string code)
    {
        await using var rig = await RelayRig.KeyedAsync();

        var answer = await rig.Browser.RequestAsync(action, null, Token);

        Assert.Equal(code, answer.ErrorCode);
        Assert.Equal(action, (string?)answer.Reply["action"]);
        Assert.Null(answer.Reply["nonce"]);
        Assert.Empty(rig.Owner.Heard);
    }

    [Fact]
    public async Task HTTP_authentication_is_refused_with_error_15_and_reaches_no_owner()
    {
        await using var rig = await RelayRig.KeyedAsync();
        await CheckAsync(rig, Key());

        var answer = await rig.Browser.RequestAsync("get-logins", new JsonObject { ["url"] = "https://github.com/", ["httpAuth"] = "true" }, Token);

        Assert.Equal("15", answer.ErrorCode);
        Assert.IsType<BrowserCheckRequest>(Assert.Single(rig.Owner.Asked));
    }

    [Fact]
    public async Task An_action_the_protocol_does_not_have_is_answered_with_error_12_and_a_message_that_is_not_JSON_with_13()
    {
        await using var rig = await RelayRig.KeyedAsync();

        var unknown = await rig.Browser.RequestAsync("get-logins-count", null, Token);
        await NativeMessages.WriteAsync(rig.ToRelay, "not json"u8.ToArray(), Token);
        var notJson = await rig.Browser.ReceiveAsync(Token);
        await NativeMessages.WriteAsync(rig.ToRelay, Array.Empty<byte>(), Token);
        var empty = await rig.Browser.ReceiveAsync(Token);

        Assert.Equal("12", unknown.ErrorCode);
        Assert.Equal("13", (string?)notJson!["errorCode"]);
        Assert.Equal("13", (string?)empty!["errorCode"]);
        Assert.Empty(rig.Owner.Heard);
    }

    [Fact]
    public async Task A_sealed_request_whose_message_names_another_action_is_answered_with_error_12()
    {
        await using var rig = await RelayRig.KeyedAsync();
        var nonce = ExtensionClient.NewNonce();

        await rig.Browser.SendAsync(Request("get-databasehash", rig.Browser.Seal(new JsonObject { ["action"] = "lock-database" }, nonce), nonce), Token);
        var reply = await rig.Browser.ReceiveAsync(Token);

        Assert.Equal("12", (string?)reply!["errorCode"]);
        Assert.Empty(rig.Owner.Heard);
    }

    [Fact]
    public async Task With_nothing_holding_the_vault_a_request_is_answered_with_error_1_and_knocks_only_when_the_extension_asks_to_unlock()
    {
        await using var rig = await RelayRig.KeyedAsync(owner: false);
        using var heard = new SemaphoreSlim(0);
        using var app = LockedAppEndpoint.Listen(rig.Options.LockedAppName!, () => heard.Release(), _ => { });

        var quiet = await rig.Browser.RequestAsync("get-databasehash", null, Token);
        Assert.False(await heard.WaitAsync(TimeSpan.FromMilliseconds(500), Token), "the relay knocked when the extension had not asked to unlock");

        var asking = await rig.Browser.RequestAsync("get-databasehash", null, ExtensionClient.NewNonce(), triggerUnlock: true, Token);

        Assert.Equal("1", quiet.ErrorCode);
        Assert.Equal("1", asking.ErrorCode);
        Assert.Equal("Database not opened", (string?)asking.Reply["error"]);
        Assert.True(await heard.WaitAsync(_wait, Token), "the locked app was not told");
        Assert.False(await heard.WaitAsync(TimeSpan.FromMilliseconds(500), Token), "the locked app was told twice");
    }

    [Fact]
    public async Task An_owner_that_says_it_is_locked_is_answered_as_not_open()
    {
        await using var rig = await RelayRig.KeyedAsync();
        rig.Owner.Session = null;

        var answer = await rig.Browser.RequestAsync("lock-database", null, Token);

        Assert.Equal("1", answer.ErrorCode);
        Assert.Empty(rig.Owner.Asked);
    }

    [Fact]
    public async Task The_relay_attaches_as_the_browser_it_was_started_for_and_nothing_else()
    {
        await using var rig = await RelayRig.KeyedAsync();

        var hash = await rig.Browser.RequestAsync("get-databasehash", null, Token);

        Assert.Equal("2", hash.ErrorCode);
        var attached = Assert.IsType<AttachRequest>(Assert.Single(rig.Owner.Heard));
        Assert.Equal(new AttachRequest(rig.Options.VaultPath) { Browser = BrowserIdentity.ChromeWebStore.Caller }, attached);
    }

    /// <summary>
    /// The hash the owner tells the attachment answers <c>get-databasehash</c> sealed, and comes with each pairing and check, as
    /// KeePassXC's answers carry it, so the extension keeps keypaste's pairing in a slot of its own (D-0636).
    /// </summary>
    [Fact]
    public async Task The_hash_the_owner_tells_answers_get_databasehash_and_comes_with_each_pairing_and_check()
    {
        await using var rig = await RelayRig.KeyedAsync();
        var hash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        rig.Owner.DatabaseHash = hash;

        var told = await rig.Browser.RequestAsync("get-databasehash", new JsonObject { ["connectedKeys"] = new JsonArray(Key()) }, Token);
        var paired = await rig.Browser.RequestAsync("associate", new JsonObject { ["key"] = rig.Browser.PublicKey, ["idKey"] = Key() }, Token);
        var check = await CheckAsync(rig, Key());

        Assert.Equal(hash, (string?)told.Message!["hash"]);
        Assert.False(told.Message.ContainsKey("oldHash"));
        Assert.Equal(hash, (string?)paired.Message!["hash"]);
        Assert.Equal(_pairing, (string?)paired.Message["id"]);
        Assert.Equal(hash, (string?)check.Message!["hash"]);
        Assert.Equal(_pairing, (string?)check.Message["id"]);
    }

    [Fact]
    public async Task An_associate_reaches_the_owner_only_under_the_exchanged_session_key_and_names_only_the_identification_key()
    {
        await using var rig = await RelayRig.KeyedAsync();
        var identificationKey = Key();

        var strangers = await rig.Browser.RequestAsync("associate", new JsonObject { ["key"] = Key(), ["idKey"] = identificationKey }, Token);
        var paired = await rig.Browser.RequestAsync("associate", new JsonObject { ["key"] = rig.Browser.PublicKey, ["idKey"] = identificationKey }, Token);

        Assert.Equal("8", strangers.ErrorCode);
        Assert.Equal(_pairing, (string?)paired.Message!["id"]);
        Assert.Equal(new BrowserPairRequest(identificationKey) { Vault = rig.Options.VaultPath, Session = "session-one" }, Assert.Single(rig.Owner.Asked));
    }

    [Fact]
    public async Task An_associate_the_owner_refuses_is_answered_with_error_6()
    {
        await using var rig = await RelayRig.KeyedAsync();
        rig.Owner.PairReply = new BrowserReply(BrowserOutcome.Refused, "this keypaste process does not pair browsers or fill logins in them");

        var answer = await rig.Browser.RequestAsync("associate", new JsonObject { ["key"] = rig.Browser.PublicKey, ["idKey"] = Key() }, Token);

        Assert.Equal("6", answer.ErrorCode);
    }

    /// <summary>
    /// The extension sends every pairing it holds with each request for logins, KeePassXC's among them; only the one the last
    /// check proved reaches the owner, whatever the request names (D-0625).
    /// </summary>
    [Fact]
    public async Task After_a_check_a_fill_sends_the_owner_that_pairing_alone_and_seals_its_logins()
    {
        await using var rig = await RelayRig.KeyedAsync();
        rig.Owner.FillReply = new BrowserFillReply(BrowserOutcome.Done, string.Empty)
        {
            Logins = [new BrowserLogin("ada", "correct horse battery staple", "GitHub", "0123456789abcdef0123456789abcdef")],
        };
        var key = Key();
        var check = await CheckAsync(rig, key);

        var fill = await rig.Browser.RequestAsync(
            "get-logins",
            new JsonObject
            {
                ["id"] = "another pairing",
                ["url"] = "https://github.com/login",
                ["submitUrl"] = "https://github.com/session",
                ["keys"] = new JsonArray(
                    new JsonObject { ["id"] = "KeePassXC's own pairing", ["key"] = Key() },
                    new JsonObject { ["id"] = _pairing, ["key"] = key }),
            },
            Token);

        Assert.Equal(_pairing, (string?)check.Message!["id"]);
        Assert.Equal(
            new BrowserFillRequest(_pairing, key, "https://github.com/login")
            {
                SubmitUrl = "https://github.com/session",
                Vault = rig.Options.VaultPath,
                Session = "session-one",
            },
            Assert.IsType<BrowserFillRequest>(rig.Owner.Asked[^1]));
        Assert.Equal(2, rig.Owner.Asked.Count);

        var message = fill.Message;
        Assert.NotNull(message);
        Assert.Equal(1, (int)message["count"]!);
        Assert.Equal(_pairing, (string?)message["id"]);
        var entry = Assert.Single(message["entries"]!.AsArray())!.AsObject();
        Assert.Equal(new[] { "login", "name", "password", "uuid" }, entry.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("ada", (string?)entry["login"]);
        Assert.Equal("correct horse battery staple", (string?)entry["password"]);
        Assert.DoesNotContain("correct horse", fill.Reply.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fill_before_a_check_is_answered_with_error_8_and_reaches_no_owner()
    {
        await using var rig = await RelayRig.KeyedAsync();

        var answer = await rig.Browser.RequestAsync("get-logins", new JsonObject { ["url"] = "https://github.com/login", ["id"] = _pairing }, Token);

        Assert.Equal("8", answer.ErrorCode);
        Assert.Empty(rig.Owner.Heard);
    }

    [Fact]
    public async Task A_check_the_owner_does_not_hold_leaves_no_pairing_proven()
    {
        await using var rig = await RelayRig.KeyedAsync();
        await CheckAsync(rig, Key());
        rig.Owner.CheckReply = new BrowserReply(BrowserOutcome.NotPaired, "no pairing of this browser has that id and key");

        var check = await rig.Browser.RequestAsync("test-associate", Pairing(Key()), Token);
        var fill = await rig.Browser.RequestAsync("get-logins", new JsonObject { ["url"] = "https://github.com/login" }, Token);

        Assert.Equal("8", check.ErrorCode);
        Assert.Equal("8", fill.ErrorCode);
        Assert.Equal(2, rig.Owner.Asked.Count);
    }

    [Fact]
    public async Task A_new_key_exchange_leaves_no_pairing_proven()
    {
        await using var rig = await RelayRig.KeyedAsync();
        await CheckAsync(rig, Key());
        await rig.Browser.ChangePublicKeysAsync(Token);

        var fill = await rig.Browser.RequestAsync("get-logins", new JsonObject { ["url"] = "https://github.com/login" }, Token);

        Assert.Equal("8", fill.ErrorCode);
        Assert.Single(rig.Owner.Asked);
    }

    [Fact]
    public async Task Lock_database_has_the_owner_lock_and_is_answered_sealed()
    {
        await using var rig = await RelayRig.KeyedAsync();

        var answer = await rig.Browser.RequestAsync("lock-database", null, Token);

        Assert.NotNull(answer.Message);
        Assert.Equal(new BrowserLockRequest { Vault = rig.Options.VaultPath, Session = "session-one" }, Assert.Single(rig.Owner.Asked));
    }

    [Fact]
    public async Task The_owner_locking_is_told_as_database_locked_and_the_first_answer_after_its_unlock_brings_database_unlocked()
    {
        await using var rig = await RelayRig.KeyedAsync();
        Assert.Equal("2", (await rig.Browser.RequestAsync("get-databasehash", null, Token)).ErrorCode);

        await rig.Owner.StopAsync();

        Assert.Equal("database-locked", await rig.Browser.ReceiveSignalAsync(Token).WaitAsync(_wait, Token));
        Assert.Equal("1", (await rig.Browser.RequestAsync("get-databasehash", null, Token)).ErrorCode);

        rig.Owner.Listen();

        Assert.Equal("2", (await rig.Browser.RequestAsync("get-databasehash", null, Token)).ErrorCode);
        Assert.Equal(new[] { "database-locked", "database-unlocked" }, rig.Browser.Signals);
        Assert.Equal("2", (await rig.Browser.RequestAsync("get-databasehash", null, Token)).ErrorCode);
        Assert.Equal(2, rig.Browser.Signals.Count);
    }

    [Fact]
    public async Task Closing_standard_input_ends_the_relay()
    {
        await using var rig = await RelayRig.KeyedAsync();

        rig.CloseInput();

        Assert.Equal(0, await rig.Running.WaitAsync(_wait, Token));
    }

    private static async Task<Answer> CheckAsync(RelayRig rig, string key)
    {
        var check = await rig.Browser.RequestAsync("test-associate", Pairing(key), Token);
        Assert.NotNull(check.Message);
        return check;
    }

    private static JsonObject Pairing(string key) => new() { ["id"] = _pairing, ["key"] = key };

    private static JsonObject Request(string action, string message, string nonce) =>
        new() { ["action"] = action, ["message"] = message, ["nonce"] = nonce, ["clientID"] = ExtensionClient.NewNonce() };

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(NaclBox.KeyLength));
}
