using Keypaste.Core.Browser;
using Xunit;

namespace Keypaste.Core.Tests.Browser;

/// <summary>Which item a page's request for logins matches, by registrable domain, scheme and port, and which requests get none (8.3a1, D-0617, D-0618).</summary>
public sealed class LoginLookupTests
{
    [Theory]
    [InlineData("https://github.com/login", "github.com")]
    [InlineData("https://github.com/login", "https://github.com")]
    [InlineData("https://github.com/login", "  HTTPS://GitHub.com/  ")]
    [InlineData("https://gist.github.com/", "https://github.com")]
    [InlineData("https://a.b.example.co.uk/", "https://b.example.co.uk/login")]
    [InlineData("https://gist.alice.github.io/", "https://alice.github.io")]
    [InlineData("https://xn--bcher-kva.de/", "xn--bcher-kva.de")]
    public void An_item_matches_its_own_host_and_its_subdomains_within_one_registrable_domain(string page, string item) =>
        Assert.True(Lookup(page).Matches(item));

    [Theory]
    [InlineData("https://ab.example.com/", "https://b.example.com")]
    [InlineData("https://example.com/", "https://login.example.com")]
    [InlineData("https://github.com/", "https://gist.github.com")]
    [InlineData("https://github.com.evil.io/", "https://github.com")]
    [InlineData("https://evilgithub.com/", "https://github.com")]
    [InlineData("https://xn--gthub-n2e.com/", "https://github.com")]
    [InlineData("https://bob.github.io/", "https://alice.github.io")]
    [InlineData("https://bucket.s3.amazonaws.com/", "https://amazonaws.com")]
    [InlineData("https://github.com/", "https://github.io")]
    public void An_item_does_not_match_another_site_a_sibling_or_its_parent(string page, string item) =>
        Assert.False(Lookup(page).Matches(item));

    [Theory]
    [InlineData("https://github.io/")]
    [InlineData("https://co.uk/")]
    [InlineData("http://router/")]
    public void A_public_suffix_has_no_registrable_domain_and_gets_no_logins(string page) =>
        Refused(page, LookupRefusal.PublicSuffix);

    [Theory]
    [InlineData("https://192.168.1.10:8443/login", "https://192.168.1.10:8443")]
    [InlineData("https://192.168.1.10:8443/login", "192.168.1.10:8443")]
    [InlineData("http://localhost:3000/", "http://localhost:3000")]
    [InlineData("http://[::1]:8080/", "http://[::1]:8080")]
    [InlineData("http://[::1]:8080/", "http://[0:0:0:0:0:0:0:1]:8080/")]
    public void An_ip_address_or_localhost_matches_itself(string page, string item) =>
        Assert.True(Lookup(page).Matches(item));

    [Theory]
    [InlineData("https://192.168.1.11:8443/", "https://192.168.1.10:8443")]
    [InlineData("https://192.168.1.10/", "https://192.168.1.10:8443")]
    [InlineData("https://192.168.1.10/", "https://192.168.001.010")]
    [InlineData("http://app.localhost:3000/", "http://localhost:3000")]
    [InlineData("http://localhost:3000/", "http://app.localhost:3000")]
    [InlineData("http://127.0.0.1:3000/", "http://localhost:3000")]
    [InlineData("http://[::1]:8080/", "http://[::2]:8080")]
    public void An_ip_address_or_localhost_matches_nothing_else(string page, string item) =>
        Assert.False(Lookup(page).Matches(item));

    [Theory]
    [InlineData("http://github.com/login", "https://github.com")]
    [InlineData("http://github.com/login", "github.com")]
    public void An_https_item_never_fills_an_http_page(string page, string item) =>
        Assert.False(Lookup(page).Matches(item));

    [Fact]
    public void An_http_item_matches_its_http_page_and_is_asked_about_every_time()
    {
        var plain = Lookup("http://nas.home.arpa/login");
        var secure = Lookup("https://nas.home.arpa/login");

        Assert.True(plain.Matches("http://nas.home.arpa"));
        Assert.True(plain.AsksEveryTime);
        Assert.False(secure.Matches("http://nas.home.arpa"));
        Assert.False(secure.AsksEveryTime);
    }

