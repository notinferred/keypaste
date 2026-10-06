using Keypaste.Core;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>What <c>keypaste mcp</c> accepts on its command line for help, the run tool and the client label.</summary>
public sealed class ServerOptionsTests
{
    private static bool Parse(out ServerOptions? options, out string error, params string[] argv) =>
        ServerOptions.TryParse(argv, null, null, null, null, out options, out error);

    [Fact]
    public void Run_IsOffUnlessAllowed()
    {
        Assert.True(Parse(out var plain, out _, "--vault", "v.kdbx"));
        Assert.True(Parse(out var allowed, out _, "--vault", "v.kdbx", "--allow-run"));

        Assert.False(plain!.AllowRun);
        Assert.True(allowed!.AllowRun);
        Assert.NotNull(allowed.VaultKey);
    }

    /// <summary>D-0422: a bridge nobody widened reaches the variables of tagged entries, and nothing by place.</summary>
    [Fact]
    public void WithNoExpose_TheExposureIsEveryProjectsVariables()
    {
        Assert.True(Parse(out var options, out _, "--vault", "v.kdbx"));

        Assert.Equal([EntryExposure.DefaultGlob], options!.Exposure.Globs);
        Assert.Equal("tag:env:*", EntryExposure.DefaultGlob);
    }

    [Fact]
    public void AMalformedTagSelector_IsRefusedAtStartup()
    {
        Assert.False(Parse(out _, out var error, "--expose", "tag:team"));
        Assert.StartsWith("--expose:", error, StringComparison.Ordinal);
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
    public void WithNoVaultNamed_TheBridgeAsksAboutTheChosenVault()
    {
        Assert.True(ServerOptions.TryParse([], null, null, null, "chosen.kdbx", out var chosen, out _));
        Assert.True(ServerOptions.TryParse([], "env.kdbx", null, null, "chosen.kdbx", out var fromEnvironment, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "flag.kdbx"], "env.kdbx", null, null, "chosen.kdbx", out var fromFlag, out _));
        Assert.True(ServerOptions.TryParse([], null, null, null, null, out var none, out _));

        Assert.Equal(Path.GetFullPath("chosen.kdbx"), chosen!.VaultPath);
        Assert.NotNull(chosen.VaultKey);
        Assert.NotNull(chosen.ApproverName);
        Assert.Equal(Path.GetFullPath("env.kdbx"), fromEnvironment!.VaultPath);
        Assert.Equal(Path.GetFullPath("flag.kdbx"), fromFlag!.VaultPath);
        Assert.Empty(none!.VaultPath);
        Assert.Null(none.VaultKey);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("--vault", "v.kdbx", "--help")]
    public void EveryHelpForm_AsksForTheOneUsage(params string[] argv)
    {
        Assert.True(Parse(out var options, out _, argv));
        Assert.True(options!.WantsHelp);
    }

    [Fact]
    public void AnArgumentOtherThanHelp_IsRefused()
    {
        Assert.False(Parse(out _, out var error, "--allow-run", "serve"));
        Assert.Contains("'serve'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUsage_KeepsTheOptionNamesTheDemoChecks_AndNamesTheVerbs()
    {
        foreach (var name in new[] { "--vault", "--client-label", "--expose", "--allow-run", "serve", "setup", "policy" })
        {
            Assert.Contains(name, ServerOptions.Usage, StringComparison.Ordinal);
        }
    }
}
