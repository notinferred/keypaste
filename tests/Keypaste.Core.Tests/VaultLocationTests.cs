using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The rule the CLI and the MCP bridge both answer the same way (docs/PRODUCT.md law 4.3):
/// <c>--vault</c>, then <c>KEYPASTE_VAULT</c>, then the chosen vault, then nothing (D-0389).
/// </summary>
public sealed class VaultLocationTests
{
    [Fact]
    public void TheFlagWins()
    {
        Assert.True(VaultLocation.TryResolve("flag.kdbx", "ignored.kdbx", "chosen.kdbx", out var path, out var error));

        Assert.Equal(Path.GetFullPath("flag.kdbx"), path);
        Assert.Empty(error);
    }

    [Fact]
    public void TheEnvironmentWinsOverTheChoice()
    {
        Assert.True(VaultLocation.TryResolve(null, "fallback.kdbx", "chosen.kdbx", out var path, out var error));

        Assert.Equal(Path.GetFullPath("fallback.kdbx"), path);
        Assert.Empty(error);
    }

    [Fact]
    public void TheChosenVault_IsUsedWhenNothingNamesOne()
    {
        Assert.True(VaultLocation.TryResolve(null, null, "chosen.kdbx", out var path, out var error));

        Assert.Equal(Path.GetFullPath("chosen.kdbx"), path);
        Assert.Empty(error);
    }

    [Fact]
    public void WithNone_ItRefusesAndNamesTheNextStep()
    {
        Assert.False(VaultLocation.TryResolve(null, null, null, out var path, out var error));

        Assert.Empty(path);
        Assert.Contains("keypaste use <path>", error, StringComparison.Ordinal);
        Assert.Contains("keypaste app", error, StringComparison.Ordinal);
        Assert.Contains(VaultLocation.EnvironmentVariable, error, StringComparison.Ordinal);
        Assert.Contains("--vault", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyValueCountsAsUnset(string? empty)
    {
        Assert.False(VaultLocation.TryResolve(empty, empty, empty, out _, out var error));
        Assert.NotEmpty(error);

        Assert.True(VaultLocation.TryResolve(empty, empty, "chosen.kdbx", out var path, out _));
        Assert.Equal(Path.GetFullPath("chosen.kdbx"), path);
    }

    [Fact]
    public void ThePathIsMadeAbsolute()
    {
        Assert.True(VaultLocation.TryResolve("./nested/../vault.kdbx", null, null, out var path, out _));

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(Path.GetFullPath("vault.kdbx"), path);
    }
}
