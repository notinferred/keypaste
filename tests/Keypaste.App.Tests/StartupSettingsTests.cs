using Avalonia;
using Avalonia.Styling;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// What launch does with <c>app.toml</c>: the timeout you chose is the one that locks you out, and
/// the theme you chose is the one the first frame is painted in.
/// </summary>
/// <remarks>
/// <para>
/// <b>These run <see cref="App.Compose"/> itself</b> rather than a test-local re-implementation of
/// it. That is the point: the defect F.2a repairs was not in <c>AppSettings</c>, which round-tripped
/// correctly and was well covered, nor in <c>AppVaultSession</c>, which honoured whatever timeout it
/// was handed. It was that composition never introduced the two. A test that composes a session by
/// hand would have passed against the broken app.
/// </para>
/// <para>
/// <b>"A fresh application" is a fresh <see cref="DesktopPreferences"/> and a fresh session, not a
/// fresh process.</b> The headless session is one per process and <c>HeadlessSession</c> says why,
/// so the seam is the composition method. What that still proves is the whole of the claim: nothing
/// carried over in memory from the run that saved the setting.
/// </para>
/// </remarks>
public sealed class StartupSettingsTests
{
    [Fact]
    public Task A_saved_timeout_is_the_one_a_fresh_session_locks_on() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { IdleTimeoutSeconds = 60 });

        using var session = Compose(fixture, new ManualClock());

        Assert.Equal(TimeSpan.FromMinutes(1), session.IdleTimeout);
    });

    /// <summary>
    /// The property agreeing is not the claim. A one-minute fixture has to actually lock at one
    /// minute, with a real vault open and the clock moved past it.
    /// </summary>
    [Fact]
    public Task A_one_minute_fixture_locks_at_one_minute() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { IdleTimeoutSeconds = 60 });

        var clock = new ManualClock();
        using var session = Compose(fixture, clock);

        VaultLockReason? reason = null;
        session.Locked += (_, r) => reason = r;

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True(session.IsUnlocked);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(session.IsUnlocked);
        Assert.Equal(VaultLockReason.Idle, reason);
    });

    [Fact]
    public Task A_saved_theme_is_painted_without_opening_Settings() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { Theme = AppTheme.Dark });

        using var session = Compose(fixture, new ManualClock());

        Assert.Equal(ThemeVariant.Dark, Current.RequestedThemeVariant);
    });

    [Fact]
    public Task A_theme_of_System_hands_the_decision_back_to_the_operating_system() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { Theme = AppTheme.System });

        using var session = Compose(fixture, new ManualClock());

        Assert.Equal(ThemeVariant.Default, Current.RequestedThemeVariant);
    });

    /// <summary>
    /// The whole of V-F.2a in one place: save preferences, throw the session away, compose a new
    /// one from the file alone, and the effective timeout, the displayed choice and the applied
    /// theme all agree without anyone visiting Settings again.
    /// </summary>
    [Fact]
    public Task A_fresh_session_needs_no_second_visit_to_Settings() => Started(fixture =>
    {
        var chosen = new DesktopPreferences(fixture.Home);

        using (var opening = Compose(chosen, new ManualClock()))
        {
            var screen = new SettingsViewModel(opening, fixture.Home, chosen, Current.ApplyTheme);
            screen.Idle = screen.IdleChoices.Single(c => c.Seconds == 3600);
            screen.Theme = AppTheme.Light;
        }

        var restarted = new DesktopPreferences(fixture.Home);
        using var session = Compose(restarted, new ManualClock());
        var after = new SettingsViewModel(session, fixture.Home, restarted, _ => { });

        Assert.Equal(TimeSpan.FromHours(1), session.IdleTimeout);
        Assert.Equal(3600, after.Idle.Seconds);
        Assert.Equal(ThemeVariant.Light, Current.RequestedThemeVariant);
        Assert.Equal(AppTheme.Light, after.Theme);
    });

    /// <summary>
    /// A file keypaste cannot read costs a preference and never costs a lock — and never costs the
    /// file either. Rewriting it with the defaults would destroy a hand edit at the moment its
    /// author was most likely part-way through making it (D-0028).
    /// </summary>
    [Fact]
    public Task Unreadable_preferences_take_the_defaults_and_leave_the_file_alone() => Started(fixture =>
    {
        var path = KeypasteHome.SettingsPath(fixture.Home);
        var original = "[[settings]]\nnot a pair\n"u8.ToArray();
        File.WriteAllBytes(path, original);

        using (var session = Compose(fixture, new ManualClock()))
        {
            Assert.Equal(TimeSpan.FromSeconds(AppSettings.Default.IdleTimeoutSeconds), session.IdleTimeout);
            Assert.Equal(ThemeVariant.Default, Current.RequestedThemeVariant);
        }

        Assert.Equal(original, File.ReadAllBytes(path));
    });

    [Fact]
    public Task A_missing_settings_file_is_not_created_by_starting_up() => Started(fixture =>
    {
        var path = KeypasteHome.SettingsPath(fixture.Home);
        Assert.False(File.Exists(path));

        using var session = Compose(fixture, new ManualClock());

        Assert.Equal(TimeSpan.FromMinutes(5), session.IdleTimeout);
        Assert.False(File.Exists(path));
    });

    private static App Current => (App)Application.Current!;

    private static AppVaultSession Compose(TempVault fixture, TimeProvider clock) =>
        Compose(new DesktopPreferences(fixture.Home), clock);

    private static AppVaultSession Compose(DesktopPreferences preferences, TimeProvider clock) =>
        Current.Compose(preferences, clock);

    private static void Save(TempVault fixture, AppSettings settings) =>
        AppSettings.Save(KeypasteHome.SettingsPath(fixture.Home), settings);

    /// <summary>
    /// Runs <paramref name="body"/> on the headless session's UI thread with its own temporary
    /// home, and puts the shared application's theme back afterwards — it is one object for the
    /// whole assembly, and a test that left it Dark would be changing what another test renders in.
    /// </summary>
    private static Task Started(Action<TempVault> body) => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();

        try
        {
            body(fixture);
        }
        finally
        {
            Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    });
}
