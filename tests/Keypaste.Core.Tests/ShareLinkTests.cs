using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A share link carries its id and key in the fragment, and nothing else is a link.</summary>
public sealed class ShareLinkTests
{
    private const string _id = "Qm9vYmFyQm9vYmFyQm9vYg";
    private const string _key = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA";

    [Fact]
    public void Format_PutsTheIdAndKeyInTheFragment()
    {
        Assert.Equal($"https://keypaste.com/s/#{_id}.{_key}", ShareLink.Format(ShareEndpoint.Default, _id, _key));
        Assert.Equal($"http://127.0.0.1:8787/s/#{_id}.{_key}", ShareLink.Format(new Uri("http://127.0.0.1:8787/"), _id, _key));
    }

    [Fact]
    public void TryParse_ReadsWhatFormatWrote()
    {
        Assert.True(ShareLink.TryParse(ShareLink.Format(ShareEndpoint.Default, _id, _key), out var id, out var key));
        Assert.Equal(_id, id);
        Assert.Equal(_key, key);
    }

    [Theory]
    [InlineData("https://keypaste.com/s/")]
    [InlineData("https://keypaste.com/s/#" + _id)]
    [InlineData("https://keypaste.com/s/#" + _id + "." + "AQID")]
    [InlineData("https://keypaste.com/s/#" + _id + "x." + _key)]
    [InlineData("https://keypaste.com/s/#" + _id + "." + _key + "=")]
    [InlineData("https://keypaste.com/s/#" + _id + ".+QIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA")]
    [InlineData("https://keypaste.com/" + _id + "." + _key)]
    public void TryParse_RefusesAnythingElse(string link)
    {
        Assert.False(ShareLink.TryParse(link, out _, out _));
    }

    [Fact]
    public void IsId_AcceptsOnlyTheServersShape()
    {
        Assert.True(ShareLink.IsId(_id));
        Assert.False(ShareLink.IsId(_id[..21]));
        Assert.False(ShareLink.IsId(_id + "A"));
        Assert.False(ShareLink.IsId("Qm9vYmFyQm9vYmFyQm9v/g"));
        Assert.False(ShareLink.IsId("../../../../api/share0"));
    }
}
