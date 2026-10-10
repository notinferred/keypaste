using Keypaste.Core.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Release versions order by Semantic Versioning 2.0's precedence, so a newer copy knows it is newer (G.5).</summary>
public sealed class SemanticVersionTests
{
    [Fact]
    public void The_specifications_example_chain_is_in_order()
    {
        string[] chain = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"];

        for (var i = 1; i < chain.Length; i++)
        {
            var lower = Parse(chain[i - 1]);
            var higher = Parse(chain[i]);

            Assert.True(lower < higher, $"{lower} should precede {higher}");
            Assert.True(higher > lower, $"{higher} should follow {lower}");
            Assert.True(lower.CompareTo(higher) < 0 && higher.CompareTo(lower) > 0);
        }
    }

    [Theory]
    [InlineData("0.0.1-upgrade", "0.0.2-upgrade")]
    [InlineData("0.5.0-rc.1", "0.5.0")]
    [InlineData("0.4.0", "0.5.0-rc.1")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.0-9", "1.0.0-10")]
    [InlineData("1.0.0-999", "1.0.0-a")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc-1")]
    [InlineData("1.0.0-Z", "1.0.0-a")]
    public void The_first_precedes_the_second(string first, string second)
    {
        Assert.True(Parse(first) < Parse(second));
        Assert.True(Parse(second) >= Parse(first));
        Assert.False(Parse(first) >= Parse(second));
    }

    [Theory]
    [InlineData("0.5.0")]
    [InlineData("0.5.0-rc.1")]
    [InlineData("1.0.0-alpha-1.0.x-y")]
    [InlineData("10.20.30")]
    public void A_version_reads_back_as_it_was_written_and_equals_itself(string text)
    {
        var version = Parse(text);

        Assert.Equal(text, version.ToString());
        Assert.Equal(version, Parse(text));
        Assert.Equal(0, version.CompareTo(Parse(text)));
        Assert.True(version <= Parse(text) && version >= Parse(text));
    }

    [Fact]
    public void Its_parts_are_read()
    {
        var version = Parse("1.22.333-rc.4");

        Assert.Equal(1, version.Major);
        Assert.Equal(22, version.Minor);
        Assert.Equal(333, version.Patch);
        Assert.Equal("rc.4", version.Prerelease);
        Assert.Null(Parse("1.2.3").Prerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.00.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-a..b")]
    [InlineData("1.0.0-a_b")]
    [InlineData("1.0.0+x")]
    [InlineData("1.0.0-rc.1+x")]
    [InlineData("v1.0.0")]
    [InlineData(" 1.0.0")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.-1")]
    [InlineData("99999999999.0.0")]
    [InlineData("1.0.0-é")]
    public void A_malformed_version_is_refused(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    private static SemanticVersion Parse(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), $"'{text}' did not parse");
        return version;
    }
}
