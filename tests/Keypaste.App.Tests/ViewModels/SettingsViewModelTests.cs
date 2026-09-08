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
        var model = Screen(session, fixture);

        model.Idle = model.IdleChoices.Single(c => c.Seconds == 60);

        Assert.Equal(TimeSpan.FromMinutes(1), session.IdleTimeout);
    }

    /// <summary>
    /// A restart is a fresh read of the file and a fresh session armed from it — not a second view
    /// model over the same run. The old version of this test built one, which is why it passed
    /// throughout the whole time launch was ignoring the file (F.2a).
    /// </summary>
    [Fact]
    public void A_choice_survives_a_restart()
    {
        using var fixture = new TempVault();
        using var first = new AppVaultSession(new ManualClock());

        var screen = Screen(first, fixture);
        screen.Idle = screen.IdleChoices.Single(c => c.Seconds == 900);
        screen.Theme = AppTheme.Dark;
        screen.LockWhenMinimized = true;

        var restarted = new DesktopPreferences(fixture.Home);
        using var second = new AppVaultSession(new ManualClock(), restarted.IdleTimeout);
        var after = new SettingsViewModel(second, fixture.Home, restarted, _ => { });

        Assert.Equal(TimeSpan.FromMinutes(15), second.IdleTimeout);
        Assert.Equal(900, after.Idle.Seconds);
        Assert.Equal(AppTheme.Dark, after.Theme);
        Assert.True(after.LockWhenMinimized);
    }

    [Fact]
    public void Changing_the_theme_reaches_the_application()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());

        AppTheme? applied = null;
        var model = new SettingsViewModel(
            session,
            fixture.Home,
            new DesktopPreferences(fixture.Home),
            theme => applied = theme);

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
        Assert.NotEmpty(SettingsViewModel.Offered);

        foreach (var choice in SettingsViewModel.Offered)
        {
            Assert.InRange(
                choice.Seconds,
                AppSettings.MinimumIdleTimeoutSeconds,
                AppSettings.MaximumIdleTimeoutSeconds);
        }
    }

    /// <summary>
    /// A number the list does not offer is named rather than rounded off, because the alternative
    /// is a screen showing a timeout the vault is not using — which is the whole of F.2a.
    /// </summary>
    [Fact]
    public void A_timeout_the_list_does_not_offer_is_shown_as_the_one_in_force()
    {
        using var fixture = new TempVault();
        Written(fixture, seconds: 137);

        var preferences = new DesktopPreferences(fixture.Home);
        using var session = new AppVaultSession(new ManualClock(), preferences.IdleTimeout);
        var model = new SettingsViewModel(session, fixture.Home, preferences, _ => { });

        Assert.Equal(TimeSpan.FromSeconds(137), session.IdleTimeout);
        Assert.Equal(137, model.Idle.Seconds);
        Assert.Equal("2 minutes 17 seconds", model.Idle.Label);
        Assert.Equal(SettingsViewModel.Offered.Count + 1, model.IdleChoices.Count);
    }

    [Fact]
    public void An_offered_timeout_adds_nothing_to_the_list()
    {
        using var fixture = new TempVault();
        Written(fixture, seconds: 1800);

        var preferences = new DesktopPreferences(fixture.Home);
        using var session = new AppVaultSession(new ManualClock(), preferences.IdleTimeout);
        var model = new SettingsViewModel(session, fixture.Home, preferences, _ => { });

        Assert.Equal("30 minutes", model.Idle.Label);
        Assert.Equal(SettingsViewModel.Offered.Count, model.IdleChoices.Count);
    }

    [Fact]
    public void Forgetting_them_all_empties_the_recent_list()
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        Assert.NotEmpty(RecentVaults.Load(KeypasteHome.RecentPath(fixture.Home)));

        using var session = new AppVaultSession(new ManualClock());
        var model = Screen(session, fixture);

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
        var model = Screen(session, fixture);

        Assert.InRange(
            model.Idle.Seconds,
            AppSettings.MinimumIdleTimeoutSeconds,
            AppSettings.MaximumIdleTimeoutSeconds);
    }

    private static SettingsViewModel Screen(AppVaultSession session, TempVault fixture) =>
        new(session, fixture.Home, new DesktopPreferences(fixture.Home), _ => { });

    private static void Written(TempVault fixture, int seconds) =>
        AppSettings.Save(
            KeypasteHome.SettingsPath(fixture.Home),
            AppSettings.Default with { IdleTimeoutSeconds = seconds });
}
