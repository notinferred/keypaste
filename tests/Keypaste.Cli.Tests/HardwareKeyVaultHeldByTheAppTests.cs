using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The CLI reaches a vault that also needs a YubiKey only through the app holding it (D-0514): a routed edit and
/// <c>keypaste run --session</c> go to the holder before any password is read, the holder's save asks the key, and
/// nothing says the vault opens only in the app.
/// </summary>
/// <remarks>
/// The holder takes the vault's claim as the app does and answers on its pipe through the owner Core gives the app,
/// with the vault open under the key. Which process holds it is the claim's word; the CLI acts on nothing else.
/// </remarks>
public sealed class HardwareKeyVaultHeldByTheAppTests : IDisposable
{
    private const string _master = "held-hardware-key-master";
    private const string _value = "sk_live_yubikey_vault_sentinel";

    private static readonly byte[] _secret = Convert.FromHexString("6a7b8c9d0e1f20314253647586970a1b2c3d4e5f");

    private readonly CliHarness _harness = new();
    private readonly SoftwareYubiKey _device = new(_secret);
    private readonly string _pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public HardwareKeyVaultHeldByTheAppTests()
    {
        using (var key = new HardwareKey(new SoftwareYubiKey(_secret), 2))
        using (var vault = Vault.CreateWith(_harness.VaultPath, _master, null, key))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "Work", Title = "github", Password = "gh-before" });
            ProjectVariables.Set(vault, "dev", "TOKEN", _value);
            vault.Save();
        }

        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = _pipe;
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;

        // What the app records once it has opened the vault with the key, which makes a refused verb name the app.
        var recent = KeypasteHome.RecentPath(_harness.Directory);
        Assert.True(RecentVaults.Save(
            recent,
            RecentVaults.Remember(RecentVaults.Load(recent), _harness.VaultPath, DateTimeOffset.UtcNow, null, 2)));
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task An_edit_reaches_the_vault_through_the_app_and_its_save_asks_the_key()
    {
        using var key = new HardwareKey(_device, 2);

        await using (var owner = UnlockedOwner.Start(_harness, _pipe, _master, ApprovalAnswer.ApprovedOnce, key))
        {
            var asked = _device.Challenges.Count;

            using (Hold())
            {
                _harness.Prompt.Enqueue("gh-after");
                _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("set", "Work/github", "--vault", _harness.VaultPath));
            }

            Assert.Equal(1, owner.Channel.EditsAsked);
            Assert.True(_device.Challenges.Count > asked, "the holder saved the edit without asking the key");
        }

        Assert.DoesNotContain("Master password: ", _harness.Prompt.PromptsSeen);
        Assert.Contains("holds this vault; it will ask you to allow this change", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(VaultSession.HardwareKeyRefusal, _harness.Err, StringComparison.Ordinal);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(_harness.VaultPath, _master));
        using var reader = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var saved = Vault.Open(_harness.VaultPath, _master, null, reader);
        Assert.Equal("gh-after", saved.Find(new EntryName("Work", "github"))!.Password);
    }

    [Fact]
    public async Task Run_session_starts_the_child_with_the_set_the_app_released_and_asks_no_password()
    {
        using var key = new HardwareKey(_device, 2);
        await using var owner = UnlockedOwner.Start(_harness, _pipe, _master, ApprovalAnswer.Approved, key);

        using (Hold())
        {
            _harness.AssertExit(
                CliApp.ExitSuccess,
                _harness.Run("run", "--session", "--vault", _harness.VaultPath, "dev", "--", "deploy", "--to", "staging"));
        }

        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
        Assert.Equal("deploy", _harness.ProcessLauncher.Started[^1].FileName);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(VaultSession.HardwareKeyRefusal, _harness.Err, StringComparison.Ordinal);
        Assert.Equal(["TOKEN"], Assert.IsType<EnvReleasePrompt>(owner.Channel.Last).Keys);
    }

    /// <summary>The vault's claim, taken as the app takes it when it unlocks.</summary>
    private VaultClaim Hold()
    {
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, OwnerKind.DesktopApp, out var claim, out var refusal), refusal);
        return claim;
    }
}
