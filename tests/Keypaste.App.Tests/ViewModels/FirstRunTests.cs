using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The first run offers the databases KeePassXC last opened, and the lock screen keeps the YubiKey
/// under More options unless the vault's recent entry records a slot (N.2).
/// </summary>
public sealed class FirstRunTests : IDisposable
{
    private readonly TempHome _home = new();
    private readonly TempHome _files = new();
    private readonly AppVaultSession _session;
    private int _unlocked;

    public FirstRunTests() => _session = new AppVaultSession(new ManualClock(), home: _home.Path);

    private string Ini => Path.Combine(_files.Path, "keepassxc.ini");

    public void Dispose()
    {
        _session.Dispose();
        _home.Dispose();
        _files.Dispose();
    }

    [Fact]
    public async Task With_no_recent_vault_the_welcome_offers_KeePassXCs_databases_and_choosing_one_unlocks_it()
    {
        var work = MakeVault("work.kdbx");
        var home = MakeVault("home.kdbx");
        File.WriteAllText(
            Ini,
            $"[General]\r\nLastActiveDatabase={home.Replace('\\', '/')}\r\nLastOpenedDatabases={Qt(work)}, {Qt(home)}\r\nLastDatabases={Qt(work)}, {Qt(home)}\r\n");
        var before = File.ReadAllBytes(Ini);

        using var model = NewModel(Ini);

        Assert.False(model.HasSelection);
        Assert.True(model.OffersKeePassXc);
        Assert.False(model.OffersOpenFirst);
        Assert.Equal([home, work], model.KeePassXc.Select(item => item.Path));
        Assert.Equal(["home.kdbx", "work.kdbx"], model.KeePassXc.Select(item => item.ToString()));

        model.SelectedKeePassXc = model.KeePassXc[0];

        Assert.Equal(home, model.SelectedPath);
        Assert.False(model.OffersKeePassXc);
        Type(model, TempVault.Password);
        await model.UnlockAsync();

        Assert.Equal(1, _unlocked);
        Assert.Equal(home, Assert.Single(RecentVaults.Load(KeypasteHome.RecentPath(_home.Path))).Path);
        Assert.Equal(before, File.ReadAllBytes(Ini));
    }

    [Fact]
    public void Without_KeePassXCs_settings_the_welcome_opens_a_file_or_creates_a_vault()
    {
        using var model = NewModel(Ini);

        Assert.False(model.OffersKeePassXc);
        Assert.True(model.OffersOpenFirst);
        Assert.Empty(model.KeePassXc);
    }

    [Fact]
    public void A_recent_vault_is_selected_and_KeePassXC_is_not_read()
    {
        var remembered = MakeVault("remembered.kdbx");
        RecentVaults.Save(KeypasteHome.RecentPath(_home.Path), [new RecentVault(remembered, DateTimeOffset.UtcNow)]);
        File.WriteAllText(Ini, $"[General]\nLastDatabases={Qt(MakeVault("other.kdbx"))}\n");

        using var model = NewModel(Ini);

        Assert.Equal(remembered, model.SelectedPath);
        Assert.False(model.OffersKeePassXc);
        Assert.Empty(model.KeePassXc);
    }

    [Fact]
    public void A_database_that_is_not_a_vault_is_refused_where_it_is_chosen()
    {
        var imposter = Path.Combine(_files.Path, "imposter.kdbx");
        File.WriteAllText(imposter, "not a vault");
        File.WriteAllText(Ini, $"[General]\nLastDatabases={Qt(imposter)}\n");

        using var model = NewModel(Ini);
        model.SelectedKeePassXc = Assert.Single(model.KeePassXc);

        Assert.False(model.HasSelection);
        Assert.True(model.HasLooseError);
        Assert.Equal("That isn't a KeePass vault.", model.Message);
    }

    [Fact]
    public void Neither_the_welcome_nor_the_lock_screen_speaks_of_agents()
    {
        using var model = NewModel(Ini);
        Assert.DoesNotContain("agent", model.WelcomeSubtitle, StringComparison.OrdinalIgnoreCase);

        model.Offer(MakeVault("work.kdbx"));
        Assert.DoesNotContain("agent", model.Subtitle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_vault_with_no_slot_keeps_the_YubiKey_under_More_options_and_one_with_a_slot_shows_it()
    {
        using var keyed = new AppVaultSession(new ManualClock(), home: _home.Path, hardwareKeys: new SoftwareYubiKey(new byte[20]));
        var plain = MakeVault("plain.kdbx");
        var slotted = MakeVault("slotted.kdbx");
        RecentVaults.Save(
            KeypasteHome.RecentPath(_home.Path),
            [new RecentVault(slotted, DateTimeOffset.UtcNow, HardwareKeySlot: 1), new RecentVault(plain, DateTimeOffset.UtcNow)]);

        using var model = new UnlockViewModel(keyed, _home.Path, new FakeVaultFilePicker(), () => { });

        Assert.Equal(slotted, model.SelectedPath);
        Assert.True(model.ShowsHardwareKey);
        Assert.False(model.OffersMoreOptions);

        model.SelectedRecent = model.Recent.Single(item => item.Path == plain);
        Assert.False(model.ShowsHardwareKey);
        Assert.True(model.OffersMoreOptions);

        model.ShowMoreOptionsCommand.Execute(null);
        Assert.True(model.ShowsHardwareKey);
        Assert.False(model.OffersMoreOptions);

        model.SelectedRecent = model.Recent.Single(item => item.Path == slotted);
        model.SelectedRecent = model.Recent.Single(item => item.Path == plain);
        Assert.False(model.ShowsHardwareKey);
    }

    private UnlockViewModel NewModel(string? keePassXcConfig) =>
        new(_session, _home.Path, new FakeVaultFilePicker(), () => _unlocked++, keePassXcConfig: keePassXcConfig);

    private string MakeVault(string name)
    {
        var path = Path.Combine(_files.Path, name);
        using var vault = Vault.Create(path, TempVault.Password);
        vault.Save();
        return path;
    }

    /// <summary>A path as QSettings writes it, backslashes doubled.</summary>
    private static string Qt(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);

    private static void Type(UnlockViewModel model, string password)
    {
        model.ClearPassword();

        foreach (var c in password)
        {
            model.Type(c);
        }
    }
}
