using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The <c>env</c> verb group end to end, through the real dispatch and the real vault.
/// </summary>
/// <remarks>
/// Each vault-touching test pays for Argon2 at 64 MiB twice over — once to create the vault and
/// once per command that opens it — so assertions are grouped by behaviour rather than split one
/// per fact. If this class gets slow, cut the number of vault-touching tests, never the KDF.
/// </remarks>
public sealed class EnvVerbTests
{
    internal const string Master = "correct horse battery staple";

    [Fact]
    public void Set_ThenLs_ShowsTheProjectAndTheKey_ButNeverTheValue()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master, "postgres://user:pw@localhost/db");
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "billing", "DATABASE_URL", "--vault", harness.VaultPath));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "ls", "--vault", harness.VaultPath));
        Assert.Equal("billing", harness.Out.ReplaceLineEndings("\n").Trim(), StringComparer.Ordinal);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "ls", "billing", "--vault", harness.VaultPath));
        Assert.Equal("DATABASE_URL", harness.Out.ReplaceLineEndings("\n").Trim(), StringComparer.Ordinal);
        Assert.DoesNotContain("postgres", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// The convention is a promise about where the value lands. A user who opens the vault in
    /// KeePassXC navigates to this exact path, and <c>keypaste get</c> has to reach it too.
    /// </summary>
    [Fact]
    public void Set_StoresTheValueAtEnvProjectKey_ReadableByGet()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master, "s3cret-value");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "env/billing/TOKEN", "--show", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal("s3cret-value", harness.Out.ReplaceLineEndings("\n").Trim(), StringComparer.Ordinal);
    }

    [Fact]
    public void Set_WithAnInlineValue_TakesItFromTheArgument()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        // One stdin line only: the master password. The value came from argv.
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "set", "billing", "TOKEN=inline-value", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("inline-value", vault.Find("env/billing/TOKEN")?.Password, StringComparer.Ordinal);
    }

    /// <summary>A connection string is mostly equals signs; only the first one separates.</summary>
    [Fact]
    public void Set_WithAnInlineValue_SplitsOnTheFirstEqualsOnly()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "CONN=Server=db;Pwd=a=b", "--vault", harness.VaultPath);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("Server=db;Pwd=a=b", vault.Find("env/billing/CONN")?.Password, StringComparer.Ordinal);
    }

    [Fact]
    public void Set_WithAnEmptyInlineValue_StoresAnEmptyValue()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "billing", "OPTIONAL=", "--vault", harness.VaultPath));

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal(string.Empty, vault.Find("env/billing/OPTIONAL")?.Password, StringComparer.Ordinal);
    }

    [Fact]
    public void Set_OverAnExistingKey_SaysItKeptTheOldValueInHistory()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master, "first");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);
        Assert.Contains("Set env/billing/TOKEN", harness.Err, StringComparison.Ordinal);

        harness.Stderr.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master, "second");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);

        // The retention is stated where it happens, not only in SECURITY.md — a user rotating a
        // leaked credential needs to know the old one is still in the file (DECISIONS.md D-0014).
        Assert.Contains("Updated env/billing/TOKEN", harness.Err, StringComparison.Ordinal);
        Assert.Contains("history", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("second", vault.Find("env/billing/TOKEN")?.Password, StringComparer.Ordinal);
    }

    [Fact]
    public void Set_RefusesANameThatIsNotAnEnvironmentVariable()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "set", "billing", "not-a-key=v", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("not-a-key", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Empty(vault.ReadEntries());
    }

    [Fact]
    public void Rm_RemovesTheVariable_AndLeavesTheRest()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "B=2", "--vault", harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "rm", "billing", "A", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Null(vault.Find("env/billing/A"));
        Assert.NotNull(vault.Find("env/billing/B"));
    }

    [Fact]
    public void Rm_WithoutYes_AndRedirectedStdin_IsAUsageError()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);

        harness.Prompt.Interactive = false;
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "rm", "billing", "A", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--yes", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.NotNull(vault.Find("env/billing/A"));
    }

    [Fact]
    public void MissingProjectOrKey_ExitsNotFound()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "ls", "nope", "--vault", harness.VaultPath));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "rm", "nope", "A", "--yes", "--vault", harness.VaultPath));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "rm", "billing", "NOPE", "--yes", "--vault", harness.VaultPath));
    }

    /// <summary>
    /// A project with no variables left is not the same as a project that never existed, and
    /// <c>env ls</c> has to keep telling them apart or a script cannot branch on it.
    /// </summary>
    [Fact]
    public void Ls_OnAProjectWithNoVariablesLeft_SucceedsWithNoOutput()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "rm", "billing", "A", "--yes", "--vault", harness.VaultPath);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "ls", "billing", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Empty(harness.Out);
    }

    [Fact]
    public void Ls_OnAVaultWithNoEnvGroup_SucceedsWithNoOutput()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "ls", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Empty(harness.Out);
    }

    /// <summary>
    /// A variable KeePassXC created under a name keypaste would refuse is still listed — hiding it
    /// would make the two tools disagree about one file (CORE.md law 4.6) — but the warning that
    /// it cannot be exported goes to stderr, leaving stdout machine-readable.
    /// </summary>
    [Fact]
    public void Ls_ListsAnUnusableName_AndWarnsOnStderrOnly()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "FINE=1", "--vault", harness.VaultPath);

        using (var vault = Vault.Open(harness.VaultPath, Master))
        {
            vault.AddEntry(new VaultEntry { Title = "not a key", Password = "v", GroupPath = "env/billing" });
            vault.Save();
        }

        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "ls", "billing", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);

        var lines = harness.Out.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal(["FINE", "not a key"], lines);
        Assert.Contains("not a key", harness.Err, StringComparison.Ordinal);
        Assert.Contains("warning", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two entries with one name is legal KDBX and KeePassXC will create it. There is no correct
    /// value to report, so it fails rather than silently picking one (CORE.md law 3.7).
    /// </summary>
    [Fact]
    public void Ls_FailsLoudly_WhenKeePassXcLeftTwoEntriesWithOneName()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        using (var vault = Vault.Open(harness.VaultPath, Master))
        {
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "one", GroupPath = "env/billing" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "two", GroupPath = "env/billing" });
            vault.Save();
        }

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "ls", "billing", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("TOKEN", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("one", harness.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("env")]
    [InlineData("env", "bogus")]
    [InlineData("env", "set", "billing")]
    [InlineData("env", "set", "billing", "A", "B")]
    [InlineData("env", "set", "billing", "=novalue")]
    [InlineData("env", "rm", "billing")]
    [InlineData("env", "ls", "a", "b")]
    public void MalformedInvocations_AreUsageErrors_OnStderr(params string[] args)
    {
        using var harness = new CliHarness();

        var exit = harness.Run(args);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.NotEmpty(harness.Err);
        Assert.Empty(harness.Out);
    }

    [Theory]
    [InlineData("env", "--help")]
    [InlineData("env", "-h")]
    [InlineData("env", "help")]
    [InlineData("env", "ls", "--help")]
    [InlineData("env", "set", "--help")]
    [InlineData("env", "rm", "--help")]
    public void Help_GoesToStdout_AndExitsZero(params string[] args)
    {
        using var harness = new CliHarness();

        var exit = harness.Run(args);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains("usage: keypaste env", harness.Out, StringComparison.Ordinal);
        Assert.Empty(harness.Err);
    }

    /// <summary>
    /// The group listing has to stay usable in a terminal that is not UTF-8, exactly as
    /// <c>keypaste ls</c> does — this is what stops someone reaching for box-drawing characters.
    /// </summary>
    [Fact]
    public void EnvUsage_IsAscii()
    {
        using var harness = new CliHarness();
        harness.Run("env", "--help");

        Assert.All(harness.Out, c => Assert.True(c < 128, $"non-ASCII character '{c}' in env usage"));
    }

    private static void SeedVault(CliHarness harness)
    {
        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue(Master, Master);
        harness.Run("init", harness.VaultPath);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
    }
}
