using Keypaste.Cli.Commands;
using Keypaste.Cli.Execution;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste run</c> at the command level.
/// </summary>
/// <remarks>
/// Everything the child actually sees is decided before it starts — the file name, the argument
/// list, and the environment — so all of that is asserted here against a fake launcher. What
/// cannot be reached in-process is inherited stdio and real signals, and those are gated by
/// <c>scripts/verify-run-injection.sh</c> and <c>scripts/verify-run-signals.sh</c>.
/// </remarks>
public sealed class RunCommandTests
{
    internal const string Master = "run-master-pw";

    private static CliHarness Seeded(params (string Key, string Value)[] variables)
    {
        var harness = new CliHarness();
        harness.SeedVault(Master);

        foreach (var (key, value) in variables)
        {
            harness.Prompt.Enqueue(Master, value);

            // Checked, so a seeding failure says so here rather than surfacing later as an
            // assertion about something else entirely.
            harness.AssertExit(
                CliApp.ExitSuccess,
                harness.Run("env", "set", "dev", key, "--vault", harness.VaultPath));
        }

        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
        return harness;
    }

    // ---- the split ---------------------------------------------------------------------

    [Fact]
    public void Split_WithNoSeparator_ReportsSo()
    {
        var split = RunCommand.Split(["run", "dev"]);

        Assert.False(split.HasSeparator);
        Assert.Empty(split.Command);
    }

    [Fact]
    public void Split_TakesEverythingAfterTheFirstSeparator()
    {
        var split = RunCommand.Split(["run", "dev", "--vault", "v.kdbx", "--", "npm", "start"]);

        Assert.True(split.HasSeparator);
        Assert.Equal(["run", "dev", "--vault", "v.kdbx"], split.Left);
        Assert.Equal(["npm", "start"], split.Command);
    }

    /// <summary>
    /// The whole reason the split is here and not in <see cref="CommandLine"/>: the right-hand
    /// side is exempt from option parsing, so a flag keypaste also understands still belongs to
    /// the child.
    /// </summary>
    [Fact]
    public void Split_LeavesTheChildsOwnFlagsAlone()
    {
        var split = RunCommand.Split(["run", "dev", "--vault", "mine", "--", "mytool", "--vault", "theirs"]);

        Assert.Equal(["mytool", "--vault", "theirs"], split.Command);
    }

    /// <summary>Only the first separator is a boundary; <c>git log -- path</c> has to survive.</summary>
    [Fact]
    public void Split_KeepsALaterSeparatorInTheChildsCommand()
    {
        var split = RunCommand.Split(["run", "dev", "--", "git", "log", "--", "src/"]);

        Assert.Equal(["git", "log", "--", "src/"], split.Command);
    }

    [Fact]
    public void Split_WithNothingAfterTheSeparator_HasSeparatorButNoCommand()
    {
        var split = RunCommand.Split(["run", "dev", "--"]);

        Assert.True(split.HasSeparator);
        Assert.Empty(split.Command);
    }

    // ---- usage -------------------------------------------------------------------------

