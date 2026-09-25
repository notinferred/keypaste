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
    [InlineData(".keypaste/tokens/t1")]
    [InlineData(".keypaste/x")]
    [InlineData(".KEYPASTE/x")]
    public void Set_Reserved_IsRefused(string path)
    {
        _harness.AssertExit(CliApp.ExitUsageError, Set(path));

        Assert.Contains($"keypaste set: {path} is keypaste's own group; it cannot be written here", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Add_Reserved_IsRefused()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("add", ".Keypaste/shares/s1", "--vault", _harness.VaultPath));
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("add", "s1", "--group", ".keypaste", "--vault", _harness.VaultPath));

        Assert.Contains("keypaste add: .Keypaste/shares/s1 is keypaste's own group; it cannot be written here", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("keypaste add: .keypaste/s1 is keypaste's own group", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
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
