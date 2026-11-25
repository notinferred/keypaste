using System.Text;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The browser's attachment and its requests on the owner's pipe: each round-trips, and a malformed one is refused.</summary>
public sealed class ApproverBrowserProtocolTests
{
    private const string _key = "MQu2Pw2EUqX0XIK3ujeb1Gf9xf0LHcNSVW+uaR7kk1c=";

    [Fact]
    public void A_browser_s_attachment_round_trips_and_names_no_agent()
    {
        var request = new AttachRequest("/vaults/personal.kdbx") { Browser = BrowserIdentity.FirefoxAddOns.Caller };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(request), out AttachRequest? decoded));
        Assert.Equal(request, decoded);
        Assert.Null(decoded.Client);
        Assert.Null(decoded.Exposure);
    }

    /// <summary>Each row is a browser's attachment, which decodes, with one defect.</summary>
    [Theory]
    [InlineData("""{"kind":"attach","vault":"/v.kdbx","browser":"browser@keypaste.com","client_name":"claude-code"}""")]
    [InlineData("""{"kind":"attach","vault":"/v.kdbx","browser":"browser@keypaste.com","client_label":"cursor"}""")]
    [InlineData("""{"kind":"attach","vault":"/v.kdbx","browser":"browser@keypaste.com","exposure":["**"]}""")]
    [InlineData("""{"kind":"attach","vault":"/v.kdbx","browser":""}""")]
    [InlineData("""{"kind":"attach","vault":"/v.kdbx","browser":"chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/"}""")]
    public void An_attachment_that_names_a_browser_and_an_agent_or_a_browser_out_of_bounds_is_refused(string frame)
    {
        Assert.True(ApproverProtocol.TryDecode(ApproverFrame.Versioned("""{"kind":"attach","vault":"/v.kdbx","browser":"browser@keypaste.com"}"""), out AttachRequest? _));

        Assert.False(ApproverProtocol.TryDecode(ApproverFrame.Versioned(frame), out AttachRequest? _));
    }

    [Fact]
    public void An_attach_reply_carries_the_database_hash_it_was_given()
    {
        var hash = new string('0', 63) + "f";
        var told = AttachReply.To("s") with { DatabaseHash = hash };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(told), out AttachReply? decoded));
        Assert.Equal(hash, decoded.DatabaseHash);
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(AttachReply.To("s")), out AttachReply? untold));
        Assert.Null(untold.DatabaseHash);
    }

    /// <summary>Each row is an attachment carrying a hash, or a refusal carrying none, which decode, with one defect.</summary>
    [Theory]
    [InlineData("""{"kind":"attach","session":"s","reason":"","database_hash":"ABCDEF0000000000000000000000000000000000000000000000000000000000"}""")]
    [InlineData("""{"kind":"attach","session":"s","reason":"","database_hash":"abc"}""")]
    [InlineData("""{"kind":"attach","method":0,"reason":"locked","database_hash":"abcdef0000000000000000000000000000000000000000000000000000000000"}""")]
    public void A_database_hash_that_is_not_a_lowercase_sha_256_or_beside_a_refusal_is_refused(string frame)
    {
        Assert.True(ApproverProtocol.TryDecode(
            ApproverFrame.Versioned("""{"kind":"attach","session":"s","reason":"","database_hash":"abcdef0000000000000000000000000000000000000000000000000000000000"}"""),
            out AttachReply? _));
        Assert.True(ApproverProtocol.TryDecode(ApproverFrame.Versioned("""{"kind":"attach","method":0,"reason":"locked"}"""), out AttachReply? _));

        Assert.False(ApproverProtocol.TryDecode(ApproverFrame.Versioned(frame), out AttachReply? _));
    }

    [Fact]
    public void The_requests_round_trip_as_their_own_kinds()
    {
        var pair = new BrowserPairRequest(_key) { Vault = "/v.kdbx", Session = "s" };
        var check = new BrowserCheckRequest("keypaste on this computer", _key) { Vault = "/v.kdbx", Session = "s" };
        var list = new BrowserListRequest("keypaste on this computer", _key, "https://github.com/login") { SubmitUrl = "https://github.com/session", Vault = "/v.kdbx", Session = "s" };
        var withoutForm = list with { SubmitUrl = null };
        var release = new BrowserReleaseRequest("keypaste on this computer", _key, "https://github.com/login", "0123456789abcdef0123456789abcdef") { SubmitUrl = "https://github.com/session", Vault = "/v.kdbx", Session = "s" };
        var show = new BrowserShowRequest(BrowserShow.NewLogin) { VaultName = "Acme", Url = "https://github.com/login", Vault = "/v.kdbx", Session = "s" };
        var browserLock = new BrowserLockRequest { Vault = "/v.kdbx", Session = "s" };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(pair), out BrowserPairRequest? decodedPair));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(check), out BrowserCheckRequest? decodedCheck));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(list), out BrowserListRequest? decodedList));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(withoutForm), out BrowserListRequest? decodedWithoutForm));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(release), out BrowserReleaseRequest? decodedRelease));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(show), out BrowserShowRequest? decodedShow));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(browserLock), out BrowserLockRequest? decodedLock));

        Assert.Equal(pair, decodedPair);
        Assert.Equal(check, decodedCheck);
        Assert.Equal(list, decodedList);
        Assert.Equal(withoutForm, decodedWithoutForm);
        Assert.Equal(release, decodedRelease);
        Assert.Equal(show, decodedShow);
        Assert.Equal(browserLock, decodedLock);

        Assert.Equal(ApproverMessageKind.BrowserPair, ApproverProtocol.KindOf(ApproverProtocol.Encode(pair)));
        Assert.Equal(ApproverMessageKind.BrowserCheck, ApproverProtocol.KindOf(ApproverProtocol.Encode(check)));
        Assert.Equal(ApproverMessageKind.BrowserList, ApproverProtocol.KindOf(ApproverProtocol.Encode(list)));
        Assert.Equal(ApproverMessageKind.BrowserRelease, ApproverProtocol.KindOf(ApproverProtocol.Encode(release)));
        Assert.Equal(ApproverMessageKind.BrowserShow, ApproverProtocol.KindOf(ApproverProtocol.Encode(show)));
        Assert.Equal(ApproverMessageKind.BrowserLock, ApproverProtocol.KindOf(ApproverProtocol.Encode(browserLock)));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(list), out BrowserReleaseRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(browserLock), out LockRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new LockRequest { Vault = "/v.kdbx", Session = "s" }), out BrowserLockRequest? _));
    }

    [Fact]
    public void A_pairing_key_item_or_vault_name_out_of_bounds_is_refused()
    {
        var tooLong = new string('k', AttachClient.MaximumLength + 1);

        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserPairRequest(tooLong) { Vault = "/v", Session = "s" }), out BrowserPairRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserCheckRequest(tooLong, _key) { Vault = "/v", Session = "s" }), out BrowserCheckRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserCheckRequest(string.Empty, _key) { Vault = "/v", Session = "s" }), out BrowserCheckRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserReleaseRequest("laptop", _key, "https://github.com/", string.Empty) { Vault = "/v", Session = "s" }), out BrowserReleaseRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserShowRequest(BrowserShow.Unlock) { VaultName = tooLong, Vault = "/v", Session = "s" }), out BrowserShowRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserShowRequest((BrowserShow)9) { Vault = "/v", Session = "s" }), out BrowserShowRequest? _));
    }

    [Fact]
    public void A_list_round_trips_names_with_no_password_and_carries_them_only_when_done()
    {
        var list = new BrowserListReply(BrowserOutcome.Done, string.Empty)
        {
            Site = "github.com",
            Names = [new BrowserLoginName("0123456789abcdef0123456789abcdef", "GitHub", "ada") { Vault = "Personal" }],
            LockedVaults = ["Acme"],
            Warning = "This page's connection is not secure.",
        };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(list), out BrowserListReply? decoded));
        Assert.Equal((list.Outcome, list.Site, list.Warning), (decoded.Outcome, decoded.Site, decoded.Warning));
        Assert.Equal(list.Names, decoded.Names);
        Assert.Equal(list.LockedVaults, decoded.LockedVaults);
        Assert.DoesNotContain("password", Encoding.UTF8.GetString(ApproverProtocol.Encode(list)), StringComparison.Ordinal);

        var refusedWithNames = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ApproverProtocol.Encode(list)).Replace("\"outcome\":0", "\"outcome\":2", StringComparison.Ordinal));
        Assert.False(ApproverProtocol.TryDecode(refusedWithNames, out BrowserListReply? _));
    }

    [Fact]
    public void The_replies_round_trip_and_a_release_carries_one_login_only_when_done()
    {
        var reply = new BrowserReply(BrowserOutcome.NotPaired, "no pairing of this browser has that id") { Pairing = "keypaste on this computer" };
        var login = new BrowserLogin("ada", "hunter2", "GitHub", "0123456789abcdef0123456789abcdef");
        var fill = new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = [login] };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out BrowserReply? decodedReply));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(fill), out BrowserFillReply? decodedFill));

        Assert.Equal(reply, decodedReply);
        Assert.Equal(fill.Logins, decodedFill.Logins);
        Assert.Equal(BrowserOutcome.Done, decodedFill.Outcome);

        var refusedWithLogins = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ApproverProtocol.Encode(fill)).Replace("\"outcome\":0", "\"outcome\":2", StringComparison.Ordinal));
        var unknownOutcome = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)).Replace("\"outcome\":3", "\"outcome\":9", StringComparison.Ordinal));
        var two = ApproverProtocol.Encode(fill with { Logins = [login, login] });

        Assert.False(ApproverProtocol.TryDecode(refusedWithLogins, out BrowserFillReply? _));
        Assert.False(ApproverProtocol.TryDecode(unknownOutcome, out BrowserReply? _));
        Assert.False(ApproverProtocol.TryDecode(two, out BrowserFillReply? _));
    }

    [Fact]
    public void A_description_leaves_keys_usernames_and_passwords_out()
    {
        var described = string.Join(
            "\n",
            new BrowserPairRequest(_key).ToString(),
            new BrowserCheckRequest("pairing", _key).ToString(),
            new BrowserListRequest("pairing", _key, "https://github.com/").ToString(),
            new BrowserReleaseRequest("pairing", _key, "https://github.com/", "uuid").ToString(),
            new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = [new BrowserLogin("ada", "hunter2", "GitHub", "uuid")] }.ToString(),
            new BrowserLogin("ada", "hunter2", "GitHub", "uuid").ToString());

        Assert.DoesNotContain(_key, described, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", described, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", described, StringComparison.Ordinal);
    }
}
