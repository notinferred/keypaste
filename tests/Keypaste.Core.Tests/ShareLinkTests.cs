using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A share link carries its id and key in the fragment, and nothing else is a link.</summary>
public sealed class ShareLinkTests
{
    private const string Id = "Qm9vYmFyQm9vYmFyQm9vYg";
    private const string Key = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA";

    [Fact]
    public void Format_PutsTheIdAndKeyInTheFragment()
    {
        Assert.Equal($"https://keypaste.com/s/#{Id}.{Key}", ShareLink.Format(ShareEndpoint.Default, Id, Key));
        Assert.Equal($"http://127.0.0.1:8787/s/#{Id}.{Key}", ShareLink.Format(new Uri("http://127.0.0.1:8787/"), Id, Key));
    }

    [Fact]
    public void TryParse_ReadsWhatFormatWrote()
    {
        Assert.True(ShareLink.TryParse(ShareLink.Format(ShareEndpoint.Default, Id, Key), out var id, out var key));
        Assert.Equal(Id, id);
        Assert.Equal(Key, key);
    }

    [Theory]
    [InlineData("https://keypaste.com/s/")]
    [InlineData("https://keypaste.com/s/#" + Id)]
    [InlineData("https://keypaste.com/s/#" + Id + "." + "AQID")]
    [InlineData("https://keypaste.com/s/#" + Id + "x." + Key)]
    [InlineData("https://keypaste.com/s/#" + Id + "." + Key + "=")]
    [InlineData("https://keypaste.com/s/#" + Id + ".+QIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA")]
    [InlineData("https://keypaste.com/" + Id + "." + Key)]
    public void TryParse_RefusesAnythingElse(string link)
    {
        Assert.False(ShareLink.TryParse(link, out _, out _));
    }

    [Fact]
    public void IsId_AcceptsOnlyTheServersShape()
    {
        Assert.True(ShareLink.IsId(Id));
        Assert.False(ShareLink.IsId(Id[..21]));
        Assert.False(ShareLink.IsId(Id + "A"));
        Assert.False(ShareLink.IsId("Qm9vYmFyQm9vYmFyQm9v/g"));
        Assert.False(ShareLink.IsId("../../../../api/share0"));
    }
}
