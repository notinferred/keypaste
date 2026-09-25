using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>What <c>keypaste-mcp</c> accepts on its command line for the run tool and the client label.</summary>
public sealed class ServerOptionsTests
{
    private static bool Parse(out ServerOptions? options, out string error, params string[] argv) =>
        ServerOptions.TryParse(argv, null, null, null, out options, out error);

    [Fact]
    public void Run_IsOffUnlessAllowed()
    {
        Assert.True(Parse(out var plain, out _, "--vault", "v.kdbx"));
        Assert.True(Parse(out var allowed, out _, "--vault", "v.kdbx", "--allow-run"));

        Assert.False(plain!.AllowRun);
        Assert.True(allowed!.AllowRun);
        Assert.NotNull(allowed.VaultKey);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("has/slash")]
    [InlineData("quote\"d")]
    public void ALabelAClientsFileCouldNotHold_IsRefusedAtStartup(string label)
    {
        Assert.False(Parse(out _, out var error, "--client-label", label));
        Assert.Contains("--client-label", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALabelOver64Characters_IsRefusedAtStartup()
    {
        Assert.True(Parse(out _, out _, "--client-label", new string('a', 64)));
        Assert.False(Parse(out _, out _, "--client-label", new string('a', 65)));
    }

    [Fact]
    public void TheUsage_KeepsTheOptionNamesTheDemoChecks_AndNamesAllowRun()
    {
        foreach (var option in new[] { "--vault", "--client-label", "--expose", "--allow-run" })
        {
            Assert.Contains(option, ServerOptions.Usage, StringComparison.Ordinal);
        }
    }
}
