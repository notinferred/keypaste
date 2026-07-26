using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The rule the CLI and the MCP bridge both have to answer the same way (CORE.md law 4.3). The
/// CLI's own tests still exercise it through <c>VaultLocator</c>, which is what proves the move
/// preserved behaviour rather than merely compiling.
/// </summary>
public sealed class VaultLocationTests
{
    [Fact]
    public void TheFlagWins()
    {
        Assert.True(VaultLocation.TryResolve("chosen.kdbx", "ignored.kdbx", out var path, out var error));

        Assert.Equal(Path.GetFullPath("chosen.kdbx"), path);
        Assert.Empty(error);
    }

    [Fact]
    public void TheEnvironmentIsTheFallback()
    {
        Assert.True(VaultLocation.TryResolve(null, "fallback.kdbx", out var path, out var error));

        Assert.Equal(Path.GetFullPath("fallback.kdbx"), path);
        Assert.Empty(error);
    }

    /// <summary>
    /// The refusal is the point. A credential tool that guesses which vault you meant eventually
    /// writes a secret into the wrong file.
    /// </summary>
    [Fact]
    public void WithNeither_ItRefusesAndSaysHow()
    {
        Assert.False(VaultLocation.TryResolve(null, null, out var path, out var error));

        Assert.Empty(path);
        Assert.Contains(VaultLocation.EnvironmentVariable, error, StringComparison.Ordinal);
        Assert.Contains("--vault", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>KEYPASTE_VAULT= keypaste ls</c> should complain that no vault was given, not that the
    /// empty string is missing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyValueCountsAsUnset(string? empty)
    {
        Assert.False(VaultLocation.TryResolve(empty, empty, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ThePathIsMadeAbsolute()
    {
        Assert.True(VaultLocation.TryResolve("./nested/../vault.kdbx", null, out var path, out _));

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(Path.GetFullPath("vault.kdbx"), path);
    }
}
