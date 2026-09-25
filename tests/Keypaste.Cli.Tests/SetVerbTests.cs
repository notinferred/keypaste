using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste set</c> creates an entry or replaces only its password, keeps the old value in
/// history, never prints the value, never writes keypaste's own groups and never saves under a
/// running owner.
/// </summary>
public sealed class SetVerbTests : IDisposable
{
    private const string _master = "set-verb-master";
    private const string _value = "SET-SENTINEL-6e1d";

    private readonly CliHarness _harness = new();

    public SetVerbTests()
    {
        _harness.SeedVault(_master);
        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Set(params string[] args) => _harness.Run(["set", .. args, "--vault", _harness.VaultPath]);

    private VaultEntry? Read(string group, string title)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.Find(new EntryName(group, title));
    }

    [Fact]
    public void Set_Creates()
    {
        _harness.Prompt.Enqueue(_master, _value);

        _harness.AssertExit(CliApp.ExitSuccess, Set("Banking/Chase"));

        Assert.Equal(_value, Read("Banking", "Chase")?.Password);
        Assert.Contains("  ✓ created Banking/Chase" + Environment.NewLine, _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);
    }

    [Fact]
    public void Set_UpdatesOnlyThePassword_KeepingHistory()
    {
        _harness.Prompt.Enqueue(_master, "old-value");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run(
            "add", "Banking/Chase", "--vault", _harness.VaultPath, "--username", "dan", "--url", "https://chase.example", "--notes", "n"));
        _harness.Stderr.GetStringBuilder().Clear();

        _harness.Prompt.Enqueue(_master, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set("Banking/Chase"));

        var entry = Read("Banking", "Chase");
        Assert.NotNull(entry);
        Assert.Equal(_value, entry.Password);
        Assert.Equal("dan", entry.Username);
        Assert.Equal("https://chase.example", entry.Url);
        Assert.Equal("n", entry.Notes);
        Assert.Contains("  ✓ updated Banking/Chase · the old value stays in history" + Environment.NewLine, _harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Contains(vault.ReadHistory(new EntryName("Banking", "Chase"))!, revision => revision.Fields.Password == "old-value");
    }

    [Fact]
    public void Set_RefusesANewEnvNameThatWouldMakeTheProfileUnusable()
    {
        _harness.Prompt.Enqueue(_master, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set("env/acme-api/staging/STRIPE"));

        foreach (var name in new[] { "env/acme-api/staging/bad-name", "env/acme-api/staging/stripe", "env/acme-api/Staging/KEY", "env/KEY", "env/acme-api/dev/KEY", "env/acme-api/staging/sub/KEY" })
        {
            _harness.Stderr.GetStringBuilder().Clear();
            _harness.Prompt.Enqueue(_master);
            _harness.AssertExit(CliApp.ExitUsageError, Set(name));
            Assert.Contains("Nothing was written.", _harness.Err, StringComparison.Ordinal);
        }

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Equal(["STRIPE"], vault.ReadEntries().Where(entry => entry.GroupPath.StartsWith("env", StringComparison.Ordinal)).Select(entry => entry.Title));
    }

    [Fact]
    public void Set_IntoTheRecycleBin_IsRefused()
    {
        _harness.Prompt.Enqueue(_master, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set("Banking/Chase"));
        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("rm", "Banking/Chase", "--yes", "--vault", _harness.VaultPath));
        _harness.Stderr.GetStringBuilder().Clear();

        _harness.Prompt.Enqueue(_master, "new-value");
        Assert.NotEqual(CliApp.ExitSuccess, Set("Recycle Bin/Chase"));

