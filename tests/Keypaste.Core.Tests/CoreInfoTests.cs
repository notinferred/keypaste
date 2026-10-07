using Xunit;

namespace Keypaste.Core.Tests;

public sealed class CoreInfoTests
{
    [Fact]
    public void Version_IsNotThePlaceholder()
    {
        Assert.NotEqual("0.0.0-unknown", CoreInfo.Version);
    }

    [Fact]
    public void Version_CarriesNoBuildMetadata()
    {
        Assert.DoesNotContain("+", CoreInfo.Version, StringComparison.Ordinal);
    }
}
