using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>How a grant's length reads on the prompt that offers it.</summary>
public sealed class ApprovalLimitsTests
{
    [Theory]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    [InlineData(300, "5 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(90, "90 seconds")]
    [InlineData(1, "1 second")]
    public void Describe_ReadsAsAPersonSaysIt(int seconds, string expected) =>
        Assert.Equal(expected, ApprovalLimits.Describe(seconds));

    [Fact]
    public void ThePersonsTimedGrant_DefaultsToAnHour() =>
        Assert.Equal(3600, ApprovalLimits.Default.MaximumTtlSeconds);
}
