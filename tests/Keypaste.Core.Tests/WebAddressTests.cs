using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Which of an entry's URLs keypaste opens in a browser: http and https, and a bare address as https (D-0378).</summary>
public sealed class WebAddressTests
{
    [Theory]
    [InlineData("https://github.com/login", "https://github.com/login")]
    [InlineData("http://intranet.example/", "http://intranet.example/")]
    [InlineData("  HTTPS://Example.com/Path  ", "https://example.com/Path")]
    [InlineData("github.com/login", "https://github.com/login")]
    [InlineData("localhost:8080/admin", "https://localhost:8080/admin")]
    [InlineData("example.com?next=a:b", "https://example.com/?next=a:b")]
    public void A_web_address_opens(string url, string opened)
    {
        Assert.True(WebAddress.TryOpenable(url, out var address));
        Assert.Equal(opened, address.AbsoluteUri);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript://%0aalert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("file://server/share")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("cmd://calc.exe")]
    [InlineData("mailto:ana@example.com")]
    [InlineData("ssh://host")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("https://")]
    [InlineData("https://exa mple.com")]
    public void Anything_else_never_opens(string? url) =>
        Assert.False(WebAddress.TryOpenable(url, out _));

    [Theory]
    [InlineData("https://good.example\u202E/moc.live")]
    [InlineData("https://good.example\u200B.evil.example")]
    public void An_address_drawn_differently_from_how_it_opens_never_opens(string url) =>
        Assert.False(WebAddress.TryOpenable(url, out _));
}
