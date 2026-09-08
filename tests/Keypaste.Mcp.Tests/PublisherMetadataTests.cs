using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The MCP server is published as the project rather than as a person.
/// </summary>
public sealed class PublisherMetadataTests
{
    [Fact]
    public void It_is_published_as_the_project() =>
        PublisherMetadata.IsThisProject(typeof(Program).Assembly);
}
