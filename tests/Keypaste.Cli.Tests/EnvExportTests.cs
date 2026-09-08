using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste env export</c> at the command level. The grammar of what gets written is covered in
/// <c>DotEnvWriterTests</c>, which opens no vault; these assert only what the command adds — the
/// destination rules, the warning, the confirmation, and the permissions on the file it leaves
/// behind.
/// </summary>
public sealed class EnvExportTests
{
    internal const string Master = "export-master-pw";

    private static CliHarness Seeded(params (string Key, string Value)[] variables)
    {
        var harness = new CliHarness();
        harness.SeedVault(Master);

        if (variables.Length > 0)
        {
            using var vault = Vault.Open(harness.VaultPath, Master);
            foreach (var (key, value) in variables)
            {
                vault.AddEntry(new VaultEntry { Title = key, Password = value, GroupPath = "env/billing" });
            }

            vault.Save();
        }

        harness.Prompt.PromptsSeen.Clear();
        return harness;
    }

    private static CliHarness SeededWithTwo() =>
        Seeded(("API_KEY", "sk_live_secret"), ("PORT", "8080"));

    private static string Target(CliHarness harness, string name = "out.env") =>
        Path.Combine(harness.Directory, name);

    [Fact]
    public void Export_WritesAFileThatPullReadsBackUnchanged()
    {
        using var harness = SeededWithTwo();
        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);

