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

    [Fact]
    public void Hello_IdentifiesCoreAndItsVersion()
    {
        var greeting = CoreInfo.Hello();

        Assert.Contains("keypaste-core", greeting, StringComparison.Ordinal);
        Assert.Contains(CoreInfo.Version, greeting, StringComparison.Ordinal);
    }
}
