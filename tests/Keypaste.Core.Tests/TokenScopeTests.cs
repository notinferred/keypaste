using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What a token may read: <c>read:&lt;project&gt;/&lt;profile&gt;/&lt;key or *&gt;</c>, and nothing wider.</summary>
public sealed class TokenScopeTests
{
    [Theory]
    [InlineData("read:a/dev/*", "a", "dev", null)]
    [InlineData("read:a/prod/KEY", "a", "prod", "KEY")]
    [InlineData("read:acme-api/staging/DATABASE_URL", "acme-api", "staging", "DATABASE_URL")]
    public void Accepts(string text, string project, string profile, string? key)
    {
        Assert.True(TokenScope.TryParseList(text, out var scopes, out var error), error);

        Assert.Equal(new TokenScope(project, profile, key), Assert.Single(scopes));
        Assert.Equal(text, scopes[0].ToString());
    }

    [Fact]
    public void AcceptsAList_InOrder_WithoutRepeats()
    {
        Assert.True(TokenScope.TryParseList("read:a/dev/*, read:b/staging/KEY,read:a/dev/*", out var scopes, out var error), error);

        Assert.Equal([new("a", "dev", null), new("b", "staging", "KEY")], scopes);
    }

    [Theory]
    [InlineData("write:a/dev/*")]
    [InlineData("a/dev/*")]
    [InlineData("read:a/dev")]
    [InlineData("read:a")]
    [InlineData("read:a/dev/KEY/extra")]
    [InlineData("read:*/dev/*")]
    [InlineData("read:a/*/*")]
    [InlineData("read:a/Dev/*")]
    [InlineData("read:a/dev/BAD-KEY")]
    [InlineData("read:/dev/*")]
    [InlineData("read:a:b/dev/*")]
    [InlineData("")]
    public void Rejects(string text)
    {
        Assert.False(TokenScope.TryParseList(text, out var scopes, out var error));
        Assert.Null(scopes);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Rejects_SeventeenScopes()
    {
        var text = string.Join(",", Enumerable.Range(0, 17).Select(i => $"read:p{i}/dev/*"));

        Assert.False(TokenScope.TryParseList(text, out _, out var error));
        Assert.Contains("16", error, StringComparison.Ordinal);
        Assert.True(TokenScope.TryParseList(string.Join(",", Enumerable.Range(0, 16).Select(i => $"read:p{i}/dev/*")), out _, out _));
    }

    [Fact]
    public void Rejects_AProjectWithAComma()
    {
        Assert.False(TokenScope.TryParse("read:a,b/dev/*", out _, out _));
        Assert.False(TokenScope.TryParseList("read:a,b/dev/*", out _, out _));
    }

    [Fact]
    public void KeyScopes_AddUp_AndAWildcardWins()
    {
        var keys = Info("read:a/dev/ONE,read:a/dev/TWO,read:a/staging/*,read:b/dev/THREE");

        Assert.True(keys.Covers("a", "dev"));
        Assert.True(keys.Covers("a", "staging"));
        Assert.False(keys.Covers("a", "prod"));
        Assert.False(keys.Covers("c", "dev"));
        Assert.Equal(["ONE", "TWO"], keys.KeysFor("a", "dev"));
        Assert.Null(keys.KeysFor("a", "staging"));
        Assert.Empty(keys.KeysFor("c", "dev")!);
        Assert.Equal([("a", "dev"), ("a", "staging"), ("b", "dev")], keys.Pairs);

        Assert.Null(Info("read:a/dev/ONE,read:a/dev/*").KeysFor("a", "dev"));
    }

    private static TokenInfo Info(string scopes)
    {
        Assert.True(TokenScope.TryParseList(scopes, out var parsed, out var error), error);
        return new TokenInfo("00000000", "t", parsed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), AllowProd: false);
    }
}