    [Theory]
    [InlineData("https://github.com:8443/", "https://github.com", false)]
    [InlineData("http://github.com:8080/", "http://github.com", false)]
    [InlineData("https://github.com:443/", "https://github.com", true)]
    [InlineData("https://github.com/", "https://github.com:443", true)]
    [InlineData("https://github.com:8443/", "https://github.com:8443", true)]
    [InlineData("https://github.com:8443/", "github.com:8443", true)]
    [InlineData("https://github.com/", "https://github.com:8443", false)]
    public void An_item_without_a_port_matches_only_the_schemes_default_port(string page, string item, bool matches) =>
        Assert.Equal(matches, Lookup(page).Matches(item));

    [Fact]
    public void A_form_posting_to_another_registrable_domain_is_asked_about_every_time_with_its_origin_named()
    {
        var lookup = Lookup("https://shop.example.com/checkout", "https://pay.other.net/charge?order=1");

        Assert.True(lookup.Matches("https://shop.example.com"));
        Assert.Equal("https://pay.other.net:443", lookup.OtherSiteForm?.ToString());
        Assert.True(lookup.AsksEveryTime);
    }

    [Theory]
    [InlineData("https://auth.example.com/session")]
    [InlineData("http://example.com/session")]
    [InlineData("")]
    [InlineData(null)]
    public void A_form_posting_within_the_registrable_domain_is_not_another_site(string? submitUrl)
    {
        var lookup = Lookup("https://shop.example.com/login", submitUrl);

        Assert.Null(lookup.OtherSiteForm);
        Assert.False(lookup.AsksEveryTime);
    }

    [Theory]
    [InlineData("https://alice.github.io/", "https://bob.github.io/login")]
    [InlineData("https://alice.github.io/", "https://github.io/login")]
    [InlineData("https://localhost/", "https://127.0.0.1/login")]
    public void A_form_posting_to_another_tenant_a_public_suffix_or_another_address_is_another_site(string page, string submitUrl) =>
        Assert.NotNull(Lookup(page, submitUrl).OtherSiteForm);

    [Theory]
    [InlineData("javascript:void(0)", LookupRefusal.NotAWebPage)]
    [InlineData("https://github.com@evil.io/login", LookupRefusal.UserInfo)]
    [InlineData("https://bücher.de/login", LookupRefusal.NotAscii)]
    public void A_form_posting_where_keypaste_cannot_name_gets_no_logins(string submitUrl, LookupRefusal reason)
    {
        Assert.False(LoginLookup.TryRead("https://github.com/login", submitUrl, httpAuthentication: false, out var lookup, out var refusal));
        Assert.Null(lookup);
        Assert.Equal(reason, refusal);
    }

    [Theory]
    [InlineData("KP2A_URL", "https://github.com", true)]
    [InlineData("KP2A_URL_1", "github.com", true)]
    [InlineData("KP2A_URL_2", "\"https://github.com/login\"", true)]
    [InlineData("KP2A_URL_1", "https://gitlab.com", false)]
    [InlineData("Website", "https://github.com", false)]
    [InlineData("kp2a_url_1", "https://github.com", false)]
    public void A_kp2a_url_field_is_honoured(string field, string value, bool matches) =>
        Assert.Equal(matches, Lookup("https://github.com/login").MatchesItem("https://gitlab.com", [new(field, value)]));

    [Fact]
    public void An_item_matches_by_its_url_with_or_without_fields() =>
        Assert.True(Lookup("https://github.com/login").MatchesItem("https://github.com", []));

