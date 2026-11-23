using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.Notifications;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// With the app running locked in the menu bar or tray and holding its home, a bridge that finds it locked has it show
/// one notice and no window; only the notice's Unlock… shows the unlock screen, and the next notice waits for an
/// unlock and a lock.
/// </summary>
/// <remarks>
/// The app is composed by <see cref="App.Launch"/> at login into Avalonia's own lifetime, holding the claim
/// <c>Program.Run</c> takes, as in <see cref="SecondStartTests"/>, and the bridge's knock is
/// <see cref="LockedAppEndpoint.TellAsync"/>. The home is the process's own (<see cref="IsolatedHome"/>), which other
/// tests write to, so each test puts a vault of its own on the unlock screen and asks only for vaults of its own.
/// </remarks>
public sealed class LockedNoticeTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(500);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The home <see cref="App.Launch"/> composes the session from.</summary>
    private static string Home => KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));

    [Fact]
    public Task A_bridge_finding_the_app_locked_shows_one_notice_and_no_window() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            Assert.True(Screen(app).Offer(vault.Path_));

            Assert.True(await TellAsync(vault.Path_), "the bridge did not reach the locked app");
            var notice = await NoticeAsync(notices);

            Assert.Equal("test is locked", Text(notice, "HeadText"));
            Assert.Same(notice, app.LockedNotice!.Showing);
            Assert.False(app.Main!.IsVisible, "a bridge showed the main window");
            Assert.Null(lifetime.MainWindow);
            Assert.Equal(0, claim.Requests);

            Assert.True(await TellAsync(vault.Path_), "the app stopped hearing once it had told the person");
            await SettleAsync();
            Assert.Single(notices);
            Assert.False(app.Main.IsVisible);
            notice.Close();
        });

    [Fact]
    public async Task A_notice_built_before_the_display_is_up_still_shows_its_window()
    {
        using var home = new TempHome();
        using var vault = new TempVault();
        using var session = new AppVaultSession(TimeProvider.System, home: home.Path);
        using var locked = await Task.Run(() => new LockedNotice(session, () => { }, TimeProvider.System, _ => { }), Token);

        await HeadlessSession.On(async () =>
        {
            using var screen = new UnlockViewModel(session, home.Path, new FakeVaultFilePicker(), () => { });
            Assert.True(screen.Offer(vault.Path_));
            locked.Watch(screen);

            Assert.True(await LockedAppEndpoint.TellAsync(LockedAppEndpoint.NameFor(VaultIdentity.Of(home.Path, vault.Path_)), Token));
            await Until(() => locked.Showing is not null);
            locked.Showing!.Close();
        });
    }

    [Fact]
    public Task Only_a_press_of_Open_keypaste_shows_the_unlock_screen() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            Assert.True(Screen(app).Offer(vault.Path_));

            Assert.True(await TellAsync(vault.Path_));
            var notice = await NoticeAsync(notices);
            var open = notice.FindControl<Button>("Open")!;

            Click(notice, "Open");
            Assert.True(notice.IsVisible, "a press before Unlock… was armed closed the notice");
            Assert.False(app.Main!.IsVisible, "a press before Unlock… was armed showed the window");

            await Until(() => open.IsEffectivelyEnabled);
            Click(notice, "Open");

            await Until(() => !notice.IsVisible);
            Assert.True(app.Main.IsVisible, "Unlock… did not show the window");
            Assert.Same(app.Main, lifetime.MainWindow);
            Assert.Equal(vault.Path_, Screen(app).SelectedPath);
            Assert.Null(app.LockedNotice!.Showing);
            app.Main.Close();
        });

    [Fact]
    public Task A_closed_notice_holds_back_the_next_until_an_unlock_and_a_lock() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            Assert.True(Screen(app).Offer(vault.Path_));

            Assert.True(await TellAsync(vault.Path_));
            var first = await NoticeAsync(notices);
            Click(first, "Dismiss");
            await Until(() => !first.IsVisible);
            Assert.False(app.Main!.IsVisible, "Close showed the window");

            Assert.True(await TellAsync(vault.Path_), "a closed notice stopped the app hearing");
            await SettleAsync();
            Assert.Single(notices);

            Unlock(app, vault.Path_);
            WindowInput.Drain();
            Assert.False(await TellAsync(vault.Path_), "the app still answered with a vault open");

            var unlocked = Screen(app);
            app.Authority!.Session.Lock(VaultLockReason.Manual);
            await Until(() => !ReferenceEquals(Screen(app), unlocked));
            Assert.True(Screen(app).Offer(vault.Path_));

            Assert.True(await TellAsync(vault.Path_));
            var second = await NoticeAsync(notices, count: 2);
            Assert.NotSame(first, second);
            Assert.False(app.Main.IsVisible);
            second.Close();
        });

    [Fact]
    public Task The_app_listens_for_the_vault_its_unlock_screen_shows_and_the_chosen_vault() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var shown = new TempVault();
            using var other = new TempVault();
            var chosen = Path.Combine(other.Home, "chosen.kdbx");
            File.Copy(other.Path_, chosen);
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            using var choice = new Chosen(chosen);
            Assert.True(Screen(app).Offer(shown.Path_));

            Assert.True(await TellAsync(chosen), "the app did not listen for the chosen vault");
            var notice = await NoticeAsync(notices);
            Assert.Equal("chosen is locked", Text(notice, "HeadText"));

            Assert.True(await TellAsync(shown.Path_), "the app did not listen for the vault its unlock screen shows");
            await SettleAsync();
            Assert.Single(notices);

            await Until(() => notice.FindControl<Button>("Open")!.IsEffectivelyEnabled);
            Click(notice, "Open");

            await Until(() => app.Main!.IsVisible);
            Assert.Equal(chosen, Screen(app).SelectedPath);
            Assert.False(await TellAsync(shown.Path_), "the app still listened for a vault its unlock screen no longer shows");
            Assert.True(await TellAsync(chosen));
            app.Main!.Close();
        });

    [Fact]
    public Task A_vault_chosen_while_the_app_stays_locked_is_listened_for_in_place_of_the_one_before() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var shown = new TempVault();
            using var other = new TempVault();
            var before = Path.Combine(other.Home, "before.kdbx");
            var after = Path.Combine(other.Home, "after.kdbx");
            File.Copy(other.Path_, before);
            File.Copy(other.Path_, after);
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            using var choice = new Chosen(before);
            Assert.True(Screen(app).Offer(shown.Path_));

            using var change = new Chosen(after);

            await TellUntilHeardAsync(after);
            var notice = await NoticeAsync(notices);
            Assert.Equal("after is locked", Text(notice, "HeadText"));
            Assert.False(await TellAsync(before), "the app still listened for the vault chosen before");
            Assert.True(await TellAsync(shown.Path_), "the app stopped listening for the vault its unlock screen shows");
            Assert.False(app.Main!.IsVisible);
            notice.Close();
        });

    [Fact]
    public Task Deleting_app_toml_while_locked_ends_the_listening_for_the_vault_it_chose() =>
        WithChoice(watch: null, async (locked, home, chosen, _, _) =>
        {
            Assert.True(await TellNamedAsync(home, chosen), "the notice did not listen for the chosen vault");

            File.Delete(KeypasteHome.SettingsPath(home.Path));

            await HeardUntil(home, chosen, heard: false);
        });

    [Fact]
    public Task A_vault_chosen_where_app_toml_cannot_be_watched_is_listened_for_once_the_clock_moves() =>
        WithChoice((_, _) => throw new IOException("The configured user limit on the number of inotify watches was reached."), async (_, home, before, after, clock) =>
        {
            Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(home.Path, after, onlyIfNone: false));
            Assert.True(await TellNamedAsync(home, before), "the notice stopped listening for the chosen vault before the clock moved");

            clock.Advance(TimeSpan.FromSeconds(4));

            await HeardUntil(home, after, heard: true);
            Assert.False(await TellNamedAsync(home, before), "the notice still listened for the vault chosen before");
        });

    [Fact]
    public Task A_vault_chosen_after_the_watch_of_app_toml_fails_is_listened_for_once_the_clock_moves()
    {
        FailingWatcher? watcher = null;

        // It watches a folder app.toml is not in, so only the reading on the clock can follow the change.
        return WithChoice((_, file) => watcher = new FailingWatcher(Path.GetTempPath(), file), async (_, home, before, after, clock) =>
        {
            watcher!.Fail();
            WindowInput.Drain();
            Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(home.Path, after, onlyIfNone: false));

            clock.Advance(TimeSpan.FromSeconds(4));

            await HeardUntil(home, after, heard: true);
            Assert.False(await TellNamedAsync(home, before), "the notice still listened for the vault chosen before");
        });
    }

    [Fact]
    public Task Unlocking_takes_a_notice_still_up_down() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            Assert.True(Screen(app).Offer(vault.Path_));

            Assert.True(await TellAsync(vault.Path_));
            var notice = await NoticeAsync(notices);

            Unlock(app, vault.Path_);
            await Until(() => !notice.IsVisible);
            Assert.Null(app.LockedNotice!.Showing);
            Assert.False(await TellAsync(vault.Path_), "the app still answered with a vault open");

            app.Authority!.Session.Lock(VaultLockReason.Manual);
            WindowInput.Drain();
        });

    [Fact]
    public Task Quitting_stops_listening_and_takes_the_notice_down() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = new NoSystemNotifications() };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            Assert.True(Screen(app).Offer(vault.Path_));

            Assert.True(await TellAsync(vault.Path_));
            var notice = await NoticeAsync(notices);

            app.Tray!.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == AppTray.QuitHeader).Command!.Press();

            await Until(() => !notice.IsVisible);
            Assert.Null(app.LockedNotice);
            Assert.False(await TellAsync(vault.Path_), "a quitting app still heard the bridge");
        });

    [Fact]
    public Task A_bridge_finding_the_app_locked_posts_one_ordinary_notification_naming_no_vault_and_shows_nothing() =>
        WithNotifications(SystemAnswer.Accepts, async (app, lifetime, system, vault, notices) =>
        {
            Assert.True(await TellAsync(vault));
            await Until(() => system.Posts.Count == 1);

            var post = system.Posts[0];
            Assert.Same(SystemNotice.VaultLocked, post.Notice);
            Assert.False(post.Notice.TimeSensitive);
            Assert.Equal("An agent asked for a locked vault", post.Notice.Title);
            Assert.Equal("It was refused. Unlock the vault in keypaste, then ask the agent to try again.", post.Notice.Body);
            Assert.Equal("Open keypaste", post.Notice.Action);
            Assert.DoesNotContain("test", post.Notice.Title + post.Notice.Body + post.Notice.Kind, StringComparison.OrdinalIgnoreCase);

            await SettleAsync();
            Assert.Empty(notices);
            Assert.Null(app.LockedNotice!.Showing);
            Assert.False(app.Main!.IsVisible, "a bridge showed the main window");
            Assert.Null(lifetime.MainWindow);

            Assert.True(await TellAsync(vault), "the app stopped hearing once it had told the person");
            await SettleAsync();
            Assert.Single(system.Posts);
        });

    [Fact]
    public Task Open_keypaste_in_the_notification_shows_the_unlock_screen_on_the_vault_asked_about() =>
        WithNotifications(SystemAnswer.Accepts, async (app, lifetime, system, vault, _) =>
        {
            Assert.True(await TellAsync(vault));
            await Until(() => system.Posts.Count == 1);
            Assert.False(app.Main!.IsVisible);

            system.Posts[0].Pressed();
            await Until(() => app.Main.IsVisible);

            Assert.Same(app.Main, lifetime.MainWindow);
            Assert.Equal(vault, Screen(app).SelectedPath);
            Assert.True(system.Posts[0].Removed);
            app.Main.Close();
        });

    [Fact]
    public Task Pressing_the_notification_for_a_vault_quick_unlock_can_reopen_asks_for_it_at_once() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications { Answers = SystemAnswer.Accepts };
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            var keys = new FakeQuickUnlockKeys(MachineQuickUnlock.TouchIdName);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = system, QuickUnlockKeys = () => keys };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            Assert.True(Screen(app).Offer(vault.Path_));

            Unlock(app, vault.Path_);
            Assert.Equal(QuickUnlockOutcome.Done, app.Authority!.Session.KeepForQuickUnlock());
            WindowInput.Drain();
            var unlocked = Screen(app);
            app.Authority.Session.Lock(VaultLockReason.Manual);
            await Until(() => !ReferenceEquals(Screen(app), unlocked));
            Assert.True(Screen(app).Offer(vault.Path_));
            Assert.True(Screen(app).OffersQuickUnlock);

            Assert.True(await TellAsync(vault.Path_));
            await Until(() => system.Posts.Count == 1);
            Assert.Equal(1, keys.Asked);
            system.Posts[0].Pressed();

            await Until(() => app.Authority.Session.IsUnlocked);
            Assert.Equal(2, keys.Asked);
            Assert.True(app.Main!.IsVisible);
            app.Main.Close();
        });

    [Fact]
    public Task Unlocking_removes_the_notification_and_its_late_press_after_another_lock_opens_nothing() =>
        WithNotifications(SystemAnswer.Accepts, async (app, _, system, vault, _) =>
        {
            Assert.True(await TellAsync(vault));
            await Until(() => system.Posts.Count == 1);

            Unlock(app, vault);
            await Until(() => system.Posts[0].Removed);

            var unlocked = Screen(app);
            app.Authority!.Session.Lock(VaultLockReason.Manual);
            await Until(() => !ReferenceEquals(Screen(app), unlocked));

            system.Posts[0].Pressed();
            await SettleAsync();
            Assert.False(app.Main!.IsVisible, "a press left over from before the unlock showed the window");

            Assert.True(Screen(app).Offer(vault));
            Assert.True(await TellAsync(vault));
            await Until(() => system.Posts.Count == 2);
            Assert.False(system.Posts[1].Removed);
        });

    [Theory]
    [InlineData(SystemAnswer.Refuses)]
    [InlineData(SystemAnswer.RefusesAfterAMoment)]
    [InlineData(SystemAnswer.Throws)]
    [InlineData(SystemAnswer.NeverAnswers)]
    [InlineData(SystemAnswer.Stalls)]
    public Task A_system_that_cannot_post_gets_the_window_which_names_the_vault(SystemAnswer mode) =>
        WithNotifications(mode, async (app, _, system, vault, notices) =>
        {
            Assert.True(await TellAsync(vault));
            var notice = await NoticeAsync(notices);

            Assert.Single(system.Posts);
            Assert.Equal("test is locked", Text(notice, "HeadText"));
            Assert.False(app.Main!.IsVisible);
            notice.Close();
        });

    [Fact]
    public Task Quitting_removes_the_notification() =>
        WithNotifications(SystemAnswer.Accepts, async (app, _, system, vault, _) =>
        {
            Assert.True(await TellAsync(vault));
            await Until(() => system.Posts.Count == 1);

            app.Tray!.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == AppTray.QuitHeader).Command!.Press();

            await Until(() => system.Posts[0].Removed);
            Assert.Null(app.LockedNotice);
        });

    private static Task WithNotifications(SystemAnswer answer, Func<App, ClassicDesktopStyleApplicationLifetime, FakeNotifications, string, List<Window>, Task> test) =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications { Answers = answer };
            using var home = new TempHome();
            using var vault = new TempVault();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim, Notifications = system };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            var notices = Notices(app);
            Assert.True(Screen(app).Offer(vault.Path_));

            await test(app, lifetime, system, vault.Path_, notices);
        });

    /// <summary>A notice built on a home of its own and a manual clock, its unlock screen showing one vault and another chosen, with a third to choose.</summary>
    private static Task WithChoice(Func<string, string, FileSystemWatcher>? watch, Func<LockedNotice, TempHome, string, string, ManualClock, Task> test) =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var shown = new TempVault();
            var before = Path.Combine(shown.Home, "before.kdbx");
            var after = Path.Combine(shown.Home, "after.kdbx");
            File.Copy(shown.Path_, before);
            File.Copy(shown.Path_, after);
            Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(home.Path, before, onlyIfNone: false));
            var clock = new ManualClock();
            using var session = new AppVaultSession(clock, home: home.Path);
            using var locked = new LockedNotice(session, () => { }, clock, _ => { }, watch: watch);
            using var screen = new UnlockViewModel(session, home.Path, new FakeVaultFilePicker(), () => { });
            Assert.True(screen.Offer(shown.Path_));
            locked.Watch(screen);

            await test(locked, home, before, after, clock);
        });

    private static Task<bool> TellNamedAsync(TempHome home, string vault) =>
        LockedAppEndpoint.TellAsync(LockedAppEndpoint.NameFor(VaultIdentity.Of(home.Path, vault)), Token);

    private static async Task HeardUntil(TempHome home, string vault, bool heard)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (await TellNamedAsync(home, vault) != heard)
        {
            Assert.True(DateTime.UtcNow < deadline, heard ? "the notice never listened for that vault" : "the notice never stopped listening for that vault");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }

    private static Task<bool> TellAsync(string vault) =>
        LockedAppEndpoint.TellAsync(LockedAppEndpoint.NameFor(VaultIdentity.Of(Home, vault)), Token);

    private static async Task TellUntilHeardAsync(string vault)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!await TellAsync(vault))
        {
            Assert.True(DateTime.UtcNow < deadline, "the app never listened for that vault");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }

    private static List<Window> Notices(App app)
    {
        var notices = new List<Window>();
        app.LockedNotice!.Shown += (_, notice) => notices.Add(notice);
        return notices;
    }

    private static async Task<Window> NoticeAsync(List<Window> notices, int count = 1)
    {
        await Until(() => notices.Count >= count);
        Assert.Equal(count, notices.Count);
        return notices[^1];
    }

    private static UnlockViewModel Screen(App app) =>
        Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(app.Main!.FindControl<ContentControl>("Root")!.Content).DataContext);

    private static void Unlock(App app, string vault)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, app.Authority!.Session.TryUnlock(vault, master.Value));
    }

    private static async Task SettleAsync()
    {
        await Task.Delay(_settle, Token);
        WindowInput.Drain();
    }

    private static AppClaim Claim(TempHome home)
    {
        Assert.True(AppClaim.TryAcquire(home.Path, out var claim));
        return claim;
    }

    private static DesktopPreferences Tray(TempHome home)
    {
        var preferences = new DesktopPreferences(home.Path);
        preferences.Update(AppSettings.Default with { StayInTray = true });
        return preferences;
    }

    private static void Attach(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod(
            "SubscribeGlobalEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(subscribe is not null, "Avalonia no longer has SubscribeGlobalEvents; attach the lifetime another way");
        subscribe.Invoke(lifetime, null);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the app never reached that state");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }

    /// <summary>A watcher whose failure a test raises, as the system raises one when it drops the watch.</summary>
    private sealed class FailingWatcher(string directory, string file) : FileSystemWatcher(directory, file)
    {
        internal void Fail() => OnError(new ErrorEventArgs(new IOException("The watch was dropped.")));
    }

    /// <summary>Makes a vault the chosen one in the process's home, putting back the one chosen before when disposed.</summary>
    private sealed class Chosen : IDisposable
    {
        private readonly string _settings = KeypasteHome.SettingsPath(Home);
        private readonly string? _before;

        internal Chosen(string vault)
        {
            _before = AppSettings.Load(_settings).Vault;
            Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(Home, vault, onlyIfNone: false));
        }

        public void Dispose() => _ = AppSettings.Save(_settings, AppSettings.Load(_settings) with { Vault = _before });
    }
}
