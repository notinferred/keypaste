using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The CLI cannot ask a YubiKey (D-0366), so a verb or <c>keypaste approve</c> refused a vault the app last opened
/// with one names the app and what reaches the vault through it, and a wrong password elsewhere says nothing of keys.
/// </summary>
public sealed class HardwareKeyVaultRefusalTests : IDisposable
{
    private const string _master = "hardware-key-refusal-master";

    private static readonly byte[] _secret = Convert.FromHexString("0f1e2d3c4b5a69788796a5b4c3d2e1f00112233a");

    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_verb_refused_a_vault_the_app_opened_with_a_YubiKey_names_the_app()
    {
        HardwareKeyVault();
        Remember(_harness.VaultPath, hardwareKeySlot: 2);
        _harness.Prompt.Enqueue(_master);

        var exit = _harness.Run("ls", "--vault", _harness.VaultPath);

        _harness.AssertExit(CliApp.ExitAuthFailed, exit);
        Assert.Contains("keypaste: wrong master password", _harness.Err, StringComparison.Ordinal);
        Assert.Contains(VaultSession.HardwareKeyRefusal, _harness.Err, StringComparison.Ordinal);
        Assert.Contains("opens only in the desktop app", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Approve_refused_a_vault_the_app_opened_with_a_YubiKey_names_the_app()
    {
        HardwareKeyVault();
        Remember(_harness.VaultPath, hardwareKeySlot: 1);
        _harness.Prompt.Enqueue(_master);

        var exit = _harness.Run("approve", "--vault", _harness.VaultPath);

        _harness.AssertExit(CliApp.ExitAuthFailed, exit);
        Assert.Contains(VaultSession.HardwareKeyRefusal, _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_password_for_a_vault_remembered_without_a_key_says_nothing_of_one()
    {
        _harness.SeedVault(_master);
        Remember(_harness.VaultPath, hardwareKeySlot: null);
        Remember(Path.Combine(_harness.Directory, "other.kdbx"), hardwareKeySlot: 2);
        _harness.Prompt.Enqueue("not the master password");

        var exit = _harness.Run("ls", "--vault", _harness.VaultPath);

        _harness.AssertExit(CliApp.ExitAuthFailed, exit);
        Assert.Contains("keypaste: wrong master password", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("YubiKey", _harness.Err, StringComparison.Ordinal);
    }

    private void HardwareKeyVault()
    {
        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var vault = Vault.CreateWith(_harness.VaultPath, _master, null, key);
        vault.Save();
    }

    private void Remember(string vaultPath, int? hardwareKeySlot)
    {
        var recent = KeypasteHome.RecentPath(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Assert.True(RecentVaults.Save(
            recent,
            RecentVaults.Remember(RecentVaults.Load(recent), vaultPath, DateTimeOffset.UtcNow, null, hardwareKeySlot)));
    }
}
