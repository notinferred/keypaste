using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The core assembly is published as the project; the vendored one is published as upstream.
/// </summary>
public sealed class PublisherMetadataTests
{
    [Fact]
    public void The_core_assembly_is_published_as_the_project() =>
        PublisherMetadata.IsThisProject(typeof(Vault).Assembly);

    /// <summary>
    /// third_party/Directory.Build.props severs its inheritance from the root file, so this is the
    /// assertion that the severing is doing its job in both directions: upstream's notice survives
    /// distribution, and keypaste's identity never lands on somebody else's code.
    /// </summary>
    [Fact]
    public void The_vendored_assembly_is_published_as_upstream() =>
        PublisherMetadata.IsUpstreamKeePassLib(typeof(KeePassLib.PwUuid).Assembly);
}
