using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>What <c>keypaste mcp</c> accepts on its command line for help, the run tool, the client label, the ceilings it may lower and where it looks for a locked app.</summary>
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

    /// <summary>
    /// The ceilings an agent's request for less lowers while nothing holds the vault are the ones
    /// <c>--clients</c> names, as setup pins them beside the log for a home whose variable a client may
    /// strip; without it, the home's, and only while the bridge's log is the home's too, since a log
    /// pinned elsewhere leaves it none it can tell the owner reads.
    /// </summary>
    [Fact]
    public void TheCeilingsTheBridgeLowers_AreTheOnesPinned_ElseItsHomesWhileItsLogIsTheHomesToo()
    {
        var home = Path.GetFullPath("bridge-home");
        var custom = Path.GetFullPath("custom-home");

        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx"], null, home, null, null, out var plain, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx", "--audit-log", KeypasteHome.AuditPath(home)], null, home, null, null, out var pinned, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx", "--audit-log", Path.Combine(home, "other", KeypasteHome.AuditFileName)], null, home, null, null, out var elsewhere, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx", "--audit-log", KeypasteHome.AuditPath(home)], null, null, null, null, out var stripped, out _));
        Assert.True(ServerOptions.TryParse(
            ["--vault", "v.kdbx", "--audit-log", KeypasteHome.AuditPath(custom), "--clients", KeypasteHome.ClientsPath(custom)], null, null, null, null, out var setUp, out _));

        Assert.Equal(KeypasteHome.ClientsPath(home), plain!.ClientsPath);
        Assert.Equal(KeypasteHome.ClientsPath(home), pinned!.ClientsPath);
        Assert.Null(elsewhere!.ClientsPath);
        Assert.Null(stripped!.ClientsPath);
        Assert.Equal(KeypasteHome.ClientsPath(custom), setUp!.ClientsPath);
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

    /// <summary>D-0466: a bridge looks for the locked app under its vault's own name, whatever <c>--approver</c> or <c>KEYPASTE_APPROVER</c> pins.</summary>
    [Fact]
    public void TheLockedApp_IsLookedForUnderItsVaultsName_WhicheverApproverIsPinned()
    {
        var home = Path.GetFullPath("bridge-home");
        var expected = LockedAppEndpoint.NameFor(VaultIdentity.Of(KeypasteHome.Resolve(home), Path.GetFullPath("v.kdbx")));

        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx"], null, home, null, null, out var plain, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx", "--approver", "pinned-by-flag"], null, home, null, null, out var flagged, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx"], null, home, "pinned-by-environment", null, out var fromEnvironment, out _));

        Assert.Equal(expected, plain!.LockedAppName);
        Assert.Equal(("pinned-by-flag", expected), (flagged!.ApproverName, flagged.LockedAppName));
        Assert.Equal(("pinned-by-environment", expected), (fromEnvironment!.ApproverName, fromEnvironment.LockedAppName));
    }

    [Fact]
    public void WithoutAVault_NoLockedAppIsLookedFor()
    {
        Assert.True(ServerOptions.TryParse(["--approver", "pinned-by-flag"], null, null, "pinned-by-environment", null, out var pinned, out _));
        Assert.True(Parse(out var help, out _, "--help"));

        Assert.Equal("pinned-by-flag", pinned!.ApproverName);
        Assert.Null(pinned.LockedAppName);
        Assert.Null(help!.LockedAppName);
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

    /// <summary>
    /// D-0467: <c>keypaste doctor</c> starts a registration as <c>mcp --help</c> and then its own flags, so help put first
    /// is help whatever follows and names no audit log to open, and a flag left without its value fails rather than taking it.
    /// </summary>
    [Fact]
    public void HelpBeforeEveryPinnedFlag_IsHelp_AndNamesNoAuditLog()
    {
        Assert.True(Parse(
            out var options,
            out _,
            "--help",
            "--vault", "v.kdbx",
            "--audit-log", "audit.jsonl",
            "--clients", "clients.toml",
            "--approver", "pinned",
            "--client-label", "cursor",
            "--allow-run",
            "--expose", "**"));

        Assert.True(options!.WantsHelp);
        Assert.Empty(options.AuditPath);
        Assert.False(Parse(out _, out _, "--help", "--client-label"));
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
