using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.GlobalShortcuts;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.ClosingTheMainWindowTests;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Quick search: the search shortcut and Search… open a small window over every unlocked vault that names items by title,
/// username and vault, copies the chosen password, username or one-time code by the main window's own commands and countdown,
/// and closes so the app in use has the keyboard back. With every vault locked it asks quick unlock first; a waiting prompt still
/// comes first; Settings can send the shortcut to the keypaste window instead.
/// </summary>
/// <remarks>The app is composed by <see cref="App.Launch"/> as <see cref="SearchFromAnyAppTests"/> composes it, with a fake clipboard and a hide the test counts.</remarks>
public sealed class QuickSearchTests
{
    private const string _password = "SENTINEL-QS-PASSWORD-7f3a";
    private const string _notes = "SENTINEL-QS-NOTES-19c2";
    private const string _custom = "SENTINEL-QS-CUSTOM-4b8e";
    private const string _seed = "JBSWY3DPEHPK3PXP";
    private const string _username = "maya@example.com";
    private static readonly EntryName _github = new(string.Empty, "github");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task The_shortcut_and_Search_open_quick_search_with_its_fixed_title_and_leave_the_window_closed() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);

            foreach (var press in new Action[] { running.System.Press, () => TrayItem(running.App, AppTray.SearchHeader).Command!.Press() })
            {
                press();
                var window = await OpenAsync(running);

                Assert.False(running.Main.IsVisible, "quick search brought the keypaste window forward");
                Assert.Equal(QuickSearchWindow.FixedTitle, window.Title);
                Assert.True(window.FindControl<TextBox>("QuickSearchField")!.IsFocused);

                WindowInput.Type(window, "git");
                Assert.Equal("github", Assert.Single(Model(window).Rows).Title);
                Assert.Equal(QuickSearchWindow.FixedTitle, window.Title);

                Key(window, PhysicalKey.Escape);
                Assert.Null(running.App.QuickSearch);
                Assert.Null(running.Clipboard.Content);
            }

            Assert.Equal(2, running.Hidden);
        });

    [Fact]
    public Task A_match_shows_its_title_username_and_vault_and_no_value_reaches_the_screen_or_automation() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);
            running.System.Press();
            var window = await OpenAsync(running);

            WindowInput.Type(window, "git");
            var row = Assert.Single(Model(window).Rows);
            Assert.Equal(("github", _username, "agents"), (row.Title, row.Username, row.Vault));
            Assert.Equal($"github, {_username}, agents", row.ToString());

            foreach (var secret in new[] { _password, _notes, _custom, _seed })
            {
                AutomationSurface.AssertNothingExposes((Control)window.Content!, secret);
            }

            // A search never reads a value, so typing one finds nothing.
            window.FindControl<TextBox>("QuickSearchField")!.Text = string.Empty;
            WindowInput.Type(window, _password);
            Assert.Empty(Model(window).Rows);
            Assert.True(Model(window).ShowsNoMatch);
        });

    [Fact]
    public Task Return_copies_the_password_through_the_countdown_closes_and_hands_back_and_a_lock_clears_it() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);
            running.System.Press();
            var window = await OpenAsync(running);

            WindowInput.Type(window, "git");
            Key(window, PhysicalKey.Enter);
            await Until(() => running.App.QuickSearch is null);

            Assert.Equal(_password, running.Clipboard.Content);
            Assert.True(running.Clipboard.ContentWasSetAsASecret);
            Assert.True(running.Shell.Clipboard.IsCounting, "the copy started no countdown");
            Assert.Equal("Password", running.Shell.Clipboard.Label);
            Assert.False(window.IsVisible);
            Assert.Equal(1, running.Hidden);

            await running.App.Vaults!.LockAllAsync(VaultLockReason.Manual);
            await Until(() => running.Clipboard.Content is null);
        });

    [Fact]
    public Task Shift_command_C_copies_the_username_plain_and_command_T_the_one_time_code_through_the_countdown() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);

            running.System.Press();
            var window = await OpenAsync(running);
            WindowInput.Type(window, "git");
            Chord(window, PhysicalKey.C, RawInputModifiers.Shift);
            await Until(() => running.App.QuickSearch is null);
            Assert.Equal(_username, running.Clipboard.Content);
            Assert.False(running.Clipboard.ContentWasSetAsASecret);
            Assert.False(running.Shell.Clipboard.IsCounting, "a username copied as typed was counted down");

            running.System.Press();
            window = await OpenAsync(running);
            WindowInput.Type(window, "git");
            Chord(window, PhysicalKey.T);
            await Until(() => running.App.QuickSearch is null);
            Assert.Matches("^[0-9]{6}$", running.Clipboard.Content);
            Assert.True(running.Clipboard.ContentWasSetAsASecret);
            Assert.True(running.Shell.Clipboard.IsCounting);
            Assert.Equal("One-time code", running.Shell.Clipboard.Label);
            Assert.Equal(2, running.Hidden);
        });

    [Fact]
    public Task Command_Return_opens_the_chosen_item_in_the_keypaste_window() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);
            running.System.Press();
            var window = await OpenAsync(running);

            WindowInput.Type(window, "git");
            Chord(window, PhysicalKey.Enter);
            WindowInput.Drain();

            Assert.Null(running.App.QuickSearch);
            Assert.True(running.Main.IsVisible);
            Assert.Equal("github", Assert.IsType<EntriesViewModel>(running.Shell.Content).Selected?.Title);
            Assert.Null(running.Clipboard.Content);
            Assert.Equal(0, running.Hidden);
        });

    [Fact]
    public Task Another_window_taking_focus_or_any_lock_closes_it_without_handing_back() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);

            running.System.Press();
            WindowInput.Deactivate(await OpenAsync(running));
            Assert.Null(running.App.QuickSearch);

            running.System.Press();
            var window = await OpenAsync(running);
            await running.App.Vaults!.LockAllAsync(VaultLockReason.Manual);
            await Until(() => running.App.QuickSearch is null);
            Assert.False(window.IsVisible);
            Assert.Equal(0, running.Hidden);
        });

    [Fact]
    public Task With_every_vault_locked_it_asks_quick_unlock_first_then_opens_and_a_cancel_shows_nothing() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var keys = new FakeQuickUnlockKeys(MachineQuickUnlock.TouchIdName);
            using var running = await Running.UnlockedAsync(fixture, keys: keys);
            Assert.Equal(QuickUnlockOutcome.Done, running.App.Authority!.Session.KeepForQuickUnlock());
            await running.LockAsync();
            Assert.True(running.UnlockScreen.ShowsQuickUnlockAlone);
            var asked = keys.Asked;

            keys.Response = KeyOutcome.Cancelled;
            running.System.Press();
            await Until(() => keys.Asked == asked + 1 && !running.UnlockScreen.Busy);
            WindowInput.Drain();
            Assert.Null(running.App.QuickSearch);
            Assert.False(running.Main.IsVisible, "a cancelled quick unlock brought the window forward");

            keys.Response = KeyOutcome.Released;
            running.System.Press();
            await OpenAsync(running);
            Assert.Equal(asked + 2, keys.Asked);
            Assert.True(running.App.Authority.Session.IsUnlocked);
            Assert.False(running.Main.IsVisible);
        });

    [Fact]
    public Task With_every_vault_locked_and_no_quick_unlock_it_shows_the_unlock_screen_as_before() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);
            await running.LockAsync();

            running.System.Press();
            WindowInput.Drain();

            Assert.True(running.Main.IsVisible);
            Assert.IsType<UnlockView>(running.Main.FindControl<ContentControl>("Root")!.Content);
            Assert.Null(running.App.QuickSearch);
        });

    [Fact]
    public Task A_waiting_request_comes_forward_instead_of_quick_search() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture);
            await using var client = await ConnectAsync(running.App.Authority!, running.Vault);
            var reply = Request(client, running.App, running.Vault);
            var prompt = await PromptAsync(running.Lifetime);

            running.System.Press();
            WindowInput.Drain();

            Assert.Null(running.App.QuickSearch);
            Assert.True(prompt.FindControl<Button>("Deny")!.IsFocused, "the prompt came forward off Deny");
            await Until(() => IsArmed(prompt));
            Click(prompt, "Deny");
            Assert.Equal(AuditDecision.Denied, (await reply.WaitAsync(TimeSpan.FromSeconds(10), Token))!.Decision);
        });

    [Fact]
    public Task Settings_choice_of_the_keypaste_window_is_kept_and_the_next_press_follows_it() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            using var running = await Running.UnlockedAsync(fixture, hidden: false);
            running.Shell.Current = Destinations.Of(DestinationKind.Settings);
            var settings = Assert.IsType<SettingsViewModel>(running.Shell.Content);
            Assert.Equal(SearchOpens.QuickSearch, settings.SearchOpensChoice.Opens);

            settings.SearchOpensChoice = settings.SearchOpensChoices.Single(choice => choice.Opens == SearchOpens.Window);
            Assert.Equal(SearchOpens.Window, AppSettings.Load(KeypasteHome.SettingsPath(fixture.Home)).SearchOpens);
            running.Main.Close();

            running.System.Press();
            await Until(() => running.Main.FocusManager?.GetFocusedElement() is TextBox { Name: EntriesView.SearchName });
            Assert.Null(running.App.QuickSearch);
            Assert.True(running.Main.IsVisible);
        });

    private static async Task<QuickSearchWindow> OpenAsync(Running running)
    {
        await Until(() => running.App.QuickSearch is { IsVisible: true });
        return running.App.QuickSearch!;
    }

    private static QuickSearchViewModel Model(Window window) => Assert.IsType<QuickSearchViewModel>(window.DataContext);

    /// <summary>A key with this platform's command modifier, as a person presses ⌘ or Ctrl with it.</summary>
    private static void Chord(Window window, PhysicalKey key, RawInputModifiers also = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control) | also);
        WindowInput.Drain();
    }

    private static NativeMenuItem TrayItem(App app, string header) =>
        app.Tray!.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);

    /// <summary>The app composed by launch over a vault holding a login with every kind of value, unlocked on its own screen.</summary>
    private sealed class Running : IDisposable
    {
        private Running(TempVault fixture, IQuickUnlockKeys? keys)
        {
            Vault = Path.Combine(fixture.Home, "agents.kdbx");

            using (var created = ClaimedVault.Create(Vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = "SENTINEL-QS-DEPLOY" });
                created.AddEntry(new VaultEntry { Title = "github", Username = _username, Password = _password, Notes = _notes, Url = "https://github.com" });
                Assert.True(created.SetFields(_github, [new FieldWrite("Recovery code", _custom, Protect: true)]));
                Assert.True(created.SetTotp(_github, _seed));
                TempVault.Sees(created, "f21");
                created.Save();
            }

            var preferences = new DesktopPreferences(fixture.Home);
            preferences.Update(AppSettings.Default with { StayInTray = true });
            Lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(Lifetime);
            App = new App
            {
                Preferences = preferences,
                GlobalShortcutFor = () => System,
                Clipboards = _ => Clipboard,
                QuickUnlockKeys = () => keys,
                HideApp = () => Hidden++,
            };
            App.Launch(Lifetime);
            Lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            Main = Lifetime.MainWindow!;
        }

        internal string Vault { get; }

        internal ClassicDesktopStyleApplicationLifetime Lifetime { get; }

        internal App App { get; }

        internal Window Main { get; }

        internal FakeGlobalShortcut System { get; } = new();

        internal FakeClipboard Clipboard { get; } = new();

        internal int Hidden { get; private set; }

        internal ShellViewModel Shell => Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(Root).DataContext);

        internal UnlockViewModel UnlockScreen => Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(Root).DataContext);

        private object? Root => Main.FindControl<ContentControl>("Root")!.Content;

        /// <summary>Unlocks on the unlock screen, then closes the window into the tray unless a test keeps it.</summary>
        internal static async Task<Running> UnlockedAsync(TempVault fixture, IQuickUnlockKeys? keys = null, bool hidden = true)
        {
            var running = new Running(fixture, keys);
            running.Main.Show();
            await running.UnlockOnScreenAsync();

            if (hidden)
            {
                running.Main.Close();
                Assert.False(running.Main.IsVisible);
            }

            return running;
        }

        /// <summary>Locks every vault and offers this one on the unlock screen again, the window staying where it was.</summary>
        internal async Task LockAsync()
        {
            await App.Vaults!.LockAllAsync(VaultLockReason.Manual);
            await Until(() => Root is UnlockView);
            UnlockScreen.Offer(Vault);
        }

        private async Task UnlockOnScreenAsync()
        {
            Assert.True(UnlockScreen.Offer(Vault));

            foreach (var c in TempVault.Password)
            {
                UnlockScreen.Type(c);
            }

            await UnlockScreen.UnlockAsync();
            await Until(() => Root is ShellView);
        }

        public void Dispose()
        {
            App.Dispose();
            Lifetime.Dispose();
        }
    }
}
