using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste rotate</c> replaces a password with a generated one, keeps the old one in history,
/// never prints either, and never saves under a running owner.
/// </summary>
public sealed class RotateVerbTests : IDisposable
{
    private const string _master = "rotate-verb-master";
    private const string _old = "ROTATE-OLD-SENTINEL-4c2e";

    private readonly CliHarness _harness = new();

    public RotateVerbTests()
    {
        _harness.SeedVault(_master);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.AddEntry(new VaultEntry { GroupPath = "Banking", Title = "Chase", Username = "dan", Password = _old });
        vault.AddEntry(new VaultEntry { GroupPath = ".keypaste/tokens", Title = "t1", Password = "verifier" });
        vault.Save();
        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Rotate(params string[] args) => _harness.Run(["rotate", .. args, "--vault", _harness.VaultPath]);

    private VaultEntry Read()
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.Find(new EntryName("Banking", "Chase"))!;
    }

    [Fact]
    public void Rotate_ReplacesThePassword_AndNeverPrintsIt()
    {
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(CliApp.ExitSuccess, Rotate("Banking/Chase"));

        var rotated = Read();
        Assert.NotEqual(_old, rotated.Password);
        Assert.Equal(20, rotated.Password.Length);
        Assert.Equal("dan", rotated.Username);
        Assert.Contains("  ✓ rotated Banking/Chase · 20 characters · the old value stays in history", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(rotated.Password, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_old, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Contains(vault.ReadHistory(new EntryName("Banking", "Chase"))!, revision => revision.Fields.Password == _old);
    }

    [Fact]
    public void Rotate_Words()
    {
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(CliApp.ExitSuccess, Rotate("Banking/Chase", "--words", "6", "--separator", "."));

        Assert.Equal(6, Read().Password.Split('.').Length);
        Assert.Contains("· 6 words ·", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotate_Unknown_Exits3()
    {
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(CliApp.ExitNotFound, Rotate("Banking/Nope"));

        Assert.Contains("keypaste rotate: no entry named Banking/Nope", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotate_Reserved_IsRefused()
    {
        _harness.AssertExit(CliApp.ExitUsageError, Rotate(".keypaste/tokens/t1"));

        Assert.Contains("keypaste rotate: .keypaste/tokens/t1 is keypaste's own group", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Rotate_WhileHeld_IsRefused_BeforeThePassword()
    {
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, OwnerKind.TerminalAgent, out var claim, out var refusal), refusal);
        var before = File.ReadAllBytes(_harness.VaultPath);

        using (claim)
        {
            _harness.Prompt.Enqueue(_master);
            _harness.AssertExit(CliApp.ExitInternalError, Rotate("Banking/Chase"));
        }

        Assert.Contains("Lock it there first.", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Equal(before, File.ReadAllBytes(_harness.VaultPath));
    }

    [Fact]
    public void Rotate_Help_SaysAProviderKeyIsRotatedThere()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("rotate", "--help"));

        Assert.Contains("rotated at the provider", _harness.Out.ReplaceLineEndings(" "), StringComparison.Ordinal);
    }
}
