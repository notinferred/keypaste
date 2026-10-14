using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The UUIDs of agents' entries and keypaste's own groups are RFC 9562's version 5 UUIDs of their names
/// in ACAF's namespace, so any writer reproduces them. The expected values are Python's
/// <c>uuid.uuid5</c> of the same inputs.
/// </summary>
public sealed class AcafIdsTests
{
    [Fact]
    public void TheNamespace_IsTheVersionFiveUuidOfTheSpecificationsUrl() =>
        Assert.Equal(new Guid("919d7b74-6e77-5fb0-99eb-74758c119d7d"), AcafIds.Namespace);

    [Theory]
    [InlineData("claude-code", "a1e1d967-675e-5db1-987a-5faf0dbe0627")]
    [InlineData("*", "f6fc6f43-949a-5159-b8ad-cdff2e6c90f8")]
    [InlineData("agent-éエ", "1d196cf3-67da-5079-b0ca-01f388873641")]
    public void AnAgentsName_DerivesTheVersionFiveUuidOfItsUtf8Bytes(string name, string expected) =>
        AssertVersionFive(new Guid(expected), AcafIds.Agent(name));

    [Theory]
    [InlineData(ReservedGroups.Root, "0fe73e58-3954-5d56-bca4-c9696b45564a")]
    [InlineData(ReservedGroups.Agents, "ecef397c-6fd7-5243-8bd5-c0a96d98a672")]
    public void AGroupsPath_DerivesTheVersionFiveUuidOfItAfterASlash(string groupPath, string expected) =>
        AssertVersionFive(new Guid(expected), AcafIds.Group(groupPath));

    [Fact]
    public void NamesThatDifferOnlyInCase_AreDifferentEntries() =>
        Assert.NotEqual(AcafIds.Agent("claude-code"), AcafIds.Agent("Claude-Code"));

    private static void AssertVersionFive(Guid expected, Guid actual)
    {
        var bytes = actual.ToByteArray(bigEndian: true);

        Assert.Equal(expected, actual);
        Assert.Equal(0x50, bytes[6] & 0xF0);
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }
}
