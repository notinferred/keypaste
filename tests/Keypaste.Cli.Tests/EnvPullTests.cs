using System.Text;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste env pull</c> at the command level. Every test here opens a vault, which costs a
/// key derivation, so the grammar itself is covered in <c>DotEnvTests</c> and these assert only
/// what the command adds: the plan, the confirmation, fail-closed, and the deletion.
/// </summary>
public sealed class EnvPullTests
{
    internal const string Master = "pull-master-pw";

    private static string WriteEnvFile(CliHarness harness, string contents, string name = ".env")
    {
        var path = Path.Combine(harness.Directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static CliHarness Seeded()
    {
        var harness = new CliHarness();
        harness.SeedVault(Master);

        // SeedVault clears the output streams but not the prompt log, and two tests here assert
        // that nothing was asked at all.
        harness.Prompt.PromptsSeen.Clear();
        return harness;
    }

    /// <summary>Reads the vault back through the core, so the assertion does not trust the CLI.</summary>
    private static IReadOnlyList<EnvVariable> Stored(CliHarness harness, string project)
    {
        using var vault = Vault.Open(harness.VaultPath, Master);
        return new EnvStore(vault).Read(project);
    }

    [Fact]
    public void Pull_ImportsEveryVariable_AndSummarisesByNameOnly()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness,
            "# comment\nexport API_KEY=sk_live_secret\nPORT=8080\nMESSAGE=\"a\\nb\"\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);

        Assert.Equal(
            [new EnvVariable("API_KEY", "sk_live_secret"), new EnvVariable("MESSAGE", "a\nb"), new EnvVariable("PORT", "8080")],
            Stored(harness, "billing"));

        // Names are progress, not data, so they go to stderr and stdout stays empty.
        Assert.Empty(harness.Out);
        Assert.Contains("3 new", harness.Err, StringComparison.Ordinal);
        Assert.Contains("API_KEY", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_secret", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fail-closed test. A partial import whose original was then deleted is unrecoverable,
    /// so one bad line has to leave the vault exactly as it was.
    /// </summary>
    [Fact]
    public void Pull_WithABadLine_WritesNothing_AndReportsEveryProblem()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "GOOD=1\nFOO-BAR=2\nnoequals\n=3\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("3 problems", harness.Err, StringComparison.Ordinal);
        Assert.Contains("line 2", harness.Err, StringComparison.Ordinal);
        Assert.Contains("line 3", harness.Err, StringComparison.Ordinal);
        Assert.Contains("line 4", harness.Err, StringComparison.Ordinal);
        Assert.Contains("Nothing was imported", harness.Err, StringComparison.Ordinal);

        // Not merely "GOOD is absent" — the group itself must never have been created.
        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.False(new EnvStore(vault).ProjectExists("billing"));
    }

    /// <summary>
    /// Re-running a pull after editing one line is the normal case, so the plan has to
    /// distinguish the three outcomes and leave the untouched ones alone. Rewriting an identical
    /// value would spend a KDBX history slot (D-0014 caps them at ten) for no change.
    /// </summary>
    [Fact]
    public void Pull_OverExistingVariables_PrintsThePlan_AndSkipsUnchanged()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master, "keep-me");
        harness.Run("env", "set", "billing", "SAME", "--vault", harness.VaultPath);
        harness.Prompt.Enqueue(Master, "old");
        harness.Run("env", "set", "billing", "CHANGED", "--vault", harness.VaultPath);
        harness.Stderr.GetStringBuilder().Clear();

        var path = WriteEnvFile(harness, "SAME=keep-me\nCHANGED=new\nFRESH=1\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("1 new, 1 updated, 1 unchanged", harness.Err, StringComparison.Ordinal);
        Assert.Contains("history", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        var stored = new EnvStore(vault).Read("billing").ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        Assert.Equal("new", stored["CHANGED"]);
        Assert.Equal("keep-me", stored["SAME"]);
        Assert.Equal("1", stored["FRESH"]);

        // That the unchanged entry was not rewritten is the other half of this, and it is asserted
        // where it can be seen: EnvStoreTests.Set_WithTheValueItAlreadyHas_StillCostsAHistoryItem
        // shows why skipping matters, and Vault.CountHistoryItems is internal to the core.
    }

    [Fact]
    public void Pull_DecliningTheConfirmation_WritesNothing()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue(Master, "n");
        var exit = harness.Run("env", "pull", "billing", path, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("Cancelled.", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.False(new EnvStore(vault).ProjectExists("billing"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Pull_WithoutYes_AndRedirectedStdin_IsAUsageError_AndReadsNothing()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        var exit = harness.Run("env", "pull", "billing", path, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--yes is required", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Pull_DeleteSource_RemovesTheFile_AndSaysWhatDeletingDoesNotDo()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--delete-source", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.False(File.Exists(path));
        Assert.Contains("does not overwrite", harness.Err, StringComparison.Ordinal);
        Assert.Contains("rotate", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The import already succeeded by the time deletion is considered, so a piped run neither
    /// deletes nor fails — it says what it left behind. This differs from <c>rm --yes</c> on
    /// purpose: there the deletion is the command.
    /// </summary>
    [Fact]
    public void Pull_ByDefault_LeavesTheFileInPlace_AndSaysSo()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.True(File.Exists(path));
        Assert.Contains("--delete-source", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// A worktree and a submodule carry a <c>.git</c> file rather than a directory, and those are
    /// exactly the setups where the history belongs to somebody else.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pull_InsideAGitRepository_WarnsThatDeletingDoesNotTouchHistory(bool asDirectory)
    {
        using var harness = Seeded();

        var marker = Path.Combine(harness.Directory, ".git");
        if (asDirectory)
        {
            Directory.CreateDirectory(marker);
        }
        else
        {
            File.WriteAllText(marker, "gitdir: ../elsewhere\n");
        }

        var nested = Directory.CreateDirectory(Path.Combine(harness.Directory, "app", "config")).FullName;
        var path = Path.Combine(nested, ".env");
        File.WriteAllText(path, "A=1\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("git history", harness.Err, StringComparison.Ordinal);
        Assert.Contains("every clone", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// A typo in the path must not cost a password entry and a key derivation to discover, so
    /// everything about the file is settled before the vault is opened.
    /// </summary>
    [Fact]
    public void Pull_MissingFile_ExitsNotFound_WithoutAskingForTheMasterPassword()
    {
        using var harness = Seeded();

        var exit = harness.Run(
            "env", "pull", "billing", Path.Combine(harness.Directory, "absent.env"),
            "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Pull_WithNoPathOperand_LooksForDotEnv()
    {
        using var harness = Seeded();

        // No .env exists in the test host's working directory, and the message has to name what
        // it looked for. Asserting this way avoids Directory.SetCurrentDirectory, which is
        // process-global and unsafe while other tests run in parallel.
        var exit = harness.Run("env", "pull", "billing", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains(".env", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Pull_RefusesTwoNamesDifferingOnlyInCase_BeforeWritingAnything()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "TOKEN=a\nToken=b\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("only in case", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.False(new EnvStore(vault).ProjectExists("billing"));
    }

    [Fact]
    public void Pull_OfAFileThatChangesNothing_SucceedsAndSaysSo()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(Master, "v");
        harness.Run("env", "set", "billing", "A", "--vault", harness.VaultPath);
        harness.Stderr.GetStringBuilder().Clear();

        var path = WriteEnvFile(harness, "A=v\n");
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("already matches", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// PowerShell 5.1 writes UTF-16 from <c>&gt;</c> and <c>Set-Content</c>, so this is not an
    /// exotic file — it is what a Windows user's redirect produces.
    /// </summary>
    [Fact]
    public void Pull_ReadsAUtf16File()
    {
        using var harness = Seeded();
        var path = Path.Combine(harness.Directory, "utf16.env");
        File.WriteAllText(path, "A=café\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal("café", Assert.Single(Stored(harness, "billing")).Value);
    }
}