    [Theory]
    [InlineData("\"https://example.com/login\"", true)]
    [InlineData("  \"https://example.com/login\"  ", true)]
    [InlineData("\"https://example.com/login/\"", false)]
    [InlineData("\"https://EXAMPLE.com/login\"", false)]
    [InlineData("\"https://example.com\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"", false)]
    public void A_quoted_url_matches_only_the_pages_exact_url(string item, bool matches) =>
        Assert.Equal(matches, Lookup("https://example.com/login").Matches(item));

    [Fact]
    public void A_quoted_url_matches_no_subdomain() =>
        Assert.False(Lookup("https://www.example.com/login").Matches("\"https://example.com/login\""));

    [Theory]
    [InlineData("https://*.example.com")]
    [InlineData("*.example.com")]
    [InlineData("https://www.example.com/*")]
    [InlineData("https://www.example.*")]
    [InlineData("https://*")]
    public void A_wildcard_is_not_honoured(string item)
    {
        var lookup = Lookup("https://www.example.com/login");

        Assert.False(lookup.Matches(item));
        Assert.False(lookup.MatchesItem(string.Empty, [new("KP2A_URL_1", item)]));
    }

    [Theory]
    [InlineData("keepassxc://by-uuid/0123456789abcdef0123456789abcdef", LookupRefusal.NotAWebPage)]
    [InlineData("keepassxc://by-path/Internet/GitHub", LookupRefusal.NotAWebPage)]
    [InlineData("file:///home/ana/login.html", LookupRefusal.NotAWebPage)]
    [InlineData("ftp://example.com/", LookupRefusal.NotAWebPage)]
    [InlineData("github.com/login", LookupRefusal.NotAWebPage)]
    [InlineData("https://github.com@evil.io/", LookupRefusal.UserInfo)]
    [InlineData("https://ana:secret@github.com/", LookupRefusal.UserInfo)]
    [InlineData("https://@github.com/", LookupRefusal.UserInfo)]
    [InlineData("https://gіthub.com/", LookupRefusal.NotAscii)]
    [InlineData("https://bücher.de/", LookupRefusal.NotAscii)]
    [InlineData("https://github.com./", LookupRefusal.NotAWebPage)]
    [InlineData("https://.github.com/", LookupRefusal.NotAWebPage)]
    [InlineData("https://git hub.com/", LookupRefusal.NotAWebPage)]
    [InlineData("https://github.com:0/", LookupRefusal.NotAWebPage)]
    [InlineData("https://github.com:65536/", LookupRefusal.NotAWebPage)]
    [InlineData("https://github.com:/", LookupRefusal.NotAWebPage)]
    [InlineData("https://0x7f.0.0.1/", LookupRefusal.NotAWebPage)]
    [InlineData("https://192.168.1/", LookupRefusal.NotAWebPage)]
    [InlineData("https://[::1%25eth0]/", LookupRefusal.NotAWebPage)]
    [InlineData("https:///github.com", LookupRefusal.NotAWebPage)]
    [InlineData("", LookupRefusal.NotAWebPage)]
    [InlineData(null, LookupRefusal.NotAWebPage)]
    public void A_page_keypaste_cannot_name_gets_no_logins(string? url, LookupRefusal reason) =>
        Refused(url, reason);

    [Fact]
    public void Http_authentication_gets_no_logins()
    {
        Assert.False(LoginLookup.TryRead("https://github.com/", null, httpAuthentication: true, out var lookup, out var refusal));
        Assert.Null(lookup);
        Assert.Equal(LookupRefusal.HttpAuthentication, refusal);
    }

    [Theory]
    [InlineData("keepassxc://by-uuid/0123456789abcdef0123456789abcdef")]
    [InlineData("keepassxc://by-path/Internet/GitHub")]
    [InlineData("file:///home/ana/github.html")]
    [InlineData("https://ana@github.com")]
    [InlineData("https://github.com:secret@github.com")]
    [InlineData("https://gіthub.com")]
    [InlineData("mailto:ana@github.com")]
    [InlineData("ssh://github.com")]
    [InlineData("{S:Site}")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_item_address_keypaste_cannot_name_matches_nothing(string? item) =>
        Assert.False(Lookup("https://github.com/").Matches(item));

    [Theory]
    [InlineData("https://Gist.GitHub.com/a/b?c=d#e", "https://gist.github.com:443", "github.com")]
    [InlineData("http://192.168.1.10:8080/", "http://192.168.1.10:8080", "192.168.1.10")]
    [InlineData("http://[::1]/", "http://[::1]:80", "[::1]")]
    [InlineData("https://www.example.co.uk/", "https://www.example.co.uk:443", "example.co.uk")]
    public void A_page_is_named_by_its_origin_and_its_registrable_domain(string page, string origin, string site)
    {
        var lookup = Lookup(page);

        Assert.Equal(origin, lookup.Page.ToString());
        Assert.Equal(site, lookup.Page.Site);
    }

    private static LoginLookup Lookup(string page, string? submitUrl = null)
    {
        Assert.True(LoginLookup.TryRead(page, submitUrl, httpAuthentication: false, out var lookup, out var refusal), $"{page} was refused: {refusal}");
        Assert.Equal(LookupRefusal.None, refusal);
        return lookup;
    }

    private static void Refused(string? page, LookupRefusal reason)
    {
        Assert.False(LoginLookup.TryRead(page, null, httpAuthentication: false, out var lookup, out var refusal));
        Assert.Null(lookup);
        Assert.Equal(reason, refusal);
    }
}
