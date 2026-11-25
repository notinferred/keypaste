using Keypaste.Core.Browser;
using Xunit;

namespace Keypaste.Core.Tests.Browser;

/// <summary>Which matched items a page may fill and which KeePassXC's browser settings or an expiry keep out, and why.</summary>
public sealed class LoginCandidatesTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_matching_items_are_considered_and_an_unnarrowed_one_is_a_candidate()
    {
        var (candidates, refused) = LoginCandidates.Of(Lookup("https://github.com/login"), [Item("GitHub", "https://github.com"), Item("Bank", "https://bank.example")], _now);

        Assert.Equal("GitHub", Assert.Single(candidates).Name.Title);
        Assert.Empty(refused);
    }

    [Fact]
    public void An_item_matching_by_a_kp2a_url_field_is_a_candidate()
    {
        var item = Item("GitHub", "https://example.org") with { Addresses = [new("KP2A_URL_1", "https://github.com")] };

        Assert.Single(LoginCandidates.Of(Lookup("https://github.com/"), [item], _now).Candidates);
    }

    [Theory]
    [InlineData(1, "KeePassXC hides it from browsers")]
    [InlineData(2, "KeePassXC offers it only to HTTP authentication")]
    [InlineData(4, "KeePassXC offers it only to one of its own browser connections")]
    public void KeePassXCs_narrowing_settings_keep_a_matched_item_out_with_their_reason(int setting, string reason)
    {
        var narrowing = (BrowserNarrowing)setting;
        var (candidates, refused) = LoginCandidates.Of(Lookup("https://github.com/"), [Item("GitHub", "https://github.com") with { Narrowing = narrowing }], _now);

        Assert.Empty(candidates);
        Assert.Equal(reason, Assert.Single(refused).Reason);
    }

    [Theory]
    [InlineData("https://github.com/", null, "github.com")]
    [InlineData("https://github.com/", "https://collector.example/post", "collector.example")]
    public void A_host_KeePassXC_was_told_to_deny_keeps_the_item_out_of_the_page_or_its_form(string page, string? submitUrl, string denied)
    {
        var item = Item("GitHub", "https://github.com") with { DeniedHosts = [denied] };

        var (candidates, refused) = LoginCandidates.Of(Lookup(page, submitUrl), [item], _now);

        Assert.Empty(candidates);
        Assert.Equal($"KeePassXC was told to deny it to {denied}", Assert.Single(refused).Reason);
    }

    [Fact]
    public void A_deny_list_naming_another_host_and_an_allow_list_change_nothing()
    {
        var item = Item("GitHub", "https://github.com") with { DeniedHosts = ["gist.github.com"] };

        Assert.Single(LoginCandidates.Of(Lookup("https://github.com/"), [item], _now).Candidates);
    }

    [Fact]
    public void An_expired_item_is_kept_out_and_one_expiring_later_is_not()
    {
        var (candidates, refused) = LoginCandidates.Of(
            Lookup("https://github.com/"),
            [Item("Old", "https://github.com") with { Expires = _now }, Item("New", "https://github.com") with { Expires = _now.AddDays(1) }],
            _now);

        Assert.Equal("New", Assert.Single(candidates).Name.Title);
        Assert.Equal("expired 2026-11-16 12:00:00Z", Assert.Single(refused).Reason);
    }

    private static BrowserItem Item(string title, string url) =>
        new(new EntryName("web", title) { Uuid = Guid.NewGuid().ToString("N") }, "dana", url, null);

    private static LoginLookup Lookup(string page, string? submitUrl = null)
    {
        Assert.True(LoginLookup.TryRead(page, submitUrl, httpAuthentication: false, out var lookup, out _));
        return lookup;
    }
}
