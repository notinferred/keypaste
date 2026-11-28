using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An unlock the browser's Unlock… asked for puts the browser back in front once that vault opens, so the person picks there; an
/// unlock the person came to keypaste for keeps the window in front.
/// </summary>
/// <remarks>The lifetime is attached as <see cref="ClosingTheMainWindowTests"/> attaches it, and the app is disposed rather than quit.</remarks>
public sealed class BrowserHandBackTests
{
    [Fact]
    public Task An_unlock_the_browser_asked_for_hides_keypaste_and_the_persons_own_unlock_keeps_it_in_front() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = ClosingTheMainWindowTests.Vault(fixture);
            var hidden = 0;

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            ClosingTheMainWindowTests.Attach(lifetime);
            using var app = new App { Preferences = ClosingTheMainWindowTests.Tray(fixture, stays: true), QuickUnlockKeys = () => null, HideApp = () => hidden++ };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var main = lifetime.MainWindow!;
            main.Show();

            await UnlockOnScreenAsync(main, vault);
            await ClosingTheMainWindowTests.Until(() => Root(main) is ShellView);
            Assert.Equal(0, hidden);

            await LockAsync(app, main);
            app.Vaults!.Pairings.Show!(BrowserShow.Unlock, "agents", NewLoginPage.None);
            AskedShown(main, vault);
            await UnlockOnScreenAsync(main, vault, offer: false);
            await ClosingTheMainWindowTests.Until(() => hidden == 1);
            Assert.True(app.Authority!.Session.IsUnlocked);

            // Brought forward by the person after the browser's ask, the window is theirs again.
            await LockAsync(app, main);
            app.Vaults.Pairings.Show!(BrowserShow.Unlock, "agents", NewLoginPage.None);
            AskedShown(main, vault);
            app.OpenWindowAtPersonsAct();
            await UnlockOnScreenAsync(main, vault, offer: false);
            await ClosingTheMainWindowTests.Until(() => Root(main) is ShellView);
            Drain();
            Assert.Equal(1, hidden);
        });

    [Fact]
    public Task A_quick_unlock_the_browser_asked_for_closes_the_window_into_the_tray_where_the_app_has_no_hide_and_keeps_the_vault_unlocked() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = ClosingTheMainWindowTests.Vault(fixture);
            var keys = new FakeQuickUnlockKeys(MachineQuickUnlock.TouchIdName);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            ClosingTheMainWindowTests.Attach(lifetime);
            using var app = new App { Preferences = ClosingTheMainWindowTests.Tray(fixture, stays: true), QuickUnlockKeys = () => keys };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var main = lifetime.MainWindow!;
            main.Show();

            await UnlockOnScreenAsync(main, vault);
            await ClosingTheMainWindowTests.Until(() => Root(main) is ShellView);
            Assert.Equal(QuickUnlockOutcome.Done, app.Authority!.Session.KeepForQuickUnlock());
            await LockAsync(app, main);
            app.Vaults!.Pairings.Show!(BrowserShow.Unlock, "agents", NewLoginPage.None);

            await ClosingTheMainWindowTests.Until(() => app.Authority.Session.IsUnlocked);
            await ClosingTheMainWindowTests.Until(() => !main.IsVisible);
            Assert.True(app.Authority!.Session.IsUnlocked);
            Assert.True(app.Tray!.IsShown);
        });

    [Fact]
    public Task A_vault_the_browser_asked_to_unlock_beside_an_open_one_hides_keypaste_once_its_own_form_opens_it() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var acmePath = Path.Combine(fixture.Home, "acme.kdbx");

            using (var created = ClaimedVault.Create(acmePath, TempVault.Password))
            {
                created.Save();
            }

            var hidden = 0;
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            ClosingTheMainWindowTests.Attach(lifetime);
            using var app = new App { Preferences = new DesktopPreferences(fixture.Home), QuickUnlockKeys = () => null, HideApp = () => hidden++ };
            app.Launch(lifetime);
            var main = lifetime.MainWindow!;
            main.Show();

            await UnlockOnScreenAsync(main, fixture.Path_);
            await ClosingTheMainWindowTests.Until(() => Root(main) is ShellView);
            var vaults = app.Vaults!;
            var acme = vaults.Add(acmePath);

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, acme.Session.TryUnlock(acmePath, master.Value));
            }

            vaults.LockAlone(acme);
            Drain();
            Assert.Equal(0, hidden);

            vaults.Pairings.Show!(BrowserShow.Unlock, "acme", NewLoginPage.None);
            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(Root(main)).DataContext);
            Drain();
            var form = Assert.IsType<LockedVaultViewModel>(shell.Content).Unlock;
            Assert.Equal("acme", form.Heading);
            Type(form);
            await form.UnlockAsync();

            await ClosingTheMainWindowTests.Until(() => hidden == 1);
            Assert.True(acme.Session.IsUnlocked);
        });

    private static async Task UnlockOnScreenAsync(Window main, string vault, bool offer = true)
    {
        var unlock = UnlockScreen(main);

        if (offer)
        {
            Assert.True(unlock.Offer(vault));
        }

        Type(unlock);
        await unlock.UnlockAsync();
    }

    /// <summary>Runs the browser's posted ask, so what the test does next comes after it, and checks it offered the vault.</summary>
    private static void AskedShown(Window main, string vault)
    {
        Drain();
        Assert.Equal(vault, UnlockScreen(main).SelectedPath);
    }

    private static void Type(UnlockViewModel unlock)
    {
        foreach (var c in TempVault.Password)
        {
            unlock.Type(c);
        }
    }

    private static async Task LockAsync(App app, Window main)
    {
        await app.Vaults!.LockAllAsync(VaultLockReason.Manual);
        await ClosingTheMainWindowTests.Until(() => Root(main) is UnlockView);
    }

    private static UnlockViewModel UnlockScreen(Window main) =>
        Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(Root(main)).DataContext);

    private static object? Root(Window main) => main.FindControl<ContentControl>("Root")!.Content;

    private static void Drain() => Rendering.WindowInput.Drain();
}
