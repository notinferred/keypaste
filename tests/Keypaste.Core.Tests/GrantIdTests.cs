using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The short id a person revokes a grant by: derived from the grant, the same everywhere.</summary>
public sealed class GrantIdTests
{
    private static readonly GrantKey _key = new("conn-1", EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY")), "password");

    [Fact]
    public void IsEightLowercaseHex()
    {
        foreach (var id in new[] { GrantId.Of(_key), GrantId.OfEnv("env\0billing\0/home/me\0npm") })
        {
            Assert.Equal(8, id.Length);
            Assert.All(id, c => Assert.True(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f', $"'{c}' in {id}"));
            Assert.True(GrantId.IsId(id));
            Assert.True(GrantId.IsId(id.ToUpperInvariant()));
        }
    }

    [Fact]
    public void IsStable()
    {
        Assert.Equal(GrantId.Of(_key), GrantId.Of(_key with { }));
        Assert.Equal(GrantId.OfEnv("k"), GrantId.OfEnv("k"));
    }

    [Fact]
    public void DiffersByField()
    {
        Assert.NotEqual(GrantId.Of(_key), GrantId.Of(_key with { Field = "username" }));
        Assert.NotEqual(GrantId.Of(_key), GrantId.Of(_key with { ConnectionId = "conn-2" }));
        Assert.NotEqual(GrantId.OfEnv("a"), GrantId.OfEnv("b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("3f9a1c0")]
    [InlineData("3f9a1c021")]
    [InlineData("3f9a1c0g")]
    [InlineData("claude-c")]
    public void AnythingElse_IsNotAnId(string text) => Assert.False(GrantId.IsId(text));
}
