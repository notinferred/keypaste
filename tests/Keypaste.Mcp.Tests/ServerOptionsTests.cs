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
        Assert.NotNull(Assert.Single(allowed.Vaults).Key);
    }

    /// <summary>a bridge nobody widened reaches the variables of tagged entries, and nothing by place.</summary>
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

        Assert.Equal(Path.GetFullPath("chosen.kdbx"), Assert.Single(chosen!.Vaults).Path);
        Assert.NotNull(chosen.Vaults[0].PipeName);
        Assert.Equal(Path.GetFullPath("env.kdbx"), Assert.Single(fromEnvironment!.Vaults).Path);
        Assert.Equal(Path.GetFullPath("flag.kdbx"), Assert.Single(fromFlag!.Vaults).Path);
        Assert.Empty(none!.Vaults);
    }

    /// <summary>a bridge looks for the locked app under its vault's own name, whatever <c>--approver</c> or <c>KEYPASTE_APPROVER</c> pins.</summary>
    [Fact]
    public void TheLockedApp_IsLookedForUnderItsVaultsName_WhicheverApproverIsPinned()
    {
        var home = Path.GetFullPath("bridge-home");
        var expected = LockedAppEndpoint.NameFor(VaultIdentity.Of(KeypasteHome.Resolve(home), Path.GetFullPath("v.kdbx")));

        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx"], null, home, null, null, out var plain, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx", "--approver", "pinned-by-flag"], null, home, null, null, out var flagged, out _));
        Assert.True(ServerOptions.TryParse(["--vault", "v.kdbx"], null, home, "pinned-by-environment", null, out var fromEnvironment, out _));

        Assert.Equal(expected, Assert.Single(plain!.Vaults).LockedAppName);
        Assert.Equal(("pinned-by-flag", expected), (Assert.Single(flagged!.Vaults).PipeName, flagged.Vaults[0].LockedAppName));
        Assert.Equal(("pinned-by-environment", expected), (Assert.Single(fromEnvironment!.Vaults).PipeName, fromEnvironment.Vaults[0].LockedAppName));
    }

    [Fact]
    public void WithoutAVault_NoLockedAppIsLookedFor()
    {
        Assert.True(ServerOptions.TryParse(["--approver", "pinned-by-flag"], null, null, "pinned-by-environment", null, out var pinned, out _));
        Assert.True(Parse(out var help, out _, "--help"));

        Assert.Empty(pinned!.Vaults);
        Assert.Empty(help!.Vaults);
    }

    /// <summary>A tool allowed several vaults names each with --vault; each is asked through its own owner's pipe, named by its file.</summary>
    [Fact]
    public void EachVaultGiven_IsServedThroughItsOwnPipe_AndNamedByItsFile()
    {
        Assert.True(Parse(out var options, out var error, "--vault", "Personal.kdbx", "--vault", "work/Acme.kdbx", "--vault", "Personal.kdbx"), error);

        Assert.Equal(["Personal", "Acme"], options!.Vaults.Select(vault => vault.Name));
        Assert.Equal([Path.GetFullPath("Personal.kdbx"), Path.GetFullPath("work/Acme.kdbx")], options.Vaults.Select(vault => vault.Path));
        Assert.Equal(2, options.Vaults.Select(vault => vault.PipeName).Distinct().Count());
        Assert.Equal(2, options.Vaults.Select(vault => vault.LockedAppName).Distinct().Count());
    }

    [Fact]
    public void TwoVaultsOfOneName_AreNumbered()
    {
        Assert.True(Parse(out var options, out var error, "--vault", "a/Personal.kdbx", "--vault", "b/Personal.kdbx"), error);

        Assert.Equal(["Personal", "Personal 2"], options!.Vaults.Select(vault => vault.Name));
    }

    /// <summary>A pinned pipe is one owner's, so several vaults cannot share it.</summary>
    [Fact]
    public void AnApproverWithSeveralVaults_IsRefusedAtStartup()
    {
        Assert.False(Parse(out _, out var flagged, "--vault", "a.kdbx", "--vault", "b.kdbx", "--approver", "pinned"));
        Assert.False(ServerOptions.TryParse(["--vault", "a.kdbx", "--vault", "b.kdbx"], null, null, "pinned", null, out _, out var fromEnvironment));

        Assert.StartsWith("--approver:", flagged, StringComparison.Ordinal);
        Assert.StartsWith("--approver:", fromEnvironment, StringComparison.Ordinal);
    }

    /// <summary>a bridge never looks for its approver on a locked app's pipe, however the name is cased.</summary>
    [Theory]
    [InlineData("keypaste-locked-0123")]
    [InlineData("KEYPASTE-LOCKED-0123")]
    [InlineData("Keypaste-Locked-0123")]
    public void AnApproverNamingALockedAppsPipe_IsRefusedAtStartup_FromTheFlagOrTheEnvironment(string approver)
    {
        Assert.False(Parse(out _, out var flagged, "--vault", "v.kdbx", "--approver", approver));
        Assert.False(ServerOptions.TryParse(["--vault", "v.kdbx"], null, null, approver, null, out _, out var fromEnvironment));

        Assert.StartsWith("--approver: an approver name cannot start with keypaste-locked-", flagged, StringComparison.Ordinal);
        Assert.StartsWith("--approver: an approver name cannot start with keypaste-locked-", fromEnvironment, StringComparison.Ordinal);
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
    /// <c>keypaste doctor</c> starts a registration as <c>mcp --help</c> and then its own flags, so help put first
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

    /// <summary>the usage names the verbs that replaced <c>mcp serve</c>, <c>setup</c> and <c>policy</c>, and none of the three.</summary>
    [Fact]
    public void TheUsage_KeepsTheOptionNamesTheDemoChecks_AndNamesTheVerbsThatReplacedItsOwn()
    {
        foreach (var name in new[] { "--vault", "--client-label", "--expose", "--allow-run", "`keypaste setup`", "`keypaste approve`", "`keypaste clients`" })
        {
            Assert.Contains(name, ServerOptions.Usage, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"\b(?:serve|policy)\b|mcp setup", ServerOptions.Usage);
    }
}
