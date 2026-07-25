using Keypaste.Cli.Clipboard;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// End-to-end coverage of the five verbs, in-process against fakes.
/// </summary>
/// <remarks>
/// Each vault operation costs an Argon2 derivation (~100ms), so these lean on a handful of
/// seeded vaults rather than one per assertion. If this file ever gets slow, cut the number of
/// vault-touching cases — never the KDF parameters, which are the thing under test elsewhere.
/// </remarks>
public sealed class VerbTests
{
    internal const string Master = "correct horse battery staple";

    [Fact]
    public void Init_CreatesAVaultCoreCanReopen()
    {
        using var harness = new CliHarness();
        harness.Prompt.Enqueue(Master, Master);

        var exit = harness.Run("init", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.True(File.Exists(harness.VaultPath));
        Assert.Empty(harness.Out);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Empty(vault.ReadEntries());
    }

    [Fact]
    public void Init_MismatchedPasswords_WritesNoVault()
    {
        using var harness = new CliHarness();
        harness.Prompt.Enqueue(Master, "something else");

        var exit = harness.Run("init", harness.VaultPath);

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.False(File.Exists(harness.VaultPath));
        Assert.Contains("do not match", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_RefusesToOverwriteAnExistingVault()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);

        harness.Prompt.Enqueue(Master, Master);
        var exit = harness.Run("init", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("already exists", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_EmptyPassword_IsRejected()
    {
        using var harness = new CliHarness();
        harness.Prompt.Enqueue(string.Empty, string.Empty);

        var exit = harness.Run("init", harness.VaultPath);

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.False(File.Exists(harness.VaultPath));
    }

    [Fact]
    public void Add_StoresEveryFieldVerbatim_IncludingMultiLineNotesAndNonAscii()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);

        const string notes = "first notes line\nsecond line: , ; = \" ' punctuation";
        harness.Prompt.Enqueue(Master, "s3cret");

        var exit = harness.Run(
            "add", "compat/ascii",
            "--vault", harness.VaultPath,
            "--username", "ünïcode-user",
            "--url", "https://example.invalid/keypaste",
            "--notes", notes);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Empty(harness.Out);

        using var vault = Vault.Open(harness.VaultPath, Master);
        var entry = vault.Find("compat/ascii");

        Assert.NotNull(entry);
        Assert.Equal("s3cret", entry.Password, StringComparer.Ordinal);
        Assert.Equal("ünïcode-user", entry.Username, StringComparer.Ordinal);
        Assert.Equal(notes, entry.Notes, StringComparer.Ordinal);
        Assert.Equal("compat", entry.GroupPath, StringComparer.Ordinal);
    }

    [Fact]
    public void Add_DuplicateEntry_IsRejected()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "one"));

        harness.Prompt.Enqueue(Master, "two");
        var exit = harness.Run("add", "solo", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("already exists", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_GroupInPathAndFlag_IsAUsageError()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);

        var exit = harness.Run("add", "a/b", "--group", "c", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
    }

    [Fact]
    public void Get_WithShow_WritesOnlyThePasswordToStdout()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--show");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal("solo-secret", harness.Out.TrimEnd(), StringComparer.Ordinal);
    }

    /// <summary>
    /// The core promise of <c>get</c>: without <c>--show</c> the secret reaches the clipboard and
    /// never stdout, because stdout ends up in shell history, scrollback and CI logs.
    /// </summary>
    [Fact]
    public void Get_WithoutShow_CopiesToTheClipboard_AndNeverToStdout()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal(1, harness.Clipboard.SetCount);
        Assert.DoesNotContain("solo-secret", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("solo-secret", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_DefaultTimeout_IsTwentySeconds()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        harness.Run("get", "solo", "--vault", harness.VaultPath);

        Assert.Equal(TimeSpan.FromSeconds(20), harness.ClearStrategy.RequestedDelay);
        Assert.True(harness.ClearStrategy.Cleared);
    }

    [Fact]
    public void Get_TimeoutZero_DoesNotScheduleAClear()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--timeout", "0");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Null(harness.ClearStrategy.RequestedDelay);
    }

    /// <summary>
    /// If the user copied something else during the wait, keypaste leaves it alone. keepassxc-cli
    /// clears unconditionally and destroys whatever was there; its own GUI does not.
    /// </summary>
    [Fact]
    public void Get_ClipboardChangedSinceTheCopy_IsLeftAlone()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        // Simulate the user copying something else during the twenty-second wait.
        harness.ClearStrategy.DuringWait =
            () => harness.Clipboard.ReplaceExternally("something the user copied");

        harness.Prompt.Enqueue(Master);
        harness.Run("get", "solo", "--vault", harness.VaultPath);

