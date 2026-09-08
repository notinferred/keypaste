using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// What minimizing the window does, which until F.2b was nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>A real window, whose state really changes.</b> The setting round-tripped through
/// <c>app.toml</c> correctly for as long as it existed and was still a lie on the screen, because
/// no window state ever reached it — so a test that asserts the file is the test that already
/// passed against the defect (V-F.2b: "persistence-only tests cannot pass this task").
/// </para>
/// <para>
/// These run <see cref="App.Watch"/> and <see cref="App.Compose"/> themselves, for the reason
/// <c>StartupSettingsTests</c> gives: the missing wiring was in composition, so composition is what
/// has to run.
/// </para>
/// </remarks>
public sealed class MinimizeLockTests
{
    [Fact]
    public Task Minimizing_locks_the_vault_when_the_setting_is_on() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { LockWhenMinimized = true });

        using var app = new Armed(fixture, new ManualClock());
        app.Unlock(fixture);

        app.Minimize();

        Assert.False(app.Session.IsUnlocked);
        Assert.Equal(VaultLockReason.Minimized, app.Reason);
    });

    /// <summary>
    /// The other half of the claim: off means the ordinary idle policy, unchanged and still in
    /// force. A minimize-lock that also broke idle locking would be a worse defect than the one it
    /// repaired.
    /// </summary>
    [Fact]
    public Task Minimizing_leaves_the_vault_alone_when_the_setting_is_off() => Started(fixture =>
    {
        var clock = new ManualClock();
        using var app = new Armed(fixture, clock);
        app.Unlock(fixture);

        app.Minimize();

        Assert.True(app.Session.IsUnlocked);
        Assert.Null(app.Reason);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(app.Session.IsUnlocked);
        Assert.Equal(VaultLockReason.Idle, app.Reason);
    });

    /// <summary>
    /// "Repeat after restart": a fresh <see cref="DesktopPreferences"/>, a fresh session and a
    /// fresh watch, reading nothing but the file.
    /// </summary>
    [Fact]
    public Task A_saved_choice_is_in_force_at_the_next_launch() => Started(fixture =>
    {
        using (var first = new Armed(fixture, new ManualClock()))
        {
            Screen(first, fixture).LockWhenMinimized = true;
        }

        using var restarted = new Armed(fixture, new ManualClock());
        restarted.Unlock(fixture);

        restarted.Minimize();

        Assert.False(restarted.Session.IsUnlocked);
        Assert.Equal(VaultLockReason.Minimized, restarted.Reason);
    });

    [Fact]
    public Task Turning_it_on_in_Settings_needs_no_restart() => Started(fixture =>
    {
        using var app = new Armed(fixture, new ManualClock());
        app.Unlock(fixture);

        app.Minimize();
        Assert.True(app.Session.IsUnlocked);

        Screen(app, fixture).LockWhenMinimized = true;
        app.Restore();
        app.Minimize();

        Assert.False(app.Session.IsUnlocked);
        Assert.Equal(VaultLockReason.Minimized, app.Reason);
    });

    [Fact]
    public Task Turning_it_off_in_Settings_stops_it_at_once() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { LockWhenMinimized = true });

        using var app = new Armed(fixture, new ManualClock());
        app.Unlock(fixture);

        Screen(app, fixture).LockWhenMinimized = false;
        app.Minimize();

        Assert.True(app.Session.IsUnlocked);
        Assert.Null(app.Reason);
    });

    /// <summary>
    /// Restoring shows the locked screen. The window survives a lock — only its content is
    /// replaced — so coming back must not be mistaken for coming back unlocked.
    /// </summary>
    [Fact]
    public Task Restoring_leaves_the_vault_locked() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { LockWhenMinimized = true });

        using var app = new Armed(fixture, new ManualClock());
        app.Unlock(fixture);

        app.Minimize();
        app.Restore();

        Assert.False(app.Session.IsUnlocked);
        Assert.Null(app.Session.Unlocked);
        Assert.Equal(1, app.Locks);
    });

    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.FullScreen)]
    public Task No_other_window_state_locks(WindowState state) => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { LockWhenMinimized = true });

        using var app = new Armed(fixture, new ManualClock());
        app.Unlock(fixture);

        app.MoveTo(state);

        Assert.True(app.Session.IsUnlocked);
        Assert.Equal(0, app.Locks);
    });

    [Fact]
    public Task Minimizing_a_locked_session_raises_nothing() => Started(fixture =>
    {
        Save(fixture, AppSettings.Default with { LockWhenMinimized = true });

        using var app = new Armed(fixture, new ManualClock());

        app.Minimize();

        Assert.Equal(0, app.Locks);
    });

    /// <summary>
    /// The checkbox is offered exactly where a minimize can be observed, and both answers come from
    /// one place — an inactive security setting is the defect this task repairs, and a second
    /// opinion about where it works would rebuild it.
    /// </summary>
    [Fact]
    public Task The_checkbox_is_offered_exactly_where_minimizing_is_observed() => Started(fixture =>
    {
        using var app = new Armed(fixture, new ManualClock());
        var screen = Screen(app, fixture);

        var window = new Window { Content = new SettingsView { DataContext = screen } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single();

        Assert.Equal(MinimizeLock.IsSupported, screen.MinimizeLockSupported);
        Assert.Equal(MinimizeLock.IsSupported, checkbox.IsEffectivelyVisible);

        window.Close();
    });

    /// <summary>Every desktop target keypaste advertises reports a native minimize.</summary>
    [Fact]
    public void Windows_macOS_and_Linux_all_support_it() =>
        Assert.Equal(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux(),
            MinimizeLock.IsSupported);

    private static App Current => (App)Application.Current!;

    private static SettingsViewModel Screen(Armed app, TempVault fixture) =>
        new(app.Session, fixture.Home, app.Preferences, _ => { });

    private static void Save(TempVault fixture, AppSettings settings) =>
        AppSettings.Save(KeypasteHome.SettingsPath(fixture.Home), settings);

    /// <summary>Runs <paramref name="body"/> on the headless session's UI thread.</summary>
    private static Task Started(Action<TempVault> body) => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();

        try
        {
            body(fixture);
        }
        finally
        {
            // The application object is one per assembly; a test that left a theme behind would be
            // changing what another one renders in.
            Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    });

    /// <summary>
    /// A composed application: the preferences on disk, the session they armed, a real window and
    /// the watch that joins them — built by the methods launch builds them with.
    /// </summary>
    private sealed class Armed : IDisposable
    {
        private readonly MinimizeLock _watch;
        private readonly Window _window;

        internal Armed(TempVault fixture, TimeProvider clock)
        {
            Preferences = new DesktopPreferences(fixture.Home);
            Session = Current.Compose(Preferences, clock);

            _window = new Window();
            _window.Show();

            _watch = App.Watch(_window, Preferences, Session);

            Session.Locked += (_, reason) =>
            {
                Reason = reason;
                Locks++;
            };
        }

        internal DesktopPreferences Preferences { get; }

        internal AppVaultSession Session { get; }

        /// <summary>Why the vault last locked, or null if it has not.</summary>
        internal VaultLockReason? Reason { get; private set; }

        /// <summary>How many times it has locked.</summary>
        internal int Locks { get; private set; }

        internal void Unlock(TempVault fixture)
        {
            using var master = TempVault.Secret(TempVault.Password);
            Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(fixture.Path_, master.Value));
        }

        internal void Minimize() => MoveTo(WindowState.Minimized);

        internal void Restore() => MoveTo(WindowState.Normal);

        internal void MoveTo(WindowState state)
        {
            _window.WindowState = state;
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            _watch.Dispose();
            Session.Dispose();
            _window.Close();
        }
    }
}
