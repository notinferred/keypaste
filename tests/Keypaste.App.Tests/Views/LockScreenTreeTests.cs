using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// What a screen reader finds on the welcome and the lock screen (V-N.2): KeePassXC's databases by
/// name, no word about agents, and the YubiKey only under More options for a vault without a slot.
/// </summary>
public sealed class LockScreenTreeTests
{
    [Fact]
    public Task The_welcome_lists_KeePassXCs_databases_and_names_no_agent() => HeadlessSession.On(() =>
    {
        using var home = new TempHome();
        using var files = new TempHome();
        var vault = Path.Combine(files.Path, "work.kdbx");
        using (var created = Vault.Create(vault, TempVault.Password))
        {
            created.Save();
        }

        var ini = Path.Combine(files.Path, "keepassxc.ini");
        File.WriteAllText(ini, $"[General]\nLastDatabases={vault.Replace(@"\", @"\\", StringComparison.Ordinal)}\n");

        using var session = new AppVaultSession(new ManualClock(), home: home.Path);
        using var model = new UnlockViewModel(session, home.Path, new FakeVaultFilePicker(), () => { }, keePassXcConfig: ini);
        var window = Show(model);

        var names = Names(window);
        Assert.Contains("work.kdbx", names);
        Assert.Contains("Open another file…", names);
        Assert.Contains("Create a new vault…", names);
        Assert.DoesNotContain("Open a vault…", names);
        Assert.DoesNotContain(names, name => name.Contains("agent", StringComparison.OrdinalIgnoreCase));
    });

    [Fact]
    public Task A_vault_without_a_slot_keeps_the_YubiKey_under_More_options_and_the_screen_names_no_agent() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        using var session = new AppVaultSession(new ManualClock(), home: fixture.Home, hardwareKeys: new SoftwareYubiKey(new byte[20]));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });
        var window = Show(model);

        var names = Names(window);
        Assert.Contains("More options", names);
        Assert.DoesNotContain("Unlock with a YubiKey too", names);
        Assert.DoesNotContain(names, name => name.Contains("agent", StringComparison.OrdinalIgnoreCase));

        var more = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "More options");
        WindowInput.Reveal(window, more);
        WindowInput.Press(window, more);
        WindowInput.Release(window, more);

        names = Names(window);
        Assert.Contains("Unlock with a YubiKey too", names);
        Assert.DoesNotContain("More options", names);
    });

    [Fact]
    public Task A_vault_whose_recent_entry_records_a_slot_shows_its_YubiKey_directly() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        RecentVaults.Save(KeypasteHome.RecentPath(fixture.Home), [new RecentVault(fixture.Path_, DateTimeOffset.UtcNow, HardwareKeySlot: 2)]);

        using var session = new AppVaultSession(new ManualClock(), home: fixture.Home, hardwareKeys: new SoftwareYubiKey(new byte[20]));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });
        var window = Show(model);

        var names = Names(window);
        Assert.Contains("YubiKey, slot 2", names);
        Assert.Contains("Unlock without a YubiKey", names);
        Assert.DoesNotContain("More options", names);
    });

    private static Window Show(UnlockViewModel model)
    {
        var window = new Window { Width = 800, Height = 900, Content = new UnlockView { DataContext = model } };
        window.Show();
        WindowInput.Drain();
        return window;
    }

    private static List<string> Names(Window window)
    {
        WindowInput.Drain();
        return [.. Visible(ControlAutomationPeer.CreatePeerForElement(window)).Select(peer => peer.GetName() ?? string.Empty)];
    }

    private static IEnumerable<AutomationPeer> Visible(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            if (child is ControlAutomationPeer { Owner.IsEffectivelyVisible: true })
            {
                yield return child;

                foreach (var descendant in Visible(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
