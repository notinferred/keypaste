using Keypaste.Cli.Clipboard;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// End-to-end coverage of the five verbs, in-process against fakes.
/// </summary>
/// <remarks>
/// Each vault operation derives a key, so these lean on a handful of seeded vaults rather than one per
/// assertion. The vaults take the token cost of D-0469; <c>KeyDerivationCostTests</c> holds the production one.
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
        Assert.Contains("the master password cannot be empty", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// What each refusal says, pinned word for word.
    /// </summary>
    /// <remarks>
    /// The rules moved to <c>VaultCreation</c> so the desktop applies the same ones, and each front
    /// end maps the outcomes to its own words. Substring assertions elsewhere in this file would not
    /// notice the <c>keypaste init:</c> and <c>keypaste:</c> prefixes swapping, and the prefix is the
    /// part a person greps for.
    /// </remarks>
    [Fact]
    public void Init_RefusalsKeepTheirExactWording()
    {
        using (var harness = new CliHarness())
        {
            harness.SeedVault(Master);
            harness.Prompt.Enqueue(Master, Master);
            harness.Run("init", harness.VaultPath);
            Assert.Contains(
                $"keypaste init: '{harness.VaultPath}' already exists",
                harness.Err,
                StringComparison.Ordinal);
        }

        using (var harness = new CliHarness())
        {
            harness.Prompt.Enqueue(string.Empty, string.Empty);
            harness.Run("init", harness.VaultPath);
            Assert.Contains("keypaste: the master password cannot be empty", harness.Err, StringComparison.Ordinal);
        }

        using (var harness = new CliHarness())
        {
            harness.Prompt.Enqueue(Master, "something else");
            harness.Run("init", harness.VaultPath);
            Assert.Contains("keypaste: the passwords do not match", harness.Err, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An empty master password is refused after one prompt, not two.
    /// </summary>
    /// <remarks>
    /// Confirming a password nobody typed is a question with no useful answer. The rule that refuses
    /// it lives in <c>VaultCreation</c>; what this pins is that the verb does not ask twice on the
    /// way to it, which is what it did before the rules moved.
    /// </remarks>
    [Fact]
    public void Init_EmptyPassword_IsNotAskedToConfirm()
    {
        using var harness = new CliHarness();
        harness.Prompt.Enqueue(string.Empty, string.Empty);

        harness.Run("init", harness.VaultPath);

        Assert.Equal(["New master password: "], harness.Prompt.SecretPrompts);
    }

    [Fact]
    public void Init_ANonEmptyPassword_IsAskedToConfirm()
    {
        using var harness = new CliHarness();
        harness.Prompt.Enqueue(Master, Master);

        harness.Run("init", harness.VaultPath);

        Assert.Equal(
            ["New master password: ", "Confirm master password: "],
            harness.Prompt.SecretPrompts);
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

    /// <summary>
    /// Three identities can answer to <c>a/b/c</c>: the title in the root group, <c>b/c</c> in
    /// <c>a</c>, and <c>c</c> in <c>a/b</c>. With the first two present, the third is a different
    /// identity that no existing-entry check would catch — and creating it would deepen a
    /// collision <c>get</c> already refuses. The digest is the assertion that nothing was written:
    /// <see cref="Vault.Save"/> re-randomises salt and nonces, so an unchanged one proves no save
    /// happened rather than that the contents matched.
    /// </summary>
    [Fact]
    public void Add_APathThatAlreadyNamesTwo_IsRefused_AndTheVaultIsNotWritten()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);
        Author(harness, string.Empty, "a/b/c", "root-titled");
        Author(harness, "a", "b/c", "slashed");

        var before = Digest(harness.VaultPath);

        harness.Prompt.Enqueue(Master, "third");
        var exit = harness.Run("add", "a/b/c", "--vault", harness.VaultPath);

        Assert.NotEqual(CliApp.ExitSuccess, exit);
        Assert.Equal(before, Digest(harness.VaultPath), StringComparer.Ordinal);

        // Why it refused matters: "already exists" would be a false statement about an identity
        // no entry has, and it is the answer the old joined-path check gave.
        Assert.Contains("names more than one item", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("already exists", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal(2, vault.ReadEntries().Count);
    }

    /// <summary>
    /// The false refusal F.1e removes. An entry titled <c>b/c</c> in <c>a</c> makes the *path*
    /// <c>a/b/c</c> taken, and that alone used to refuse a genuinely different entry called
    /// <c>c</c> in <c>a/b</c>. KeePassXC would make it, so keypaste does — and says what it costs,
    /// because the path is ambiguous from that moment on.
    /// </summary>
    [Fact]
    public void Add_ADistinctIdentityWhosePathIsTaken_IsAllowed_AndSaysWhatItCosts()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);
        Author(harness, "a", "b/c", "slashed");

        harness.Prompt.Enqueue(Master, "fresh");
        var exit = harness.Run("add", "c", "--group", "a/b", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("names two items", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("fresh", vault.Find(new EntryName("a/b", "c"))?.Password);
        Assert.Equal("slashed", vault.Find(new EntryName("a", "b/c"))?.Password);
    }

    [Fact]
    public void Get_WithReveal_WritesOnlyThePasswordToStdout_AndNothingToTheClipboard()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--reveal");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal("solo-secret" + Environment.NewLine, harness.Out);
        Assert.Equal(0, harness.Clipboard.SetCount);
    }

    /// <summary>
    /// The core promise of <c>get</c>: without <c>--reveal</c> the secret reaches the clipboard and
    /// never stdout, because stdout ends up in shell history, scrollback and CI logs.
    /// </summary>
    [Fact]
    public void Get_WithoutReveal_CopiesToTheClipboard_AndNeverToStdout()
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
    /// (docs/PRODUCT.md law 3.7).
    /// </summary>
    [Fact]
    public void Get_NoClipboardTool_FailsAndSuggestsReveal_WithoutLeakingTheSecret()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));
        harness.Clipboard.SetStatus = ClipboardStatus.NoTool;

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("--reveal", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("solo-secret", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("solo-secret", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_UnknownEntry_ExitsNotFound()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "nope", "--vault", harness.VaultPath, "--reveal");

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Empty(harness.Out);
    }

    /// <summary>
    /// The read half of D-0091. Two entries answer to <c>a/b/c</c>, so serving either would be a
    /// guess — and unlike a guessed removal, a guessed read hands the wrong secret over and says
    /// nothing about having done it. Both ways out of the verb are checked: <c>--reveal</c> must
    /// print no password, and the default must put none on the clipboard.
    /// </summary>
    [Fact]
    public void Get_ACollidingPath_IsRefused_AndReleasesNeitherSecret()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("a/b/c", "nested-secret"));
        Author(harness, "a", "b/c", "slashed-secret");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(
            CliApp.ExitInternalError,
            harness.Run("get", "a/b/c", "--vault", harness.VaultPath, "--reveal"));

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(
            CliApp.ExitInternalError,
            harness.Run("get", "a/b/c", "--vault", harness.VaultPath));

        Assert.Empty(harness.Out);
        Assert.Equal(0, harness.Clipboard.SetCount);
        Assert.DoesNotContain("nested-secret", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("slashed-secret", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The value leaves with its own item's placeholders filled in, on stdout and on the clipboard alike,
    /// for the password and for a custom field (D-0465).
    /// </summary>
    [Fact]
    public void Get_FillsInTheItemsOwnPlaceholders_OnStdoutAndTheClipboard()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);
        PlantDatabase(harness, "db-{USERNAME}-{s:DB_SUFFIX}");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("get", "web/db", "--vault", harness.VaultPath, "--reveal"));
        Assert.Equal("db-dbuser-5d1e-9f2c", harness.Out.TrimEnd(), StringComparer.Ordinal);

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(
            CliApp.ExitSuccess,
            harness.Run("get", "web/db", "--field", "DATABASE_URL", "--vault", harness.VaultPath, "--timeout", "0"));
        Assert.Equal("postgres://dbuser-5d1e:db-dbuser-5d1e-9f2c@db", harness.Clipboard.Content, StringComparer.Ordinal);
    }

    /// <summary>
    /// A value keypaste cannot fill in leaves by neither way, after whatever filled in before it; the
    /// refusal names the field and the placeholder, never a value, and points to what shows it as stored.
    /// </summary>
    [Fact]
    public void Get_AValueThatCannotBeFilledIn_IsRefused_AndPointsToShowReveal()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);
        PlantDatabase(harness, "{USERNAME}-{S:Recovery codes}");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitInternalError, harness.Run("get", "web/db", "--vault", harness.VaultPath, "--reveal"));

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitInternalError, harness.Run("get", "web/db", "--vault", harness.VaultPath));

        Assert.Empty(harness.Out);
        Assert.Equal(0, harness.Clipboard.SetCount);
        Assert.Contains(
            "keypaste get: the password of 'web/db' holds the KeePass placeholder {S:Recovery codes}, which names a field keypaste never releases; keypaste show 'web/db' --reveal shows it as the vault holds it",
            harness.Err,
            StringComparison.Ordinal);
        Assert.DoesNotContain("dbuser-5d1e", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("recovery-7b3a", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_WrongMasterPassword_ExitsAuthFailed()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));

        harness.Prompt.Enqueue("not the password");
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--reveal");

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

    /// <summary>
    /// The entry leaves the vault's own listing and lands somewhere it can be recovered from, and
    /// the command says which of the two deletions it did. Saying "Removed" for a deletion that
    /// can be undone, or the reverse, is the half of this a person acts on.
    /// </summary>
    [Fact]
    public void Rm_WithYes_MovesTheEntryToTheRecycleBin_AndSaysSo()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "one"), ("keep", "two"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("rm", "solo", "--vault", harness.VaultPath, "--yes");

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains("recycle bin", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Null(vault.Find("solo"));
        Assert.NotNull(vault.Find("keep"));

        var recycled = Assert.Single(vault.ReadRecycled());
        Assert.Equal("solo", recycled.Title, StringComparer.Ordinal);
        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(recycled.Id));
        Assert.Equal("one", vault.Find("solo")?.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// The confirmation is asked before the deletion, so it has to come from the vault rather than
    /// from what keypaste would prefer to be true.
    /// </summary>
    [Fact]
    public void Rm_AsksAConfirmationThatMatchesWhatTheVaultWillDo()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "one"));

        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue(Master, "y");
        var exit = harness.Run("rm", "solo", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains(
            harness.Prompt.PromptsSeen,
            prompt => prompt.Contains("recycle bin", StringComparison.Ordinal));

        Assert.DoesNotContain(
            harness.Prompt.PromptsSeen,
            prompt => prompt.Contains("no undo", StringComparison.Ordinal));
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

    /// <summary>
    /// A joined path is not an identity. An entry titled <c>b/c</c> in group <c>a</c> and an entry
    /// titled <c>c</c> in group <c>a/b</c> are both <c>a/b/c</c>, so there is nothing for the
    /// command to delete that would not be a guess (docs/PRODUCT.md law 3.7). The digest is the
    /// assertion that nothing was written: <see cref="Vault.Save"/> re-randomises salt and nonces,
    /// so an unchanged digest proves no save happened rather than that the contents matched.
    /// </summary>
    [Fact]
    public void Rm_ACollidingPath_IsRefused_AndTheVaultIsNotWritten()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("a/b/c", "nested"));
        Author(harness, "a", "b/c", "slashed");

        var before = Digest(harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("rm", "a/b/c", "--vault", harness.VaultPath, "--yes");

        Assert.NotEqual(CliApp.ExitSuccess, exit);
        Assert.Equal(before, Digest(harness.VaultPath), StringComparer.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal(2, vault.ReadEntries().Count);
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
    /// happens to be. In a vault with no recycle bin the deletion is irreversible, and in one with
    /// a bin it is most of the way there, so either way it is asked for explicitly.
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
        var exit = harness.Run("get", "solo", "--vault", harness.VaultPath, "--reveal");

        Assert.Equal(CliApp.ExitSuccess, exit);
    }

    [Fact]
    public void EnvironmentVariable_IsUsedWhenTheFlagIsAbsent()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("solo", "solo-secret"));
        harness.Environment[VaultLocator.EnvironmentVariable] = harness.VaultPath;

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "solo", "--reveal");

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

    /// <summary>
    /// One array on one line: groups and entries under their raw names, in the text listing's order,
    /// with no value and nothing from keypaste's own groups.
    /// </summary>
    [Fact]
    public void Ls_Json_GroupsAndEntries_NoValues_NoReserved()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("Banking/Chase", "chase-secret"));
        Author(harness, ".keypaste/tokens", "t1", "token-verifier");
        Author(harness, ".Keypaste", "planted", "planted-secret");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("ls", "--json", "--vault", harness.VaultPath));

        Assert.Equal(
            """[{"type":"group","path":"Banking"},{"type":"entry","path":"Banking/Chase","group":"Banking","title":"Chase"}]""" + Environment.NewLine,
            harness.Out);
        Assert.DoesNotContain("secret", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_Text_SkipsReserved()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master, ("Banking/Chase", "chase-secret"));
        Author(harness, ".keypaste/shares", "s1", "revoke-token");

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("ls", "--flat", "--vault", harness.VaultPath));

        Assert.Equal(["Banking/", "Banking/Chase"], harness.Out.ReplaceLineEndings("\n").TrimEnd().Split('\n'));
    }

    /// <summary>
    /// Writes an entry the CLI itself cannot create: <c>add</c> reads a separator in the argument
    /// as a group, so a title containing one has to come through the core, the way KeePassXC does.
    /// </summary>
    private static void Author(CliHarness harness, string groupPath, string title, string password)
    {
        using var vault = Vault.Open(harness.VaultPath, Master);
        vault.AddEntry(new VaultEntry { Title = title, Password = password, GroupPath = groupPath });
        vault.Save();
    }

    /// <summary>Adds <c>web/db</c>, whose <c>DATABASE_URL</c> names its username and password.</summary>
    private static void PlantDatabase(CliHarness harness, string password)
    {
        var name = new EntryName("web", "db");
        using var vault = Vault.Open(harness.VaultPath, Master);
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Username = "dbuser-5d1e", Password = password });
        Assert.True(vault.SetFields(
            name,
            [
                new FieldWrite("DB_SUFFIX", "9f2c"),
                new FieldWrite("DATABASE_URL", "postgres://{USERNAME}:{PASSWORD}@db"),
                new FieldWrite("Recovery codes", "recovery-7b3a"),
            ]));
        vault.Save();
    }

    private static string Digest(string path)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }
}
