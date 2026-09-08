using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The CLI is published as the project rather than as a person.
/// </summary>
public sealed class PublisherMetadataTests
{
    [Fact]
    public void It_is_published_as_the_project() =>
        PublisherMetadata.IsThisProject(typeof(Program).Assembly);
}