        Assert.False(harness.ClearStrategy.Cleared);
        Assert.Equal(0, harness.Clipboard.ClearCount);
        Assert.Equal("something the user copied", harness.Clipboard.Content, StringComparer.Ordinal);
        Assert.Contains("leaving it alone", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// No clipboard means a loud failure, never a quiet fallback to printing the secret
    /// (CORE.md law 3.7).
    /// </summary>
    [Fact]
    public void Get_NoClipboardTool_FailsAndSuggestsShow_WithoutLeakingTheSecret()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));
        harness.Clipboard.SetStatus = ClipboardStatus.NoTool;

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("--show", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("solo-secret", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("solo-secret", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_UnknownEntry_ExitsNotFound()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "nope", "--vault", harness.VaultPath, "--show");

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Empty(harness.Out);
    }

    [Fact]
    public void Get_WrongMasterPassword_ExitsAuthFailed()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue("not the password");
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--show");

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.Empty(harness.Out);
    }

    [Fact]
    public void Ls_PrintsAnIndentedTree_NamesOnly()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("compat/ascii", "a"), ("compat/nested/deep", "b"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("ls", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);

        var lines = harness.Out.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal(["compat/", "  ascii", "  nested/", "    deep"], lines);
    }

    [Fact]
    public void Ls_Flat_PrintsFullPaths()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("compat/ascii", "a"), ("compat/nested/deep", "b"));

        harness.Prompt.Enqueue(Master);
        harness.Run("ls", "--vault", harness.VaultPath, "--flat");

        var lines = harness.Out.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal(["compat/", "compat/ascii", "compat/nested/", "compat/nested/deep"], lines);
    }

    /// <summary>
    /// Keeps the listing ASCII. This is the test that stops someone "improving" the tree with
    /// box-drawing characters, which would break code pages, CI logs and diffs against
    /// <c>keepassxc-cli ls -R -f</c>.
    /// </summary>
    [Fact]
    public void Ls_UsesNoNonAsciiCharacters()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("compat/ascii", "a"), ("compat/nested/deep", "b"));

        harness.Prompt.Enqueue(Master);
        harness.Run("ls", "--vault", harness.VaultPath);

        Assert.All(harness.Out, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4} in ls output"));
    }

    [Fact]
    public void Ls_NeverPrintsAUsernameOrPassword()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);

        harness.Prompt.Enqueue(Master, "top-secret");
        harness.Run("add", "solo", "--vault", harness.VaultPath, "--username", "the-user");

        harness.Prompt.Enqueue(Master);
        harness.Run("ls", "--vault", harness.VaultPath);

        Assert.DoesNotContain("top-secret", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("the-user", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Rm_WithYes_RemovesTheEntryForGood()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "one"), ("keep", "two"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("rm", "solo", "--vault", harness.VaultPath, "--yes");

        Assert.Equal(CliApp.ExitSuccess, exit);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Null(vault.Find("solo"));
        Assert.NotNull(vault.Find("keep"));
    }

    [Fact]
    public void Rm_UnknownEntry_ExitsNotFound()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "one"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("rm", "nope", "--vault", harness.VaultPath, "--yes");

        Assert.Equal(CliApp.ExitNotFound, exit);
    }

    [Fact]
    public void Rm_AGroupPath_IsRejectedWithAGroupSpecificMessage()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("compat/ascii", "a"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("rm", "compat", "--vault", harness.VaultPath, "--yes");

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("is a group", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// A piped run must not have its confirmation answered by whatever the next line of stdin
    /// happens to be. Deleting a secret is irreversible, so it has to be asked for explicitly.
    /// </summary>
    [Fact]
    public void Rm_WithoutYes_AndRedirectedStdin_IsAUsageError()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "one"));

        harness.Prompt.Interactive = false;
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("rm", "solo", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--yes", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.NotNull(vault.Find("solo"));
    }

    [Fact]
    public void VaultFlag_BeatsTheEnvironmentVariable()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));
        harness.Environment[VaultLocator.EnvironmentVariable] =
            Path.Combine(harness.Directory, "does-not-exist.kdbx");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--show");

        Assert.Equal(CliApp.ExitSuccess, exit);
    }

    [Fact]
    public void EnvironmentVariable_IsUsedWhenTheFlagIsAbsent()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));
        harness.Environment[VaultLocator.EnvironmentVariable] = harness.VaultPath;

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--show");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal("solo-secret", harness.Out.TrimEnd(), StringComparer.Ordinal);
    }

    [Fact]
    public void NoVaultAnywhere_IsAUsageError_NamingBothMechanisms()
    {
        using var harness = new CliHarness();

        var exit = harness.Run("ls");

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--vault", harness.Err, StringComparison.Ordinal);
        Assert.Contains(VaultLocator.EnvironmentVariable, harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingVaultFile_ExitsNotFound()
    {
        using var harness = new CliHarness();

        var exit = harness.Run("ls", "--vault", Path.Combine(harness.Directory, "nope.kdbx"));

        Assert.Equal(CliApp.ExitNotFound, exit);
    }
}