        Assert.True(DotEnv.TryDecode(File.ReadAllBytes(path), out var text, out var decodeError), decodeError);
        Assert.True(DotEnv.TryParse(text, out var document));
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["API_KEY"] = "sk_live_secret",
                ["PORT"] = "8080",
            },
            document.Variables.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// The whole round trip, through the two verbs a user actually types. It is what makes the
    /// escape hatch an escape hatch rather than a lossy dump.
    /// </summary>
    [Fact]
    public void ExportThenPull_ReturnsTheSameValues()
    {
        using var harness = Seeded(
            ("APOSTROPHE", "it's"),
            ("HASHED", "hunter2#42"),
            ("MULTILINE", "-----BEGIN-----\nbody\n-----END-----"),
            ("SPACED", "  padded  "),
            ("TEMPLATE", "${NOT_EXPANDED}"),
            ("WINDOWS", "C:\\logs\\app"));

        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run(
            "env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath));

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run(
            "env", "pull", "roundtrip", path, "--yes", "--keep", "--vault", harness.VaultPath));

        using var vault = Vault.Open(harness.VaultPath, Master);
        var store = new EnvStore(vault);

        Assert.Equal(store.Read("billing"), store.Read("roundtrip"));
    }

    [Fact]
    public void Export_ShoutsBeforeItWrites_AndNamesTheFile()
    {
        using var harness = SeededWithTwo();
        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        var alarm = Assert.Single(harness.ConsoleStyle.Alarms);
        Assert.Contains("plaintext", alarm, StringComparison.Ordinal);
        Assert.Contains(path, harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing to be loud about, so it is not. A warning printed when there is no secret is how a
    /// warning stops being read.
    /// </summary>
    [Fact]
    public void AnEmptyProject_IsWrittenWithoutAnAlarm()
    {
        using var harness = new CliHarness();
        harness.SeedVault(Master);

        using (var vault = Vault.Open(harness.VaultPath, Master))
        {
            vault.AddEntry(new VaultEntry { Title = "PLACEHOLDER", Password = "x", GroupPath = "env/empty" });
            Assert.True(new EnvStore(vault).Remove("empty", "PLACEHOLDER"));
            vault.Save();
        }

        var path = Target(harness);
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "export", "empty", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Empty(harness.ConsoleStyle.Alarms);
        Assert.Equal(DotEnvWriter.Header, File.ReadAllText(path));
    }

    // ---- the destination ----------------------------------------------------------------

    /// <summary>
    /// The <c>init</c> precedent. Overwriting is how somebody loses the handful of variables they
    /// had not got round to importing yet.
    /// </summary>
    [Fact]
    public void AnExistingFile_IsRefused_WithoutForce()
    {
        using var harness = SeededWithTwo();
        var path = Target(harness);
        File.WriteAllText(path, "PRECIOUS=keep me\n");

        var exit = harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--force", harness.Err, StringComparison.Ordinal);
        Assert.Equal("PRECIOUS=keep me\n", File.ReadAllText(path));

        // Refused before the vault was ever opened: a destination that cannot be written should not
        // cost a password entry and a key derivation to discover.
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void AnExistingFile_IsReplaced_WithForce()
    {
        using var harness = SeededWithTwo();
        var path = Target(harness);
        File.WriteAllText(path, "STALE=old\n");

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "billing", path, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.DoesNotContain("STALE", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingDirectory_IsNotFound_AndCostsNoPassword()
    {
        using var harness = SeededWithTwo();
        var path = Path.Combine(harness.Directory, "nope", "out.env");

        var exit = harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void AGitAncestor_IsPointedOut()
    {
        using var harness = SeededWithTwo();
        Directory.CreateDirectory(Path.Combine(harness.Directory, ".git"));
        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Contains("git repository", harness.Err, StringComparison.Ordinal);
        Assert.Contains(".gitignore", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unix only: Windows has no equivalent, and SECURITY.md states that gap rather than implying
    /// a mode keypaste never set.
    /// </summary>
    [Fact]
    public void TheFileIsReadableOnlyByItsOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no owner-only file mode. SECURITY.md states that gap rather than implying a mode keypaste never set.");
            return;
        }

        using var harness = SeededWithTwo();
        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run(
            "env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    /// <summary>The mode has to survive <c>--force</c> too, which is why the old file is removed.</summary>
    [Fact]
    public void TheModeSurvivesAnOverwriteOfAWideOpenFile()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no owner-only file mode. SECURITY.md states that gap rather than implying a mode keypaste never set.");
            return;
        }

        using var harness = SeededWithTwo();
        var path = Target(harness);
        File.WriteAllText(path, "STALE=old\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        harness.Prompt.Enqueue(Master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run(
            "env", "export", "billing", path, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    // ---- --stdout -----------------------------------------------------------------------

    [Fact]
    public void Stdout_PrintsTheFile_AndWritesNothing()
    {
        using var harness = SeededWithTwo();

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "export", "billing", "--dotenv", "--stdout", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("API_KEY=sk_live_secret", harness.Out, StringComparison.Ordinal);
        Assert.Contains("PORT=8080", harness.Out, StringComparison.Ordinal);

        // The warning is data-free and on the other stream, so the pipe stays clean.
        Assert.Single(harness.ConsoleStyle.Alarms);
        Assert.DoesNotContain("sk_live_secret", harness.Err, StringComparison.Ordinal);

        Assert.Empty(Directory.GetFiles(harness.Directory, "*.env"));
    }

    /// <summary>
    /// Naming <c>--stdout</c> is the consent, exactly as <c>get --show</c> is, so a piped run is
    /// not a usage error the way it is for the file form.
    /// </summary>
    [Fact]
    public void Stdout_NeedsNoConfirmation_EvenWithStdinRedirected()
    {
        using var harness = SeededWithTwo();
        harness.Prompt.Interactive = false;

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "export", "billing", "--dotenv", "--stdout", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
    }

    // ---- confirmation -------------------------------------------------------------------

    [Fact]
    public void Declining_WritesNothing()
    {
        using var harness = SeededWithTwo();
        harness.Prompt.Interactive = true;
        var path = Target(harness);

        harness.Prompt.Enqueue(Master, "n");
        var exit = harness.Run("env", "export", "billing", path, "--dotenv", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("Cancelled.", harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Accepting_Writes()
    {
        using var harness = SeededWithTwo();
        harness.Prompt.Interactive = true;
        var path = Target(harness);

        harness.Prompt.Enqueue(Master, "y");
        harness.AssertExit(CliApp.ExitSuccess, harness.Run(
            "env", "export", "billing", path, "--dotenv", "--vault", harness.VaultPath));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void APipedRunWithoutYes_IsAUsageError_AndCostsNoPassword()
    {
        using var harness = SeededWithTwo();
        harness.Prompt.Interactive = false;

        var exit = harness.Run(
            "env", "export", "billing", Target(harness), "--dotenv", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--yes is required", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    // ---- fail closed --------------------------------------------------------------------

    /// <summary>
    /// A name KeePassXC will let you create and no <c>.env</c> reader will accept. Every offending
    /// key is named, so one pass in KeePassXC fixes them all.
    /// </summary>
    [Fact]
    public void AnUnusableName_RefusesTheWholeExport()
    {
        using var harness = Seeded(("GOOD", "1"), ("BAD-NAME", "2"), ("also.bad", "3"));
        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("BAD-NAME", harness.Err, StringComparison.Ordinal);
        Assert.Contains("also.bad", harness.Err, StringComparison.Ordinal);
        Assert.Contains("Nothing was written.", harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NamesDifferingOnlyInCase_RefuseTheWholeExport()
    {
        using var harness = Seeded(("PATH", "1"), ("Path", "2"));
        var path = Target(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "export", "billing", path, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("case", harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AnUnknownProject_IsNotFound()
    {
        using var harness = SeededWithTwo();

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "nosuch", Target(harness), "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.False(File.Exists(Target(harness)));
    }

    [Fact]
    public void TheWrongMasterPassword_FailsAuthentication()
    {
        using var harness = SeededWithTwo();

        harness.Prompt.Enqueue("not-the-password");
        var exit = harness.Run(
            "env", "export", "billing", Target(harness), "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.False(File.Exists(Target(harness)));
    }

    // ---- advisories ----------------------------------------------------------------------

    [Fact]
    public void AValueThatNeededEscapingIsNamed_ButNeverShown()
    {
        using var harness = Seeded(("APOSTROPHE", "it's a secret"), ("PLAIN", "fine"));

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "export", "billing", Target(harness), "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Contains("APOSTROPHE", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("it's a secret", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAIN", harness.Err, StringComparison.Ordinal);
    }

    // ---- the vault is not a destination --------------------------------------------------

    /// <summary>
    /// The reported defect. <c>--force</c> deletes the destination and writes a <c>.env</c> in its
    /// place; when the destination is the vault, one project's variables replace every entry in the
    /// file, in plaintext, with no history and nothing to recover from.
    /// </summary>
    [Fact]
    public void TheVaultItself_IsRefused_EvenWithForceAndYes()
    {
        using var harness = SeededWithTwo();
        var before = File.ReadAllBytes(harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "billing", harness.VaultPath,
            "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.NotNull(vault.Find("env/billing/API_KEY"));
    }

    [Fact]
    public void TheVaultItself_IsRefusedAsADestination()
    {
        using var harness = SeededWithTwo();
        var before = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run(
            "env", "export", "billing", harness.VaultPath, "--dotenv", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("is the vault", harness.Err, StringComparison.Ordinal);

        // The refusal next door offers --force. This one must not, or it teaches the way past itself.
        Assert.DoesNotContain("pass --force to overwrite", harness.Err, StringComparison.Ordinal);

        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    /// <summary>The same file named two ways, without touching the process-wide current directory.</summary>
    [Fact]
    public void AnUnnormalisedPathToTheVault_IsRefused()
    {
        using var harness = SeededWithTwo();
        Directory.CreateDirectory(Path.Combine(harness.Directory, "sub"));
        var dotted = Path.Combine(harness.Directory, "sub", "..", "vault.kdbx");
        var before = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run(
            "env", "export", "billing", dotted, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));
    }

    /// <summary>
    /// The relative spelling V-F.1b asks for, built against the working directory rather than by
    /// changing it: <c>Directory.SetCurrentDirectory</c> is process-global and unsafe while the
    /// rest of the suite runs in parallel, which is why EnvPullTests avoids it too.
    /// </summary>
    [Fact]
    public void ARelativePathToTheVault_IsRefused()
    {
        using var harness = SeededWithTwo();
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), harness.VaultPath);
        if (Path.IsPathRooted(relative))
        {
            Assert.Skip("the temporary directory is on another volume, so there is no relative spelling.");
        }

        var before = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run(
            "env", "export", "billing", relative, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void TheVaultNamedInADifferentCase_IsRefused()
    {
        using var harness = SeededWithTwo();
        if (!CaseInsensitiveHere(harness))
        {
            Assert.Skip("this file system is case-sensitive, so these are two different names.");
        }

        var shouted = Path.Combine(harness.Directory, "VAULT.KDBX");
        var before = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run(
            "env", "export", "billing", shouted, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));
    }

    /// <summary>
    /// A file symlink is the gentler half: <c>--force</c> deletes the link rather than the vault,
    /// so the vault survives and the alias silently does not. Both are refusals.
    /// </summary>
    [Fact]
    public void TheVaultThroughASymlink_IsRefused()
    {
        using var harness = SeededWithTwo();
        var alias = Path.Combine(harness.Directory, "alias.kdbx");
        Link(alias, harness.VaultPath, directory: false);
        var before = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run(
            "env", "export", "billing", alias, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));
        Assert.True(File.Exists(alias));
    }

    /// <summary>
    /// The alias that actually destroys the vault: the link is above the file name, so the delete
    /// resolves through it and lands on the real thing.
    /// </summary>
    [Fact]
    public void AVaultUnderASymlinkedDirectory_IsRefused()
    {
        using var harness = SeededWithTwo();
        var link = Path.Combine(harness.Directory, "link");
        Link(link, harness.Directory, directory: true);
        var before = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run(
            "env", "export", "billing", Path.Combine(link, "vault.kdbx"),
            "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(harness.VaultPath));
    }

    /// <summary>
    /// The master-password prompt sits between the first check and the write, which is long enough
    /// for the destination to become something else. It needs no link, so it never skips.
    /// </summary>
    [Fact]
    public void ADestinationThatBecomesAVaultAfterTheCheck_IsStillRefused()
    {
        using var harness = SeededWithTwo();
        var target = Target(harness);
        harness.Prompt.OnPrompt = _ => File.Copy(harness.VaultPath, target, overwrite: true);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "billing", target, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(File.ReadAllBytes(harness.VaultPath), File.ReadAllBytes(target));
    }

    /// <summary>
    /// Wider than "the vault this export reads from", deliberately: a hard link and a bind mount
    /// reach one file by a path nothing managed can resolve, and losing somebody else's vault costs
    /// exactly as much. DECISIONS.md D-0092 records the widening.
    /// </summary>
    [Fact]
    public void AKdbxDestination_IsRefused_EvenWhenItIsNotTheSource()
    {
        using var harness = SeededWithTwo();
        var other = Path.Combine(harness.Directory, "other.kdbx");
        File.Copy(harness.VaultPath, other);
        var before = File.ReadAllBytes(other);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "billing", other, "--dotenv", "--yes", "--force", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Equal(before, File.ReadAllBytes(other));
    }

    /// <summary>The guard did not become "refuse anything near a vault".</summary>
    [Fact]
    public void AFileBesideTheVault_StillExports()
    {
        using var harness = SeededWithTwo();
        var target = Target(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run(
            "env", "export", "billing", target, "--dotenv", "--yes", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("API_KEY", File.ReadAllText(target), StringComparison.Ordinal);
        Assert.Single(harness.ConsoleStyle.Alarms);
    }

    /// <summary>
    /// Creates a link, or skips. Windows needs Developer Mode or an elevated shell, and a machine
    /// that cannot make one must say so rather than pass a test it never ran.
    /// </summary>
    private static void Link(string path, string target, bool directory)
    {
        try
        {
            if (directory)
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create symbolic links: {ex.Message}");
        }
    }

    private static bool CaseInsensitiveHere(CliHarness harness)
    {
        var probe = Path.Combine(harness.Directory, "case-probe");
        File.WriteAllText(probe, string.Empty);
        try
        {
            return File.Exists(Path.Combine(harness.Directory, "CASE-PROBE"));
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
