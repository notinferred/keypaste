using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Settings apply at once, survive a restart, and cannot produce a vault that never locks.
/// </summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public void Choosing_a_timeout_re_arms_the_session_immediately()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(), TimeSpan.FromHours(4));
        var model = new SettingsViewModel(session, fixture.Home, _ => { });

        model.Idle = SettingsViewModel.IdleChoices.Single(c => c.Seconds == 60);

        Assert.Equal(TimeSpan.FromMinutes(1), session.IdleTimeout);
    }

    [Fact]
    public void A_choice_survives_a_restart()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        var first = new SettingsViewModel(session, fixture.Home, _ => { });
        first.Idle = SettingsViewModel.IdleChoices.Single(c => c.Seconds == 900);
        first.Theme = AppTheme.Dark;
        first.LockWhenMinimized = true;

        var second = new SettingsViewModel(session, fixture.Home, _ => { });

        Assert.Equal(900, second.Idle.Seconds);
        Assert.Equal(AppTheme.Dark, second.Theme);
        Assert.True(second.LockWhenMinimized);
    }

    [Fact]
    public void Changing_the_theme_reaches_the_application()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        AppTheme? applied = null;
        var model = new SettingsViewModel(session, fixture.Home, theme => applied = theme);

        model.Theme = AppTheme.Light;

        Assert.Equal(AppTheme.Light, applied);
    }

    /// <summary>
    /// There is deliberately no "never" — it would be the one setting everybody chose the first
    /// time the countdown interrupted them, and it would turn off the feature 4.1 exists to ship.
    /// </summary>
    [Fact]
    public void Every_offered_timeout_actually_locks()
    {
        Assert.NotEmpty(SettingsViewModel.IdleChoices);

        foreach (var choice in SettingsViewModel.IdleChoices)
        {
            Assert.InRange(
                choice.Seconds,
                AppSettings.MinimumIdleTimeoutSeconds,
                AppSettings.MaximumIdleTimeoutSeconds);
        }
    }

    [Fact]
    public void Forgetting_them_all_empties_the_recent_list()
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        Assert.NotEmpty(RecentVaults.Load(KeypasteHome.RecentPath(fixture.Home)));

        using var session = new AppVaultSession(new ManualClock());
        var model = new SettingsViewModel(session, fixture.Home, _ => { });

        model.ForgetAllCommand.Execute(null);

        Assert.Empty(RecentVaults.Load(KeypasteHome.RecentPath(fixture.Home)));
        Assert.True(model.HasMessage);
    }

    /// <summary>
    /// An unreadable settings file costs a preference, never a lock — the session still gets a
    /// timeout that locks, because the default already does.
    /// </summary>
    [Fact]
    public void A_malformed_settings_file_still_yields_a_timeout_that_locks()
    {
        using var fixture = new TempVault();
        File.WriteAllText(KeypasteHome.SettingsPath(fixture.Home), "[[settings]]\nnot a pair\n");

        using var session = new AppVaultSession(new ManualClock());
        var model = new SettingsViewModel(session, fixture.Home, _ => { });

        Assert.InRange(
            model.Idle.Seconds,
            AppSettings.MinimumIdleTimeoutSeconds,
            AppSettings.MaximumIdleTimeoutSeconds);
    }
}
