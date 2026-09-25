using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Which host a share is made on decides which page reads its key, so the environment may name only
/// a local development server and anything else takes a deliberate <c>--endpoint</c>.
/// </summary>
public sealed class ShareEndpointTests
{
    [Fact]
    public void Nothing_IsKeypasteCom()
    {
        Assert.True(ShareEndpoint.TryResolve(null, null, out var endpoint, out _));
        Assert.Equal(ShareEndpoint.Default, endpoint);
        Assert.True(ShareEndpoint.IsDefault(endpoint));
        Assert.True(ShareEndpoint.TryResolve(null, string.Empty, out endpoint, out _));
        Assert.True(ShareEndpoint.IsDefault(endpoint));
    }

    [Theory]
    [InlineData("https://share.example.org")]
    [InlineData("https://share.example.org/")]
    [InlineData("https://share.example.org:8443")]
    public void Option_AcceptsAnHttpsOrigin(string option)
    {
        Assert.True(ShareEndpoint.TryResolve(option, "https://ignored.example", out var endpoint, out var error), error);
        Assert.Equal("https", endpoint.Scheme);
        Assert.Equal("/", endpoint.AbsolutePath);
        Assert.False(ShareEndpoint.IsDefault(endpoint));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787")]
    [InlineData("https://127.0.0.1:8787")]
    [InlineData("http://localhost:8787/")]
    [InlineData("http://[::1]:8787")]
    public void Environment_AcceptsALoopbackServer(string value)
    {
        Assert.True(ShareEndpoint.TryResolve(null, value, out var endpoint, out var error), error);
        Assert.False(ShareEndpoint.IsDefault(endpoint));
    }

    [Theory]
    [InlineData("https://share.example.org")]
    [InlineData("https://keypaste.com.evil.example")]
    [InlineData("http://share.example.org")]
    [InlineData("http://127.0.0.2:8787")]
    [InlineData("http://localhost.evil.example")]
    public void Environment_RefusesAnythingNotLoopback(string value)
    {
        Assert.False(ShareEndpoint.TryResolve(null, value, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.Contains("KEYPASTE_SHARE_URL may only name a local development server", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://share.example.org")]
    [InlineData("http://127.0.0.1:8787")]
    [InlineData("https://share.example.org/path")]
    [InlineData("https://share.example.org/?q=1")]
    [InlineData("https://share.example.org/#x")]
    [InlineData("https://user:pass@share.example.org")]
    [InlineData("share.example.org")]
    [InlineData("/relative")]
    [InlineData("ftp://share.example.org")]
    public void Option_RefusesWhatIsNotAnHttpsOrigin(string option)
    {
        Assert.False(ShareEndpoint.TryResolve(option, null, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.StartsWith("--endpoint", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://keypaste.com", true)]
    [InlineData("https://share.example.org", true)]
    [InlineData("http://127.0.0.1:8787", true)]
    [InlineData("http://share.example.org", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Recorded_AcceptsOnlyWhatCouldHaveMadeAShare(string recorded, bool accepted)
    {
        Assert.Equal(accepted, ShareEndpoint.TryParseRecorded(recorded, out _));
    }
}
