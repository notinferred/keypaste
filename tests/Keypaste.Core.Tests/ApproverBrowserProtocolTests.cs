using System.Text;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The browser's attachment and its four requests on the owner's pipe: each round-trips, and a malformed one is refused (D-0627).</summary>
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

    [Theory]
    [InlineData("""{"v":2,"kind":"attach","vault":"/v.kdbx","browser":"keepassxc-browser@keepassxc.org","client_name":"claude-code"}""")]
    [InlineData("""{"v":2,"kind":"attach","vault":"/v.kdbx","browser":"keepassxc-browser@keepassxc.org","client_label":"cursor"}""")]
    [InlineData("""{"v":2,"kind":"attach","vault":"/v.kdbx","browser":"keepassxc-browser@keepassxc.org","exposure":["**"]}""")]
    [InlineData("""{"v":2,"kind":"attach","vault":"/v.kdbx","browser":""}""")]
    [InlineData("""{"v":2,"kind":"attach","vault":"/v.kdbx","browser":"chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/"}""")]
    public void An_attachment_that_names_a_browser_and_an_agent_or_a_browser_out_of_bounds_is_refused(string frame) =>
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out AttachRequest? _));

    [Fact]
    public void The_four_requests_round_trip_as_their_own_kinds()
    {
        var pair = new BrowserPairRequest(_key) { Vault = "/v.kdbx", Session = "s" };
        var check = new BrowserCheckRequest("keypaste on this computer", _key) { Vault = "/v.kdbx", Session = "s" };
        var fill = new BrowserFillRequest("keypaste on this computer", _key, "https://github.com/login") { SubmitUrl = "https://github.com/session", Vault = "/v.kdbx", Session = "s" };
        var withoutForm = fill with { SubmitUrl = null };
        var browserLock = new BrowserLockRequest { Vault = "/v.kdbx", Session = "s" };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(pair), out BrowserPairRequest? decodedPair));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(check), out BrowserCheckRequest? decodedCheck));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(fill), out BrowserFillRequest? decodedFill));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(withoutForm), out BrowserFillRequest? decodedWithoutForm));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(browserLock), out BrowserLockRequest? decodedLock));

        Assert.Equal(pair, decodedPair);
        Assert.Equal(check, decodedCheck);
        Assert.Equal(fill, decodedFill);
        Assert.Equal(withoutForm, decodedWithoutForm);
        Assert.Equal(browserLock, decodedLock);

        Assert.Equal(ApproverMessageKind.BrowserPair, ApproverProtocol.KindOf(ApproverProtocol.Encode(pair)));
        Assert.Equal(ApproverMessageKind.BrowserCheck, ApproverProtocol.KindOf(ApproverProtocol.Encode(check)));
        Assert.Equal(ApproverMessageKind.BrowserFill, ApproverProtocol.KindOf(ApproverProtocol.Encode(fill)));
        Assert.Equal(ApproverMessageKind.BrowserLock, ApproverProtocol.KindOf(ApproverProtocol.Encode(browserLock)));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(browserLock), out LockRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new LockRequest { Vault = "/v.kdbx", Session = "s" }), out BrowserLockRequest? _));
    }

    [Fact]
    public void A_pairing_or_key_out_of_bounds_is_refused()
    {
        var tooLong = new string('k', AttachClient.MaximumLength + 1);

        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserPairRequest(tooLong) { Vault = "/v", Session = "s" }), out BrowserPairRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserCheckRequest(tooLong, _key) { Vault = "/v", Session = "s" }), out BrowserCheckRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new BrowserCheckRequest(string.Empty, _key) { Vault = "/v", Session = "s" }), out BrowserCheckRequest? _));
    }

    [Fact]
    public void The_replies_round_trip_and_a_fill_carries_logins_only_when_done()
    {
        var reply = new BrowserReply(BrowserOutcome.NotPaired, "no pairing of this browser has that id") { Pairing = "keypaste on this computer" };
        var fill = new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = [new BrowserLogin("ada", "hunter2", "GitHub", "0123456789abcdef0123456789abcdef")] };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out BrowserReply? decodedReply));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(fill), out BrowserFillReply? decodedFill));

        Assert.Equal(reply, decodedReply);
        Assert.Equal(fill.Logins, decodedFill.Logins);
        Assert.Equal(BrowserOutcome.Done, decodedFill.Outcome);

        var refusedWithLogins = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ApproverProtocol.Encode(fill)).Replace("\"outcome\":0", "\"outcome\":2", StringComparison.Ordinal));
        var unknownOutcome = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)).Replace("\"outcome\":3", "\"outcome\":9", StringComparison.Ordinal));

        Assert.False(ApproverProtocol.TryDecode(refusedWithLogins, out BrowserFillReply? _));
        Assert.False(ApproverProtocol.TryDecode(unknownOutcome, out BrowserReply? _));
    }

    [Fact]
    public void A_description_leaves_keys_usernames_and_passwords_out()
    {
        var described = string.Join(
            "\n",
            new BrowserPairRequest(_key).ToString(),
            new BrowserCheckRequest("pairing", _key).ToString(),
            new BrowserFillRequest("pairing", _key, "https://github.com/").ToString(),
            new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = [new BrowserLogin("ada", "hunter2", "GitHub", "uuid")] }.ToString(),
            new BrowserLogin("ada", "hunter2", "GitHub", "uuid").ToString());

        Assert.DoesNotContain(_key, described, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", described, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", described, StringComparison.Ordinal);
    }
}
