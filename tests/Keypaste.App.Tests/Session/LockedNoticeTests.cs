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
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// With the app running locked in the menu bar or tray and holding its home, a bridge that finds it locked has it show
/// one notice and no window; only the notice's Open keypaste shows the unlock screen, and the next notice waits for an
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

            Assert.Equal("An agent asked for test.kdbx", Text(notice, "HeadText"));
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
            Assert.True(notice.IsVisible, "a press before Open keypaste was armed closed the notice");
            Assert.False(app.Main!.IsVisible, "a press before Open keypaste was armed showed the window");

            await Until(() => open.IsEffectivelyEnabled);
            Click(notice, "Open");

            await Until(() => !notice.IsVisible);
            Assert.True(app.Main.IsVisible, "Open keypaste did not show the window");
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
            Assert.Equal("An agent asked for chosen.kdbx", Text(notice, "HeadText"));

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
            Assert.Equal("An agent asked for after.kdbx", Text(notice, "HeadText"));
            Assert.False(await TellAsync(before), "the app still listened for the vault chosen before");
            Assert.True(await TellAsync(shown.Path_), "the app stopped listening for the vault its unlock screen shows");
            Assert.False(app.Main!.IsVisible);
            notice.Close();
        });

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
            Assert.Equal("An agent asked for test.kdbx", Text(notice, "HeadText"));
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
