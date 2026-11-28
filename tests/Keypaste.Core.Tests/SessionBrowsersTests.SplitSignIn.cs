using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Core.Approval;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A sign-in split across pages and frames: a pick releases only the parts the page shows, the next click in the tab continues it
/// once per part within two minutes on the same site, a one-time code leaves without its seed, and a frame of another site is
/// offered only its own site's logins once it is a known sign-in frame or the person trusted it (THREATS.md T-59, T-62).
/// </summary>
public sealed partial class SessionBrowsersTests
{
    private const string _seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public async Task A_username_only_page_gets_the_username_and_no_password_leaves_keypaste()
    {
        var github = AddLogin("web", "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var reply = await link.ReleaseAsync(Release("https://github.com/login", github) with { Fill = BrowserFill.Username }, Token);

        var login = Assert.Single(reply!.Logins);
        Assert.Equal(("dana", string.Empty, string.Empty), (login.Login, login.Password, login.Totp));
        Assert.DoesNotContain("s3cret", Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
        Assert.Equal(("github.com", 120), (reply.ContinueSite, reply.ContinueSeconds));

        var line = _log.Lines()[^1];
        Assert.Equal(("fill", "granted", "picked"), (line.GetProperty("args").GetProperty("field").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        Assert.Equal("the person picked it in the page's list, its username, on https://github.com:443", line.GetProperty("reason").GetString());
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task A_password_only_page_gets_the_password_alone_and_an_item_with_no_username_still_fills_it()
    {
        var github = AddLogin("web", "GitHub", "dana", "s3cret", "https://github.com");
        var router = AddLogin("web", "Router", "{REF:U@I:00000000000000000000000000000000}", "r0uter", "https://router.example");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var password = await link.ReleaseAsync(Release("https://github.com/session", github) with { Fill = BrowserFill.Password }, Token);
        var noUsername = await link.ReleaseAsync(Release("https://router.example/", router) with { Fill = BrowserFill.Password }, Token);
        var both = await link.ReleaseAsync(Release("https://router.example/", router), Token);

        Assert.Equal((string.Empty, "s3cret"), (Assert.Single(password!.Logins).Login, password.Logins[0].Password));
        Assert.Equal("r0uter", Assert.Single(noUsername!.Logins).Password);
        Assert.Empty(both!.Logins);
        Assert.Equal("unfillable", _log.Lines()[^1].GetProperty("method").GetString());
    }

    [Fact]
    public async Task The_next_click_within_two_minutes_on_the_same_site_fills_the_password_once_with_no_pick()
    {
        var github = AddLogin("web", "GitHub", "dana", "s3cret", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        await link.ReleaseAsync(Release("https://github.com/login", github) with { Fill = BrowserFill.Username }, Token);
        _fixture.Clock.Advance(TimeSpan.FromSeconds(119));
        var continued = await link.ReleaseAsync(Release("https://gist.github.com/session", github) with { Fill = BrowserFill.Password, Continued = true }, Token);
        var again = await link.ReleaseAsync(Release("https://github.com/session", github) with { Fill = BrowserFill.Password, Continued = true }, Token);

        var login = Assert.Single(continued!.Logins);
        Assert.Equal((string.Empty, "s3cret"), (login.Login, login.Password));
        Assert.Equal((string.Empty, 0), (continued.ContinueSite, continued.ContinueSeconds));
        Assert.Empty(again!.Logins);
        Assert.Equal(0, _fixture.Channel.Asked);

        var lines = _log.Lines().TakeLast(2).ToList();
        Assert.Equal(("granted", "continued"), (lines[0].GetProperty("decision").GetString(), lines[0].GetProperty("method").GetString()));
        Assert.Equal(
            "it continued, within two minutes, the person's pick on https://github.com:443, its password, on https://gist.github.com:443",
            lines[0].GetProperty("reason").GetString());
        Assert.Equal(github, lines[0].GetProperty("args").GetProperty("item").GetString(), ignoreCase: true);
        Assert.Equal(("denied", "out-of-scope"), (lines[1].GetProperty("decision").GetString(), lines[1].GetProperty("method").GetString()));
        Assert.Contains("nothing filled in this browser", lines[1].GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_continuation_is_refused_with_no_pick_after_two_minutes_on_another_site_or_scheme_and_for_another_item()
    {
        var github = AddLogin("web", "GitHub", "dana", "s3cret", "https://github.com");
        var other = AddLogin("web", "GitHub work", "sam", "w0rk", "https://github.com");
        AddLogin("web", "GitLab", "dana", "l4b", "https://gitlab.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var password = (string url, string item) => Release(url, item) with { Fill = BrowserFill.Password, Continued = true };

        var unpicked = await link.ReleaseAsync(password("https://github.com/session", github), Token);
        await link.ReleaseAsync(Release("https://github.com/login", github) with { Fill = BrowserFill.Username }, Token);
        var otherItem = await link.ReleaseAsync(password("https://github.com/session", other), Token);
        var otherSite = await link.ReleaseAsync(password("https://gitlab.com/session", github), Token);
        var otherScheme = await link.ReleaseAsync(password("http://github.com/session", github), Token);
        var username = await link.ReleaseAsync(Release("https://github.com/session", github) with { Fill = BrowserFill.Username, Continued = true }, Token);
        _fixture.Clock.Advance(ContinuedFill.Window + TimeSpan.FromSeconds(1));
        var late = await link.ReleaseAsync(password("https://github.com/session", github), Token);

        Assert.All([unpicked, otherItem, otherSite, otherScheme, username, late], reply => Assert.Empty(reply!.Logins));
        Assert.Equal(
            [
                "nothing filled in this browser in the last two minutes is left to continue",
                "the fill it continues was of another login",
                "the fill it continues was on another site",
                "the fill it continues was on another site",
                "the fill it continues has no username left to fill",
                "the fill it continues was more than two minutes ago",
            ],
            _log.Lines().Where(line => line.GetProperty("decision").GetString() == "denied")
                .Select(line => line.GetProperty("reason").GetString()![..line.GetProperty("reason").GetString()!.LastIndexOf(", on ", StringComparison.Ordinal)]));
        Assert.DoesNotContain("s3cret", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lock_continues_no_fill()
    {
        var github = AddLogin("web", "GitHub", "dana", "s3cret", "https://github.com");
        using var other = new SecondVault(this, "acme");
        await using var owner = Owner.Start(this, () => [new PairingVault(_vault, edit => Write(_lifetime, edit)), other.Pairing]);
        other.Serve(owner);
        await using var link = await PairedLinkAsync(owner);

        await link.ReleaseAsync(Release("https://github.com/login", github) with { Fill = BrowserFill.Username }, Token);
        other.Lifetime.End();
        var continued = await link.ReleaseAsync(Release("https://github.com/session", github) with { Fill = BrowserFill.Password, Continued = true }, Token);

        Assert.Empty(continued!.Logins);
        Assert.Contains("nothing filled in this browser", _log.Lines()[^1].GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_one_time_code_continues_a_login_or_fills_from_a_pick_and_never_carries_its_seed()
    {
        var github = AddLogin("web", "GitHub", "dana", "s3cret", "https://github.com");
        _vault.SetFieldsUnchecked(new EntryName("web", "GitHub"), [new FieldWrite(TotpSpec.OtpField, $"otpauth://totp/GitHub:dana?secret={_seed}&issuer=GitHub")]);
        _vault.Save();
        var plain = AddLogin("web", "GitHub work", "sam", "w0rk", "https://github.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        string expected;

        using (var spec = _vault.ReadTotp(new EntryName("web", "GitHub"), out _))
        {
            expected = spec!.Code(_fixture.Clock).Value;
        }

        var picked = await link.ReleaseAsync(Release("https://github.com/login", github), Token);
        var code = await link.ReleaseAsync(Release("https://github.com/two-factor", github) with { Fill = BrowserFill.Totp, Continued = true }, Token);
        var pickedCode = await link.ReleaseAsync(Release("https://github.com/two-factor", github) with { Fill = BrowserFill.Totp }, Token);
        var none = await link.ReleaseAsync(Release("https://github.com/two-factor", plain) with { Fill = BrowserFill.Totp }, Token);
        var plainLogin = await link.ReleaseAsync(Release("https://github.com/login", plain), Token);

        Assert.Equal(("github.com", 120), (picked!.ContinueSite, picked.ContinueSeconds));
        var released = Assert.Single(code!.Logins);
        Assert.Equal((string.Empty, string.Empty, expected), (released.Login, released.Password, released.Totp));
        Assert.Matches("^[0-9]{6}$", released.Totp);
        Assert.Equal(expected, Assert.Single(pickedCode!.Logins).Totp);
        Assert.Empty(none!.Logins);
        Assert.Equal((string.Empty, 0), (plainLogin!.ContinueSite, plainLogin.ContinueSeconds));

        foreach (var reply in new[] { picked, code, pickedCode })
        {
            Assert.DoesNotContain(_seed, Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(_seed, _log.Text(), StringComparison.Ordinal);
        var methods = _log.Lines().TakeLast(4).Select(line => (line.GetProperty("method").GetString(), line.GetProperty("reason").GetString())).ToList();
        Assert.Equal("continued", methods[0].Item1);
        Assert.Contains("its one-time code", methods[0].Item2, StringComparison.Ordinal);
        Assert.Equal("picked", methods[1].Item1);
        Assert.Equal(("unfillable", "it has no one-time code keypaste can make, on https://github.com:443"), methods[2]);
    }

    [Fact]
    public async Task A_frame_of_another_site_is_asked_about_once_and_then_offered_only_its_own_site_s_logins()
    {
        var shop = AddLogin("web", "Shop", "dana", "sh0p", "https://www.shop.example");
        var sso = AddLogin("web", "Shop sign-in", "dana@sso", "ss0", "https://login.sso.example");
        _fixture.Channel.AnswerFrame = _ => ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var framed = (string url) => List(url) with { TopUrl = "https://www.shop.example/checkout" };

        var first = await link.ListAsync(framed("https://login.sso.example/frame"), Token);
        var again = await link.ListAsync(framed("https://login.sso.example/frame"), Token);
        var fill = await link.ReleaseAsync(Release("https://login.sso.example/frame", sso) with { TopUrl = "https://www.shop.example/checkout" }, Token);
        var tabLogin = await link.ReleaseAsync(Release("https://login.sso.example/frame", shop) with { TopUrl = "https://www.shop.example/checkout" }, Token);

        var prompt = Assert.Single(_fixture.Channel.FramePrompts);
        Assert.Equal(("laptop", "www.shop.example", "login.sso.example"), (prompt.Pairing, prompt.Top, prompt.Frame));
        Assert.Equal("Trust sign-in from login.sso.example on www.shop.example?", prompt.Question);
        Assert.Equal("login.sso.example", first!.Site);
        Assert.Equal(["Shop sign-in"], first.Names.Select(name => name.Title));
        Assert.Equal(first.Names, again!.Names);
        Assert.Equal("ss0", Assert.Single(fill!.Logins).Password);
        Assert.Empty(tabLogin!.Logins);
        var trust = Assert.Single(owner.Browsers.FrameTrusts());
        Assert.Equal(("laptop", "www.shop.example", "login.sso.example"), (trust.Pairing, trust.Top, trust.Frame));

        var trusted = _log.Lines().Single(line => line.GetProperty("args").GetProperty("field").GetString() == "trust");
        Assert.Equal(("granted", "prompt", "a person trusted sign-in from login.sso.example on www.shop.example"), (trusted.GetProperty("decision").GetString(), trusted.GetProperty("method").GetString(), trusted.GetProperty("reason").GetString()));
        var filled = _log.Lines().Last(line => line.GetProperty("decision").GetString() == "granted" && line.GetProperty("args").GetProperty("field").GetString() == "fill");
        Assert.Equal("the person picked it in the page's list in a sign-in frame on https://www.shop.example:443, on https://login.sso.example:443", filled.GetProperty("reason").GetString());
        Assert.DoesNotContain("sh0p", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_frame_the_person_does_not_trust_lists_nothing_is_not_asked_again_at_once_and_a_fill_there_is_refused_unasked()
    {
        var sso = AddLogin("web", "Shop sign-in", "dana@sso", "ss0", "https://login.sso.example");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var framed = List("https://login.sso.example/frame") with { TopUrl = "https://www.shop.example/" };

        var denied = await link.ListAsync(framed, Token);
        var heldBack = await link.ListAsync(framed, Token);
        var fill = await link.ReleaseAsync(Release("https://login.sso.example/frame", sso) with { TopUrl = "https://www.shop.example/" }, Token);

        Assert.Equal((BrowserOutcome.Refused, "a person did not trust sign-in from login.sso.example on www.shop.example"), (denied!.Outcome, denied.Reason));
        Assert.Empty(denied.Names);
        Assert.Equal(BrowserOutcome.Refused, heldBack!.Outcome);
        Assert.Contains("a moment ago", heldBack.Reason, StringComparison.Ordinal);
        Assert.Single(_fixture.Channel.FramePrompts);
        Assert.Empty(fill!.Logins);
        Assert.Contains("is not trusted", _log.Lines()[^1].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Empty(owner.Browsers.FrameTrusts());
    }

    /// <summary>The fill shortcut reaching into a frame asks nobody: a frame keypaste knows, trusts or takes for the page lists, and any other is refused.</summary>
    [Fact]
    public async Task A_list_the_fill_shortcut_reached_asks_no_trust_and_lists_only_a_frame_keypaste_already_fills()
    {
        AddLogin("web", "Shop sign-in", "dana@sso", "ss0", "https://login.sso.example");
        AddLogin("web", "Apple Account", "dana@icloud.com", "4pple", "https://idmsa.apple.com");
        AddLogin("web", "Shop login", "dana@login", "l0gin", "https://login.shop.example");
        _fixture.Channel.AnswerFrame = _ => ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var reached = (string url, string top) => List(url) with { TopUrl = top, AsksTrust = false };

        var unknown = await link.ListAsync(reached("https://login.sso.example/frame", "https://www.shop.example/"), Token);
        var apple = await link.ListAsync(reached("https://idmsa.apple.com/appleauth/auth/authorize/signin", "https://www.icloud.com/"), Token);
        var ownSite = await link.ListAsync(reached("https://login.shop.example/frame", "https://www.shop.example/"), Token);

        Assert.Equal(BrowserOutcome.Refused, unknown!.Outcome);
        Assert.Empty(unknown.Names);
        Assert.Empty(_fixture.Channel.FramePrompts);
        Assert.Equal(["Apple Account"], apple!.Names.Select(name => name.Title));
        Assert.Equal(["Shop login"], ownSite!.Names.Select(name => name.Title));
        var refused = _log.Lines().Single(line => line.GetProperty("decision").GetString() == "denied");
        Assert.Equal(("list", "out-of-scope"), (refused.GetProperty("args").GetProperty("field").GetString(), refused.GetProperty("method").GetString()));
        Assert.Equal("sign-in from login.sso.example on www.shop.example is not trusted, and the fill shortcut asks nobody, on https://login.sso.example:443", refused.GetProperty("reason").GetString());

        // Once the person trusts it at a click, the shortcut reaches it too.
        await link.ListAsync(List("https://login.sso.example/frame") with { TopUrl = "https://www.shop.example/" }, Token);
        var trusted = await link.ListAsync(reached("https://login.sso.example/frame", "https://www.shop.example/"), Token);

        Assert.Single(_fixture.Channel.FramePrompts);
        Assert.Equal(["Shop sign-in"], trusted!.Names.Select(name => name.Title));
    }

    [Theory]
    [InlineData("https://www.icloud.com/")]
    [InlineData("https://icloud.com/")]
    // Apple's own pages are on the list's frame's own site, so they are filled as their pages, with no entry of their own.
    [InlineData("https://appleid.apple.com/sign-in")]
    [InlineData("https://account.apple.com/")]
    public async Task Apple_s_sign_in_frame_is_offered_apple_s_logins_on_icloud_and_apple_account_unasked(string page)
    {
        AddLogin("web", "Apple Account", "dana@icloud.com", "4pple", "https://idmsa.apple.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var listed = await link.ListAsync(List("https://idmsa.apple.com/appleauth/auth/authorize/signin") with { TopUrl = page }, Token);

        Assert.Equal(["Apple Account"], listed!.Names.Select(name => name.Title));
        Assert.Empty(_fixture.Channel.FramePrompts);
    }

    [Fact]
    public async Task Apple_s_sign_in_frame_on_another_site_is_asked_about_as_any_other()
    {
        AddLogin("web", "Apple Account", "dana@icloud.com", "4pple", "https://idmsa.apple.com");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var listed = await link.ListAsync(List("https://idmsa.apple.com/appleauth/auth/authorize/signin") with { TopUrl = "https://icloud.example/" }, Token);

        Assert.Empty(listed!.Names);
        Assert.Equal("icloud.example", Assert.Single(_fixture.Channel.FramePrompts).Top);
    }

    [Fact]
    public async Task A_frame_of_another_origin_over_http_is_refused_unasked_and_one_of_the_tab_s_own_origin_is_its_page()
    {
        AddLogin("web", "Shop", "dana", "sh0p", "https://www.shop.example");
        AddLogin("web", "Shop sign-in", "dana@sso", "ss0", "http://login.sso.example");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);

        var plain = await link.ListAsync(List("http://login.sso.example/frame") with { TopUrl = "https://www.shop.example/" }, Token);
        var own = await link.ListAsync(List("https://www.shop.example/frame") with { TopUrl = "https://www.shop.example/checkout" }, Token);

        Assert.Equal((BrowserOutcome.Refused, "keypaste fills a frame of another origin only over https"), (plain!.Outcome, plain.Reason));
        Assert.Equal(["Shop"], own!.Names.Select(name => name.Title));
        Assert.Empty(_fixture.Channel.FramePrompts);
    }

    [Fact]
    public async Task An_https_frame_of_the_page_s_own_site_is_filled_as_the_page_with_its_own_logins_and_no_question()
    {
        AddLogin("web", "Shop checkout", "dana", "sh0p", "https://www.shop.example");
        var login = AddLogin("web", "Shop sign-in", "dana@login", "l0gin", "https://login.shop.example");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        const string tab = "https://www.shop.example/checkout";

        var listed = await link.ListAsync(List("https://login.shop.example/frame") with { TopUrl = tab }, Token);
        var filled = await link.ReleaseAsync(Release("https://login.shop.example/frame", login) with { TopUrl = tab }, Token);
        var otherScheme = await link.ListAsync(List("https://login.shop.example/frame") with { TopUrl = "http://www.shop.example/" }, Token);
        var plain = await link.ListAsync(List("http://login.shop.example/frame") with { TopUrl = "http://www.shop.example/" }, Token);

        Assert.Equal(["Shop sign-in"], listed!.Names.Select(name => name.Title));
        Assert.Equal("login.shop.example", listed.Site);
        Assert.Equal("l0gin", Assert.Single(filled!.Logins).Password);
        Assert.Equal("the person picked it in the page's list, on https://login.shop.example:443", _log.Lines().Last(line => line.GetProperty("decision").GetString() == "granted" && line.GetProperty("args").GetProperty("field").GetString() == "fill").GetProperty("reason").GetString());
        Assert.Empty(otherScheme!.Names);
        Assert.Equal((BrowserOutcome.Refused, "keypaste fills a frame of another origin only over https"), (plain!.Outcome, plain.Reason));
        Assert.Equal("www.shop.example", Assert.Single(_fixture.Channel.FramePrompts).Top);
        Assert.Empty(owner.Browsers.FrameTrusts());
    }

    [Fact]
    public async Task Removing_a_trust_asks_again_and_removing_the_pairing_removes_its_trusts()
    {
        AddLogin("web", "Shop sign-in", "dana@sso", "ss0", "https://login.sso.example");
        _fixture.Channel.AnswerFrame = _ => ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var framed = List("https://login.sso.example/frame") with { TopUrl = "https://www.shop.example/" };

        await link.ListAsync(framed, Token);
        var removed = owner.Browsers.RemoveFrameTrust(Assert.Single(owner.Browsers.FrameTrusts()));
        var asked = await link.ListAsync(framed, Token);

        Assert.True(removed.Removed, removed.Reason);
        Assert.Equal(2, _fixture.Channel.FramePrompts.Count);
        Assert.Single(asked!.Names);
        var line = _log.Lines().Last(line => line.GetProperty("method").GetString() == "removed");
        Assert.Equal("trust", line.GetProperty("args").GetProperty("field").GetString());

        Assert.True(owner.Browsers.Remove("laptop").Removed);
        Assert.Empty(owner.Browsers.FrameTrusts());
        Assert.DoesNotContain("login.sso.example", File.ReadAllText(PairingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trust_another_program_writes_into_the_file_proves_nothing()
    {
        AddLogin("web", "Shop sign-in", "dana@sso", "ss0", "https://login.sso.example");
        await using var owner = Owner.Start(this);
        await using var link = await PairedLinkAsync(owner);
        var file = JsonNode.Parse(File.ReadAllText(PairingsPath))!;
        var pairing = file["pairings"]![0]!;
        file["frames"] = new JsonArray(new JsonObject
        {
            ["pairing"] = "laptop",
            ["top"] = "www.shop.example",
            ["frame"] = "login.sso.example",
            ["created"] = "2026-11-25T00:00:00Z",
            ["key"] = pairing["key"]!.GetValue<string>(),
            ["seals"] = pairing["seals"]!.DeepClone(),
        });
        File.WriteAllText(PairingsPath, file.ToJsonString());

        var listed = await link.ListAsync(List("https://login.sso.example/frame") with { TopUrl = "https://www.shop.example/" }, Token);

        Assert.Empty(listed!.Names);
        Assert.Single(_fixture.Channel.FramePrompts);
        Assert.Empty(owner.Browsers.FrameTrusts());
    }

    [Fact]
    public async Task A_browser_s_attachment_is_told_every_feature_this_owner_decides()
    {
        await using var owner = Owner.Start(this);
        await using var link = await owner.ConnectAsync();

        var attached = await link.AttachAsync(new AttachRequest(VaultPath) { Browser = BrowserIdentity.ChromeWebStore.Caller }, Token);

        Assert.Equal([BrowserFeatures.Fill, BrowserFeatures.Continued, BrowserFeatures.Frames, BrowserFeatures.Totp], attached!.BrowserFeatures);
    }
}