    [Fact]
    public void Help_GoesToStdout_EvenWithNoSeparator()
    {
        using var harness = new CliHarness();

        var exit = harness.Run("run", "--help");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains("usage: keypaste run", harness.Out, StringComparison.Ordinal);
        Assert.Empty(harness.Err);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("run", "dev")]
    [InlineData("run", "dev", "--")]
    [InlineData("run", "--", "npm")]
    [InlineData("run", "dev", "extra", "--", "npm")]
    public void MalformedInvocations_AreUsageErrors_AndStartNothing(params string[] args)
    {
        using var harness = new CliHarness();

        var exit = harness.Run(args);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.NotEmpty(harness.Err);
        Assert.Empty(harness.Out);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void UnknownProject_ExitsNotFound_AndStartsNothing()
    {
        using var harness = Seeded(("API_KEY", "v"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("run", "absent", "--vault", harness.VaultPath, "--", "npm", "start");

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void WrongMasterPassword_ExitsAuthFailed_AndStartsNothing()
    {
        using var harness = Seeded(("API_KEY", "v"));

        harness.Prompt.Enqueue("not-the-password");
        var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "npm", "start");

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    // ---- injection ---------------------------------------------------------------------

    [Fact]
    public void TheCommandAndItsArgumentsArePassedThroughExactly()
    {
        using var harness = Seeded(("API_KEY", "v"));

        harness.Prompt.Enqueue(Master);
        harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "npm", "run", "build -- --flag");

        var started = Assert.Single(harness.ProcessLauncher.Started);
        Assert.Equal("npm", started.FileName);
        Assert.Equal(["run", "build -- --flag"], started.Arguments);
    }

    [Fact]
    public void TheProjectsVariablesReachTheChild()
    {
        using var harness = Seeded(("API_KEY", "sk_live_x"), ("EMPTY", ""));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node", "server.js");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal("sk_live_x", harness.ProcessLauncher.Environment["API_KEY"]);
        Assert.Equal("", harness.ProcessLauncher.Environment["EMPTY"]);
    }

    /// <summary>
    /// The inherited environment survives — a child that lost <c>HOME</c> or <c>PATH</c> would be
    /// useless — and the vault wins where the two disagree, which is the point of asking for it.
    /// </summary>
    [Fact]
    public void TheParentEnvironmentIsInherited_AndTheVaultWinsOnConflict()
    {
        using var harness = Seeded(("DATABASE_URL", "from-vault"));
        harness.Environment["UNRELATED"] = "from-parent";
        harness.Environment["DATABASE_URL"] = "stale-shell-value";

        harness.Prompt.Enqueue(Master);
        harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node");

        Assert.Equal("from-parent", harness.ProcessLauncher.Environment["UNRELATED"]);
        Assert.Equal("from-vault", harness.ProcessLauncher.Environment["DATABASE_URL"]);
    }

    [Fact]
    public void AProjectThatDefinesPath_IsAllowed_AndWarnedAbout()
    {
        using var harness = Seeded(("PATH", "/opt/toolchain/bin"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains("defines PATH", harness.Err, StringComparison.Ordinal);
        Assert.Single(harness.ProcessLauncher.Started);
    }

    // ---- fail closed -------------------------------------------------------------------

    /// <summary>
    /// Skipping these with a warning was the alternative. A child booted with a silently
    /// incomplete environment does not fail here — it fails later, elsewhere, as "connected to the
    /// wrong database". Every offending name is listed so one repair pass in KeePassXC is enough.
    /// </summary>
    [Fact]
    public void ANameThatCannotBeExported_StopsTheRun_AndNamesEveryOne()
    {
        using var harness = Seeded(("GOOD", "v"));

        // Written the way KeePassXC would write them: keypaste's own `env set` refuses these.
        using (var vault = Core.Vault.Open(harness.VaultPath, Master))
        {
            vault.AddEntry(new Core.VaultEntry { Title = "not-a-name", Password = "x", GroupPath = "env/dev" });
            vault.AddEntry(new Core.VaultEntry { Title = "also bad", Password = "y", GroupPath = "env/dev" });
            vault.Save();
        }

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node");

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("not-a-name", harness.Err, StringComparison.Ordinal);
        Assert.Contains("also bad", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    /// <summary>
    /// The second half of DECISIONS.md O-0009, answered here. Two such names are two variables on
    /// Linux and one on Windows, so there is no injection that means the same thing everywhere.
    /// The check is deliberately <em>not</em> platform-conditional: a vault that runs on Linux and
    /// refuses on Windows is a failure a teammate cannot reproduce.
    /// </summary>
    [Fact]
    public void TwoNamesDifferingOnlyInCase_StopTheRun_OnEveryPlatform()
    {
        using var harness = Seeded(("TOKEN", "v"));

        using (var vault = Core.Vault.Open(harness.VaultPath, Master))
        {
            vault.AddEntry(new Core.VaultEntry { Title = "Token", Password = "other", GroupPath = "env/dev" });
            vault.Save();
        }

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node");

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("only in case", harness.Err, StringComparison.Ordinal);
        Assert.Contains("TOKEN", harness.Err, StringComparison.Ordinal);
        Assert.Contains("Token", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    // ---- exit codes --------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(130)]
    public void TheChildsExitCodeBecomesKeypastes(int code)
    {
        using var harness = Seeded(("A", "v"));
        harness.ProcessLauncher.Result = new ChildResult(ChildOutcome.Exited, code, string.Empty);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(code, harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node"));
    }

    /// <summary>
    /// One fact rather than a theory: <see cref="ChildOutcome"/> is internal, and a public test
    /// method cannot take it as a parameter without leaking the type out of the CLI.
    /// </summary>
    [Fact]
    public void AChildThatNeverStarted_ReportsTheShellsCode()
    {
        (ChildOutcome Outcome, int Expected)[] cases =
        [
            (ChildOutcome.NotFound, CliApp.ExitCommandNotFound),
            (ChildOutcome.NotExecutable, CliApp.ExitCommandNotExecutable),
            (ChildOutcome.Failed, CliApp.ExitInternalError),
        ];

        foreach (var (outcome, expected) in cases)
        {
            using var harness = Seeded(("A", "v"));
            harness.ProcessLauncher.Result = new ChildResult(outcome, 0, "could not start it");

            harness.Prompt.Enqueue(Master);
            var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "nope");

            Assert.Equal(expected, exit);
            Assert.Contains("keypaste run:", harness.Err, StringComparison.Ordinal);
        }
    }

    // ---- lifetime ----------------------------------------------------------------------

    /// <summary>
    /// A child can run for hours, and holding a decrypted vault open for all of it is not
    /// something a credential tool gets to do. The master password buffer being zeroed is
    /// observable proof the session closed, since <c>VaultSession.Open</c> disposes it on return.
    /// </summary>
    [Fact]
    public void TheVaultIsClosedBeforeTheChildStarts()
    {
        using var harness = Seeded(("A", "v"));

        var zeroedWhenTheChildStarted = false;
        harness.ProcessLauncher.OnRun = () =>
            zeroedWhenTheChildStarted = harness.Prompt.IssuedSecrets.TrueForAll(s => s.IsZeroed);

        harness.Prompt.Enqueue(Master);
        harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node");

        Assert.Single(harness.ProcessLauncher.Started);
        Assert.True(zeroedWhenTheChildStarted, "the master password was still live when the child started");
    }
}
