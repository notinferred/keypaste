using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Login;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Settings › Startup: staying in the menu bar or tray, and opening at login (G.4a).</summary>
public sealed class StartupSettingsScreenTests
{
    [Fact]
    public void The_tray_is_on_by_default_except_on_Linux_where_it_is_opt_in()
    {
        using var fixture = new TempVault();

        Assert.Equal(!OperatingSystem.IsLinux(), new DesktopPreferences(fixture.Home).StaysInTray);
    }

    [Fact]
    public void Choosing_the_tray_survives_a_restart_and_reaches_what_follows_it_at_once()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());
        var preferences = new DesktopPreferences(fixture.Home);
        var changes = 0;
        preferences.Changed += (_, _) => changes++;

        using (var screen = new SettingsViewModel(session, fixture.Home, preferences, _ => { }))
        {
            screen.StayInTray = !preferences.StaysInTray;
        }

        var chosen = preferences.StaysInTray;
        Assert.Equal(1, changes);
        Assert.Equal(chosen, new DesktopPreferences(fixture.Home).StaysInTray);
        Assert.Equal(chosen, AppSettings.Load(Core.Audit.KeypasteHome.SettingsPath(fixture.Home)).StayInTray);
    }

    [Fact]
    public void Open_at_login_writes_and_removes_the_platform_entry()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());
        var login = new FakeLoginItem();
        using var screen = new SettingsViewModel(session, fixture.Home, new DesktopPreferences(fixture.Home), _ => { }) { Login = login };

        Assert.True(screen.OpenAtLoginSupported);
        Assert.False(screen.OpenAtLogin);

        screen.OpenAtLogin = true;
        Assert.True(login.IsEnabled);
        Assert.True(screen.OpenAtLogin);

        screen.OpenAtLogin = false;
        Assert.False(login.IsEnabled);
        Assert.False(screen.OpenAtLogin);
        Assert.Equal(string.Empty, screen.Message);
    }

    [Fact]
    public void An_entry_that_cannot_be_written_stays_off_and_says_so()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = new SettingsViewModel(session, fixture.Home, new DesktopPreferences(fixture.Home), _ => { })
        {
            Login = new FakeLoginItem { Refuses = true },
        };

        screen.OpenAtLogin = true;

        Assert.False(screen.OpenAtLogin);
        Assert.Contains("could not add", screen.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_login_mechanism_the_row_is_not_offered()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = new SettingsViewModel(session, fixture.Home, new DesktopPreferences(fixture.Home), _ => { });

        Assert.False(screen.OpenAtLoginSupported);
        screen.OpenAtLogin = true;
        Assert.False(screen.OpenAtLogin);
    }

    private sealed class FakeLoginItem : ILoginItem
    {
        internal bool Refuses { get; init; }

        public bool IsEnabled { get; private set; }

        public bool Enable()
        {
            IsEnabled = !Refuses;
            return !Refuses;
        }

        public bool Disable()
        {
            IsEnabled = false;
            return true;
        }
    }
}
