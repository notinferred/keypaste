using System.Security.Cryptography;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Adding and removing a YubiKey in Settings: the vault is re-encrypted under the new factors, the
/// app stays open on it, the confirmation says plainly what a lost key costs, and every result is
/// read from the file.
/// </summary>
public sealed class VaultAccessHardwareKeyTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";

    private static readonly byte[] _secret = Convert.FromHexString("00112233445566778899aabbccddeeff01234567");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-access-yubikey-tests-").FullName;
    private readonly string _home;
    private readonly string _vaultPath;
    private readonly SoftwareYubiKey _device = new(_secret);
    private readonly FakeVaultFilePicker _picker = new();

    public VaultAccessHardwareKeyTests()
    {
        _home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);
        vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
        vault.Save();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_yubikey_added_in_settings_is_required_from_then_on_and_the_app_stays_open()
    {
        using var session = Unlocked(slot: null);
        using var settings = Settings(session);
        var access = settings.Access;

        Enter(access, _master);
        access.AttachHardwareKey = true;
        access.NewSlotOne = true;
        access.ReviewCommand.Execute(null);

        Assert.True(access.IsConfirming);
        Assert.Contains("YubiKey in slot 1", access.Summary, StringComparison.Ordinal);
        Assert.Contains("If this YubiKey is lost or broken, the vault cannot be opened", access.HardwareKeyWarning, StringComparison.Ordinal);
        Assert.Contains("second YubiKey programmed with the same secret in the same slot", access.HardwareKeyWarning, StringComparison.Ordinal);

        await access.ChangeAsync();

        Assert.StartsWith("Changed.", access.Message, StringComparison.Ordinal);
        Assert.True(session.IsUnlocked);
        Assert.Equal(1, session.HardwareKeySlot);
        Assert.Single(_device.Challenges);
        Assert.Equal(1, RecentVaults.Load(KeypasteHome.RecentPath(_home)).Single().HardwareKeySlot);

        // The reopened vault still saves, asking the key again.
        session.Unlocked!.AddEntry(new VaultEntry { Title = "after", Password = "saved" });
        session.Unlocked.Save();
        Assert.Equal(2, _device.Challenges.Count);
        session.Lock(VaultLockReason.Manual);

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(_vaultPath, _master));
        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 1);
        using var reopened = Vault.Open(_vaultPath, _master, null, key);
        Assert.Equal("saved", reopened.Find("after")?.Password);
    }

    [Fact]
    public async Task A_yubikey_removed_in_settings_leaves_the_password_alone_opening_the_vault()
    {
        using (var start = Unlocked(slot: null))
        using (var adding = Settings(start))
        {
            Enter(adding.Access, _master);
            adding.Access.AttachHardwareKey = true;
            adding.Access.ReviewCommand.Execute(null);
            await adding.Access.ChangeAsync();
            Assert.StartsWith("Changed.", adding.Access.Message, StringComparison.Ordinal);
        }

        using var session = Unlocked(slot: 2);
        using var settings = Settings(session);
        var access = settings.Access;
        Assert.True(access.HasHardwareKey);

        Enter(access, _master);
        access.RemoveHardwareKey = true;
        access.ReviewCommand.Execute(null);
        Assert.Equal("Afterwards the vault opens with its master password.", access.Summary);

        await access.ChangeAsync();

        Assert.StartsWith("Changed.", access.Message, StringComparison.Ordinal);
        Assert.Null(session.HardwareKeySlot);
        Assert.Null(RecentVaults.Load(KeypasteHome.RecentPath(_home)).Single().HardwareKeySlot);
        session.Lock(VaultLockReason.Manual);

        using var opened = Vault.Open(_vaultPath, _master);
        Assert.Null(opened.HardwareKey);
    }

    [Fact]
    public async Task A_yubikey_that_is_not_plugged_in_is_not_added_and_nothing_is_written()
    {
        using var session = Unlocked(slot: null);
        using var settings = Settings(session);
        var access = settings.Access;
        var before = Snapshot();

        _device.Connected = false;
        Enter(access, _master);
        access.AttachHardwareKey = true;
        access.ReviewCommand.Execute(null);
        await access.ChangeAsync();

        Assert.StartsWith("No YubiKey with slot 2 programmed is plugged in.", access.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
        Assert.Null(session.HardwareKeySlot);
    }

    private AppVaultSession Unlocked(int? slot)
    {
        var session = new AppVaultSession(new ManualClock(), home: _home, hardwareKeys: _device);

        using var master = TempVault.Secret(_master);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value, null, slot));
        RecentVaults.Save(
            KeypasteHome.RecentPath(_home), [new RecentVault(_vaultPath, DateTimeOffset.UtcNow, null, slot)]);

        return session;
    }

    private SettingsViewModel Settings(AppVaultSession session) =>
        new(session, _home, new DesktopPreferences(_home), _ => { }, _picker);

    private static void Enter(VaultAccessViewModel access, string current)
    {
        foreach (var c in current)
        {
            access.TypeCurrent(c);
        }
    }

    private string Snapshot() =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_vaultPath))) + " " +
        string.Join(",", VaultBackups.List(_vaultPath).Select(backup => Path.GetFileName(backup.Path)));
}
