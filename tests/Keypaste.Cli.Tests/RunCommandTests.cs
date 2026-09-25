using Keypaste.Cli.Commands;
using Keypaste.Core.Launch;
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

    [Fact]
    public void AnExpiredEntry_StopsTheRun_NamingItAndWhy_WithoutAnyValue()
    {
        using var harness = Seeded(("GOOD", "good-value-7c1e"), ("OLD", "old-value-7c1e"));

        using (var vault = Core.Vault.Open(harness.VaultPath, Master))
        {
            vault.SetExpiryUnchecked(new Core.EntryName("env/dev", "OLD"), new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
            vault.AddEntry(new Core.VaultEntry { Title = "BAD-NAME", Password = "bad-value-7c1e", GroupPath = "env/dev" });
            vault.Save();
        }

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("run", "dev", "--vault", harness.VaultPath, "--", "node");

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("OLD expired 2020-01-02 03:04:05Z", harness.Err, StringComparison.Ordinal);
        Assert.Contains("BAD-NAME is not a valid environment variable name", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("value-7c1e", harness.Err + harness.Out, StringComparison.Ordinal);
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

    // ---- profiles and reference files -------------------------------------------------

    [Fact]
    public void Run_WithProfile_InjectsThatProfile()
    {
        using var harness = Profiled();

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "-p", "staging", "acme-api", "--vault", harness.VaultPath, "--", "node"));

        Assert.Equal("staging-db", harness.ProcessLauncher.Environment["DATABASE_URL"]);
        Assert.False(harness.ProcessLauncher.Environment.ContainsKey("STRIPE_KEY"));
        Assert.Empty(harness.Err);

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitNotFound, harness.Run("run", "--profile", "qa", "acme-api", "--vault", harness.VaultPath, "--", "node"));
        Assert.Contains("'acme-api' has no 'qa' profile", harness.Err, StringComparison.Ordinal);

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "-p", "QA", "acme-api", "--vault", harness.VaultPath, "--", "node"));
        Assert.Single(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void Run_InfersTheProjectFromProjectsJson()
    {
        using var harness = Profiled();
        harness.WorkingDirectory = EnvVerbTests.MapProject(harness, "acme-api");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "--vault", harness.VaultPath, "--", "node"));
        Assert.Equal("dev-db", harness.ProcessLauncher.Environment["DATABASE_URL"]);

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "-p", "staging", "--vault", harness.VaultPath, "--", "node"));
        Assert.Equal("staging-db", harness.ProcessLauncher.Environment["DATABASE_URL"]);
        Assert.DoesNotContain("resolving", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_EnvFile_ResolvesReferences()
    {
        using var harness = Profiled();
        var file = Write(harness, "refs.env", "DB=kp://acme-api/staging/DATABASE_URL\nSTRIPE_SECRET_KEY=kp://acme-api/dev/STRIPE_KEY\nGH=kp:///work/github#username\n");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "--env-file", file, "--vault", harness.VaultPath, "--", "node"));

        var environment = harness.ProcessLauncher.Environment;
        Assert.Equal("staging-db", environment["DB"]);
        Assert.Equal("sk_dev", environment["STRIPE_SECRET_KEY"]);
        Assert.Equal("octocat", environment["GH"]);
        Assert.False(environment.ContainsKey("DATABASE_URL"));
    }

    [Fact]
    public void Run_EnvFileOfLiteralsOnly_IsRefusedWithoutShowingAValueOrAskingForThePassword()
    {
        using var harness = Profiled();
        harness.Prompt.PromptsSeen.Clear();
        var file = Write(harness, "plain.env", "STRIPE_SECRET=sk_live_51Habc123\nDB_PASSWORD=hunter2\n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "--env-file", file, "--vault", harness.VaultPath, "--", "node"));

        Assert.Contains("names no kp:// reference", harness.Err, StringComparison.Ordinal);
        foreach (var value in new[] { "sk_live_51Habc123", "hunter2" })
        {
            Assert.DoesNotContain(value, harness.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(value, harness.Out, StringComparison.Ordinal);
        }

        Assert.Empty(harness.ProcessLauncher.Started);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Theory]
    [InlineData("A=kp://acme-api/dev/STRIPE_KEY\nA=kp://acme-api/staging/DATABASE_URL\n", "line 2: 'A' is set more than once")]
    [InlineData("A=kp://acme-api/dev/STRIPE_KEY\nB=kp://\n", "line 2: 'B' is not a usable reference")]
    public void Run_EnvFileProblem_NamesItsLineOnce(string content, string expected)
    {
        using var harness = Profiled();
        var file = Write(harness, "refs.env", content);

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "--env-file", file, "--vault", harness.VaultPath, "--", "node"));

        Assert.Contains("  " + expected, harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("line 2: line 2:", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ProfileFlag_RewritesTheFilesProfiles()
    {
        using var harness = Profiled();
        var file = Write(harness, "refs.env", "DB=kp://acme-api/staging/DATABASE_URL\nGH=kp:///work/github#username\n");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "--env-file", file, "-p", "dev", "--vault", harness.VaultPath, "--", "node"));

        Assert.Equal("dev-db", harness.ProcessLauncher.Environment["DB"]);
        Assert.Equal("octocat", harness.ProcessLauncher.Environment["GH"]);
        Assert.Contains("profile dev", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AutoDiscoveredFileWithEntryReferences_IsRefused()
    {
        using var harness = Profiled();
        harness.WorkingDirectory = EnvVerbTests.MapProject(harness, "acme-api");
        Write(harness, Core.EnvReferenceFile.FileName, "DB=kp://acme-api/dev/DATABASE_URL\nGH=kp:///work/github\n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "--vault", harness.VaultPath, "--", "node"));

        Assert.Contains(
            "keypaste run: .env.keypaste names vault entries, not this directory's project acme-api; pass --env-file .env.keypaste to use it",
            harness.Err,
            StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void Run_AutoDiscoveredFileForAnotherProject_IsRefused()
    {
        using var harness = Profiled();
        harness.WorkingDirectory = EnvVerbTests.MapProject(harness, "acme-api");
        Write(harness, Core.EnvReferenceFile.FileName, "DB=kp://billing/dev/DATABASE_URL\nAWS_ENDPOINT_URL=http://attacker.invalid\n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "--vault", harness.VaultPath, "--", "node"));
        Assert.Contains(".env.keypaste names project 'billing', not this directory's project acme-api", harness.Err, StringComparison.Ordinal);

        Write(harness, Core.EnvReferenceFile.FileName, "A=kp://acme-api/dev/DATABASE_URL\nB=kp://billing/dev/DATABASE_URL\n");
        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "--vault", harness.VaultPath, "--", "node"));
        Assert.Contains(".env.keypaste names several projects (acme-api, billing)", harness.Err, StringComparison.Ordinal);

        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void Run_FileWithoutAProjectMapping_IsNotAutoUsed()
    {
        using var harness = Profiled();
        harness.Environment[Core.Audit.KeypasteHome.EnvironmentVariable] = Path.Combine(harness.Directory, "home");
        harness.WorkingDirectory = harness.Directory;
        Write(harness, Core.EnvReferenceFile.FileName, "DB=kp://acme-api/staging/DATABASE_URL\n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "-p", "staging", "--vault", harness.VaultPath, "--", "env"));

        Assert.Equal(
            "keypaste run: this directory is not mapped to a project; pass --env-file .env.keypaste or name a project",
            harness.Err.Trim());
        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void Run_ReferenceMode_AlwaysSaysWhichFileProjectAndProfile()
    {
        using var harness = Profiled();
        harness.WorkingDirectory = EnvVerbTests.MapProject(harness, "acme-api");
        Write(harness, Core.EnvReferenceFile.FileName, "DB=kp://acme-api/staging/DATABASE_URL\nNODE_OPTIONS=--max-old-space-size=4096\n");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "--vault", harness.VaultPath, "--", "node"));

        var said = harness.Err.ReplaceLineEndings("\n");
        Assert.StartsWith("keypaste run: resolving .env.keypaste → project acme-api profile staging\n", said, StringComparison.Ordinal);
        Assert.Contains("keypaste run: literal NODE_OPTIONS=--max-old-space-size=4096\n", said, StringComparison.Ordinal);
        Assert.Equal("staging-db", harness.ProcessLauncher.Environment["DB"]);
        Assert.Equal("--max-old-space-size=4096", harness.ProcessLauncher.Environment["NODE_OPTIONS"]);
    }

    [Fact]
    public void Run_Summary_ListsEveryLiteral()
    {
        using var harness = Profiled();
        harness.ConsoleStyle.Terminal = true;
        var file = Write(
            harness,
            "refs.env",
            "A=kp://acme-api/dev/DATABASE_URL\nB=kp://acme-api/dev/STRIPE_KEY\nHTTPS_PROXY=http://proxy.internal:3128\n" +
            "C=kp://acme-api/dev/DATABASE_URL\nD=kp:///work/github#username\nE=kp://acme-api/staging/DATABASE_URL\nNO_PROXY=localhost\n");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "--env-file", file, "--vault", harness.VaultPath, "--", "node"));

        Assert.Equal(
            [
                $"keypaste run: resolving {file} → project acme-api, vault entries profile mixed",
                "keypaste run: literal HTTPS_PROXY=http://proxy.internal:3128",
                "keypaste run: literal NO_PROXY=localhost",
                $"  resolving {file}  profile mixed",
                "  ✓ A                   kp://acme-api/dev",
                "  ✓ B                   kp://acme-api/dev",
                "  = HTTPS_PROXY=http://proxy.internal:3128",
                "  ✓ C                   kp://acme-api/dev",
                "  = NO_PROXY=localhost",
                "  + 2 more",
                "  7 injected · nothing written to disk",
            ],
            harness.Err.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("dev-db", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_OnATerminal_ListsEveryLiteralBeforeThePasswordIsAskedFor()
    {
        using var harness = Profiled();
        harness.ConsoleStyle.Terminal = true;
        var file = Write(harness, "refs.env", "DB=kp://acme-api/dev/DATABASE_URL\nHTTPS_PROXY=http://proxy.internal:3128\n");
        string? shownFirst = null;
        harness.Prompt.OnPrompt = _ => shownFirst ??= harness.Err;

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "--env-file", file, "--vault", harness.VaultPath, "--", "node"));

        Assert.Contains("keypaste run: literal HTTPS_PROXY=http://proxy.internal:3128", shownFirst, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NODE_OPTIONS=\"{0}--require ./x.js\"")]
    [InlineData("NODE_OPTIONS=\"--max-old-space-size=4096\\n--require ./x.js\"")]
    [InlineData("NODE_OPTIONS=\"--max-old-space-size=4096 \"")]
    public void Run_ALiteralThatCannotBeShownAsWritten_IsRefusedUnasked(string literal)
    {
        using var harness = Profiled();
        harness.WorkingDirectory = EnvVerbTests.MapProject(harness, "acme-api");
        Write(harness, Core.EnvReferenceFile.FileName, $"DB=kp://acme-api/dev/DATABASE_URL\n{string.Format(literal, new string(' ', 1100))}\n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "--vault", harness.VaultPath, "--", "node"));

        Assert.Equal(
            "keypaste run: .env.keypaste line 2: the value of NODE_OPTIONS is too long or holds characters that cannot be shown on one line",
            harness.Err.Trim());
        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessLauncher.Started);
    }

    [Fact]
    public void Run_EnvFileAndProject_IsAUsageError()
    {
        using var harness = Profiled();
        var file = Write(harness, "refs.env", "DB=kp://acme-api/dev/DATABASE_URL\n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("run", "acme-api", "--env-file", file, "--vault", harness.VaultPath, "--", "node"));

        Assert.Contains("takes no project", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Run_PrintsTheSummary_OnlyOnATerminal()
    {
        using var harness = Profiled();

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "acme-api", "--vault", harness.VaultPath, "--", "node"));
        Assert.Empty(harness.Err);

        harness.ConsoleStyle.Terminal = true;
        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("run", "acme-api", "--vault", harness.VaultPath, "--", "node"));

        Assert.Equal(
            [
                "  resolving acme-api  profile dev",
                "  ✓ DATABASE_URL        kp://acme-api/dev",
                "  ✓ STRIPE_KEY          kp://acme-api/dev",
                "  2 injected · nothing written to disk",
            ],
            harness.Err.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>acme-api: dev holds DATABASE_URL=dev-db and STRIPE_KEY=sk_dev, staging DATABASE_URL=staging-db; work/github's username is octocat.</summary>
    private static CliHarness Profiled()
    {
        var harness = new CliHarness();
        harness.SeedVault(Master);
        harness.WorkingDirectory = harness.Directory;
        harness.Environment[Core.Audit.KeypasteHome.EnvironmentVariable] = Path.Combine(harness.Directory, "home");

        using (var vault = Core.Vault.Open(harness.VaultPath, Master))
        {
            var store = new Core.EnvStore(vault);
            store.TrySet("acme-api", "DATABASE_URL", "dev-db", out _);
            store.TrySet("acme-api", "STRIPE_KEY", "sk_dev", out _);
            store.TrySet("acme-api", "staging", "DATABASE_URL", "staging-db", out _);
            vault.AddEntry(new Core.VaultEntry { GroupPath = "work", Title = "github", Username = "octocat", Password = "gh-password" });
            vault.Save();
        }

        harness.Prompt.PromptsSeen.Clear();
        return harness;
    }

    private static string Write(CliHarness harness, string name, string text)
    {
        var path = Path.Combine(harness.WorkingDirectory, name);
        File.WriteAllText(path, text);
        return path;
    }
}
