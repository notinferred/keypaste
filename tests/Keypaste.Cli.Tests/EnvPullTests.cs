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
    /// <summary>
    /// The seconds a person spends answering a prompt are seconds their editor can save in, and
    /// keypaste offered to delete the file at the end of them without ever looking again
    /// (docs/STEPS.md F.1c). Each of these plants the second writer at a different prompt.
    /// </summary>
    /// <remarks>
    /// The refusal comes before the deletion question rather than after it, so nobody is asked
    /// whether to delete bytes keypaste did not import. Nothing was requested and nothing was lost,
    /// so the run still succeeds — the exit code is about the deletion, and there was none.
    /// </remarks>
    [Theory]
    [InlineData("Master password")]
    [InlineData("Import")]
    public void Pull_WhenTheSourceChangesDuringAPrompt_KeepsIt_AndNeverOffersToDeleteIt(string atPrompt)
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.Interactive = true;
        harness.Prompt.OnPrompt = ChangeTheFile(harness, path, atPrompt, "A=1\nB=2\n");
        harness.Prompt.Enqueue(Master, "y", "y");

        var exit = harness.Run("env", "pull", "billing", path, "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);

        // The bytes the second writer put there, byte for byte. A=1 was imported; B=2 never was.
        Assert.Equal("A=1\nB=2\n", File.ReadAllText(path));
        Assert.Contains("changed after keypaste read it", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete '", string.Join("\n", harness.Prompt.PromptsSeen), StringComparison.Ordinal);
        Assert.DoesNotContain("Deleted ", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The boundary the other tests cannot reach: the check passed, the person said yes, and the
    /// file changed in between. Here the deletion <em>was</em> asked for and did not happen, so the
    /// run exits nonzero — D-0015's rule that the half which failed is the half that left plaintext
    /// on disk.
    /// </summary>
    [Fact]
    public void Pull_WhenTheSourceChangesDuringTheDeletionPrompt_KeepsIt_AndFails()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.Interactive = true;
        harness.Prompt.OnPrompt = ChangeTheFile(harness, path, "Delete '", "A=1\nB=2\n");
        harness.Prompt.Enqueue(Master, "y", "y");

        var exit = harness.Run("env", "pull", "billing", path, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Equal("A=1\nB=2\n", File.ReadAllText(path));
        Assert.Contains("changed", harness.Err, StringComparison.Ordinal);
        Assert.Contains("The file still contains the values.", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--delete-source` never prompts, so its window is short — but a key derivation is hundreds
    /// of milliseconds and the check has to exist on this path too.
    /// </summary>
    [Fact]
    public void Pull_DeleteSource_WhenTheSourceChanged_KeepsIt_AndFails()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.OnPrompt = ChangeTheFile(harness, path, "Master password", "A=1\nB=2\n");
        harness.Prompt.Enqueue(Master);

        var exit = harness.Run(
            "env", "pull", "billing", path, "--yes", "--delete-source", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Equal("A=1\nB=2\n", File.ReadAllText(path));
        Assert.Contains("The file still contains the values.", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Same content, different file. The digest cannot see this one and the path rule can.
    /// </summary>
    [Fact]
    public void Pull_WhenTheSourceIsReplacedByASymlinkElsewhere_LeavesTheOtherFileAlone()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");
        var elsewhere = WriteEnvFile(harness, "A=1\n", "elsewhere.env");

        // Probed before the run, so a machine that cannot make links skips here rather than
        // throwing out of a prompt with a vault open.
        RequireSymbolicLinks(harness, elsewhere);

        harness.Prompt.OnPrompt = prompt =>
        {
            if (!prompt.StartsWith("Master password", StringComparison.Ordinal))
            {
                return;
            }

            harness.Prompt.OnPrompt = null;
            File.Delete(path);
            File.CreateSymbolicLink(path, elsewhere);
        };
        harness.Prompt.Enqueue(Master);

        var exit = harness.Run(
            "env", "pull", "billing", path, "--yes", "--delete-source", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitInternalError, exit);
        Assert.True(File.Exists(elsewhere));
        Assert.Equal("A=1\n", File.ReadAllText(elsewhere));
    }

    /// <summary>
    /// `File.Delete` does not throw for a file that is not there, so the old code reported a
    /// deletion it had not performed.
    /// </summary>
    [Fact]
    public void Pull_WhenSomethingElseRemovedTheSource_SaysSo_RatherThanClaimingItDeletedIt()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.OnPrompt = prompt =>
        {
            if (prompt.StartsWith("Master password", StringComparison.Ordinal))
            {
                harness.Prompt.OnPrompt = null;
                File.Delete(path);
            }
        };
        harness.Prompt.Enqueue(Master);

        var exit = harness.Run(
            "env", "pull", "billing", path, "--yes", "--delete-source", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.DoesNotContain("Deleted ", harness.Err, StringComparison.Ordinal);
        Assert.Contains("already gone", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The positive control for the whole set: an untouched file is still deleted on confirmation,
    /// or the tests above would pass just as well against a command that never deletes anything.
    /// </summary>
    [Fact]
    public void Pull_WithAnUnchangedSource_StillDeletesItOnConfirmation()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue(Master, "y", "y");

        var exit = harness.Run("env", "pull", "billing", path, "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.False(File.Exists(path));
        Assert.Contains($"Deleted {path}.", harness.Err, StringComparison.Ordinal);

        // And nothing was left beside it under a temporary name.
        Assert.Empty(Directory.GetFiles(harness.Directory, "*.keypaste-*"));
    }

    /// <summary>
    /// `--keep` is the flag the guide tells people to import with, and it must not depend on any
    /// of this: the file is kept whether or not it changed, and it is never even inspected.
    /// </summary>
    [Fact]
    public void Pull_WithKeep_LeavesAChangedSourceAlone_WithoutComplaining()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        harness.Prompt.OnPrompt = ChangeTheFile(harness, path, "Master password", "A=1\nB=2\n");
        harness.Prompt.Enqueue(Master);

        var exit = harness.Run("env", "pull", "billing", path, "--yes", "--keep", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal("A=1\nB=2\n", File.ReadAllText(path));
        Assert.DoesNotContain("changed after keypaste read it", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// A vault that another process wrote to fails the save, and a failed import must not reach the
    /// deletion at all — the values are in neither place.
    /// </summary>
    [Fact]
    public void Pull_WhenTheVaultSaveFails_NeverTouchesTheSource()
    {
        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");

        // The import confirmation is the prompt that sits between Vault.Open and vault.Save, so it
        // is the only one at which a second writer makes the save refuse.
        harness.Prompt.Interactive = true;
        harness.Prompt.OnPrompt = prompt =>
        {
            if (!prompt.StartsWith("Import", StringComparison.Ordinal))
            {
                return;
            }

            harness.Prompt.OnPrompt = null;
            using var elsewhere = Vault.Open(harness.VaultPath, Master);
            elsewhere.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "second" });
            elsewhere.Save();
        };
        harness.Prompt.Enqueue(Master, "y", "y");

        var exit = harness.Run("env", "pull", "billing", path, "--delete-source", "--vault", harness.VaultPath);

        Assert.NotEqual(CliApp.ExitSuccess, exit);
        Assert.True(File.Exists(path));
        Assert.Equal("A=1\n", File.ReadAllText(path));
    }

    /// <summary>
    /// The import succeeded and the file could not be removed, so it is sitting under a temporary
    /// name beside where it was. Naming it is the whole of the obligation: it is plaintext, it is
    /// the user's, and a run that quietly left it there would be worse than one that never tried.
    /// </summary>
    /// <remarks>
    /// Windows only. The read-only attribute is what makes <c>File.Delete</c> refuse there; on
    /// Linux and macOS the directory governs deletion and the file goes without complaint.
    /// </remarks>
    [Fact]
    public void Pull_WhenTheSourceCannotBeDeleted_NamesWhereItLeftIt_AndFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("on this platform the read-only attribute does not prevent deletion.");
        }

        using var harness = Seeded();
        var path = WriteEnvFile(harness, "A=1\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "pull", "billing", path, "--yes", "--delete-source", "--vault", harness.VaultPath);

        var left = Assert.Single(Directory.GetFiles(harness.Directory, "*.keypaste-*"));
        foreach (var file in Directory.GetFiles(harness.Directory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Equal("A=1\n", File.ReadAllText(left));
        Assert.Contains(left, harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Deleted ", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>Plants a rewrite of the source at the named prompt, once.</summary>
    private static Action<string> ChangeTheFile(
        CliHarness harness,
        string path,
        string atPrompt,
        string contents) =>
        prompt =>
        {
            if (!prompt.StartsWith(atPrompt, StringComparison.Ordinal))
            {
                return;
            }

            harness.Prompt.OnPrompt = null;
            File.WriteAllText(path, contents);
        };

    private static void RequireSymbolicLinks(CliHarness harness, string target)
    {
        var probe = Path.Combine(harness.Directory, "probe.link");
        try
        {
            File.CreateSymbolicLink(probe, target);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create symbolic links: {ex.Message}");
        }
    }

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