        Assert.Contains("recycle bin", _harness.Err, StringComparison.Ordinal);
        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Single(vault.ReadRecycled());
    }

    [Fact]
    public void Set_Generate()
    {
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(CliApp.ExitSuccess, Set("api/token", "--generate", "--length", "40"));

        var password = Read("api", "token")?.Password;
        Assert.NotNull(password);
        Assert.Equal(40, password.Length);
        Assert.DoesNotContain(password, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.Equal(["Master password: "], _harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Set_PipedValue()
    {
        _harness.Prompt.Interactive = false;
        _harness.Prompt.Enqueue(_master, _value);

        _harness.AssertExit(CliApp.ExitSuccess, Set("solo"));

        Assert.Equal(_value, Read(string.Empty, "solo")?.Password);
        Assert.Equal(["Master password: ", "Value: "], _harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Set_Interactive_AsksTwice_AndAMismatchSavesNothing()
    {
        _harness.Prompt.Interactive = true;
        _harness.Prompt.Enqueue(_master, _value, "something else");

        _harness.AssertExit(CliApp.ExitUsageError, Set("solo"));

        Assert.Null(Read(string.Empty, "solo"));
        Assert.Equal(["Master password: ", "Value: ", "Confirm value: "], _harness.Prompt.PromptsSeen);
        Assert.Contains("did not match; nothing was saved", _harness.Err, StringComparison.Ordinal);

        _harness.Prompt.Enqueue(_master, _value, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set("solo"));
        Assert.Equal(_value, Read(string.Empty, "solo")?.Password);
    }

    [Theory]
    [InlineData(".keypaste/tokens/t1", ".keypaste/tokens/t1")]
    [InlineData(".keypaste/x", ".keypaste/x")]
    [InlineData(".KEYPASTE/x", ".KEYPASTE/x")]
    [InlineData("/.keypaste/tokens/x", ".keypaste/tokens/x")]
    [InlineData(".keypaste//x", ".keypaste/x")]
    [InlineData("//.keypaste/x", ".keypaste/x")]
    public void Set_Reserved_IsRefused(string path, string shown)
    {
        _harness.AssertExit(CliApp.ExitUsageError, Set(path));

        Assert.Contains($"keypaste set: {shown} is keypaste's own group; it cannot be written here", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Theory]
    [InlineData("/Banking/Chase")]
    [InlineData("Banking//Chase")]
    [InlineData("//Banking/Chase")]
    public void Set_WithEmptySegments_UpdatesTheExistingEntry(string path)
    {
        _harness.Prompt.Enqueue(_master, "old-value");
        _harness.AssertExit(CliApp.ExitSuccess, Set("Banking/Chase"));
        _harness.Stderr.GetStringBuilder().Clear();

        _harness.Prompt.Enqueue(_master, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set(path));

        Assert.Contains("  ✓ updated Banking/Chase", _harness.Err, StringComparison.Ordinal);
        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Equal(_value, vault.Find(new EntryName("Banking", "Chase"))?.Password);
        Assert.Single(vault.ReadEntries(), entry => entry.Title == "Chase");
    }

    [Theory]
    [InlineData(".Keypaste/shares/s1", null, ".Keypaste/shares/s1")]
    [InlineData("s1", ".keypaste", ".keypaste/s1")]
    [InlineData("/.keypaste/tokens/s1", null, ".keypaste/tokens/s1")]
    [InlineData("s1", "/.keypaste", ".keypaste/s1")]
    [InlineData("s1", ".keypaste//tokens", ".keypaste/tokens/s1")]
    public void Add_Reserved_IsRefused(string target, string? group, string shown)
    {
        string[] groupArgs = group is null ? [] : ["--group", group];

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(["add", target, .. groupArgs, "--vault", _harness.VaultPath]));

        Assert.Contains($"keypaste add: {shown} is keypaste's own group; it cannot be written here", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Theory]
    [InlineData("KEY", "env/acme-api/dev", "the dev profile is env/acme-api itself")]
    [InlineData("KEY", "env/acme-api/staging/sub", "'env/acme-api/staging/sub' is never read")]
    public void Add_WhereNoProfileReads_IsRefused(string title, string group, string reason)
    {
        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("add", title, "--group", group, "--vault", _harness.VaultPath));

        Assert.Contains(reason, _harness.Err, StringComparison.Ordinal);
        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.DoesNotContain(vault.ReadEntries(), entry => entry.GroupPath.StartsWith("env", StringComparison.Ordinal));
    }

    [Fact]
    public void Add_WithALeadingSlash_FindsTheExistingEntry()
    {
        _harness.Prompt.Enqueue(_master, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set("Banking/Chase"));

        _harness.Prompt.Enqueue(_master);
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("add", "/Banking/Chase", "--vault", _harness.VaultPath));

        Assert.Contains("keypaste add: 'Banking/Chase' already exists", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_WhileTheVaultIsHeld_IsRefused_BeforeThePassword()
    {
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, OwnerKind.TerminalAgent, out var claim, out var refusal), refusal);
        var before = File.ReadAllBytes(_harness.VaultPath);

        using (claim)
        {
            _harness.Prompt.Enqueue(_master, _value);
            _harness.AssertExit(CliApp.ExitInternalError, Set("Banking/Chase"));
        }

        Assert.Contains("keypaste: this vault is already unlocked in keypaste agent", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("Lock it there first.", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Equal(before, File.ReadAllBytes(_harness.VaultPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Set_NeverEchoesTheValue(bool update)
    {
        if (update)
        {
            _harness.Prompt.Enqueue(_master, "first");
            _harness.AssertExit(CliApp.ExitSuccess, Set("secrets/target"));
        }

        _harness.Prompt.Enqueue(_master, _value);
        _harness.AssertExit(CliApp.ExitSuccess, Set("secrets/target"));

        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("first", _harness.Out + _harness.Err, StringComparison.Ordinal);
    }
}
