using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Terms;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The terms of use are one link away where their two services start: the crash-report choice, asked once, which never stands
/// between someone and their vault and sends nothing until they choose Send, and Share…, which makes no link before Continue.
/// </summary>
public sealed class TermsWhereServicesStartTests
{
    private static void AssertAnswersInView(Window window)
    {
        var viewport = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(0, viewport.Offset.Y);

        foreach (var name in new[] { "SendCrashReports", "DontSend", "OpenTerms" })
        {
            var control = Named<Button>(window, name);
            var bottom = control.TranslatePoint(new Avalonia.Point(0, control.Bounds.Height), viewport)!.Value.Y;
            Assert.True(bottom <= viewport.Viewport.Height, $"{name} ends at {bottom} in a view {viewport.Viewport.Height} tall");
        }
    }

    private static string Toml(string home) => KeypasteHome.SettingsPath(home);

    private static IEnumerable<string> Texts(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty);

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static (MainWindow Window, UnlockViewModel Model) ShowUnlock(AppVaultSession session, string home, CrashChoiceViewModel? choice)
    {
        var model = new UnlockViewModel(session, home, new FakeVaultFilePicker(), () => { }) { CrashChoice = choice };
        var window = new MainWindow { Width = 1280, Height = 800 };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
        window.Show();
        RenderedShell.Drain();
        return (window, model);
    }

    [Fact]
    public Task The_choice_sits_below_the_password_which_keeps_the_keyboard_and_Return_unlocks_past_it() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        var unlocked = 0;
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => unlocked++)
        {
            CrashChoice = new CrashChoiceViewModel(TermsOfUse.Carried, crashes.Choose),
        };
        var window = new MainWindow { Width = 1280, Height = 800 };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
        window.Show();
        RenderedShell.Drain();

        var password = Named<Keypaste.App.Controls.MaskedInput>(window, "Password");
        var send = Named<Button>(window, "SendCrashReports");
        var dontSend = Named<Button>(window, "DontSend");
        Assert.True(password.IsFocused);
        Assert.True(Top(password, window) < Top(send, window));
        Assert.Equal(send.Classes, dontSend.Classes);
        Assert.DoesNotContain("primary", send.Classes);

        WindowInput.Type(window, TempVault.Password);
        Key(window, PhysicalKey.Enter);
        await Until(() => unlocked == 1);

        Assert.True(model.CrashChoice!.IsAsking);
        Assert.False(crashes.IsOn);
        Assert.Null(preferences.Current.SendCrashReports);
        Assert.Null(preferences.Current.CrashChoiceTerms);
        window.Close();
    });

    private static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        WindowInput.Drain();
    }

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 400 && !condition(); tries++)
        {
            RenderedShell.Drain();
            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    [Fact]
    public Task The_link_opens_the_whole_terms_which_the_keyboard_scrolls_and_Escape_closes_back_to_the_link() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var (window, model) = ShowUnlock(session, fixture.Home, new CrashChoiceViewModel(TermsOfUse.Carried, _ => true));

        using (model)
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), control => control.Name == "TermsText");

            var link = Named<Button>(window, "OpenTerms");
            Named<Button>(window, "DontSend").Focus();
            RenderedShell.Drain();
            Key(window, PhysicalKey.Tab);
            Assert.True(link.IsFocused);

            Key(window, PhysicalKey.Enter);
            RenderedShell.Drain();

            var terms = Assert.IsType<TermsWindow>(Assert.Single(window.OwnedWindows));
            var text = Named<ScrollViewer>(terms, "TermsText");
            Assert.True(text.IsFocused);
            var shown = Texts(terms).ToList();
            Assert.Contains(TermsOfUse.Carried.Title, shown);
            Assert.Contains(TermsOfUse.Carried.Status, shown);
            Assert.All(TermsOfUse.Carried.Sections, section => Assert.Contains(section.Heading, shown));

            Key(terms, PhysicalKey.PageDown);
            Assert.True(text.Offset.Y > 0);

            terms.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            RenderedShell.Drain();

            Assert.Empty(window.OwnedWindows);
            Assert.True(link.IsFocused);
            window.Close();
        }
    });

    [Fact]
    public Task A_screen_replaced_while_the_terms_are_open_closes_them() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var (window, model) = ShowUnlock(session, fixture.Home, new CrashChoiceViewModel(TermsOfUse.Carried, _ => true));

        using (model)
        {
            Named<Button>(window, "OpenTerms").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            RenderedShell.Drain();
            Assert.Single(window.OwnedWindows);

            window.FindControl<ContentControl>("Root")!.Content = null;
            RenderedShell.Drain();

            Assert.Empty(window.OwnedWindows);
            window.Close();
        }
    });

    // The welcome, KeePassXC's databases however many, and a locked vault with other recent vaults.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(6, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 5)]
    public Task The_two_answers_and_the_link_are_in_view_without_scrolling_at_the_default_window(int keePassXcDatabases, int recentVaults) => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        var others = Enumerable.Range(0, Math.Max(keePassXcDatabases, recentVaults)).Select(i => Path.Combine(fixture.Home, $"other-{i}.kdbx")).ToList();
        others.ForEach(other => File.Copy(fixture.Path_, other));

        if (recentVaults > 0)
        {
            RecentVaults.Save(
                KeypasteHome.RecentPath(fixture.Home),
                [new RecentVault(fixture.Path_, DateTimeOffset.UtcNow), .. others.Select(other => new RecentVault(other, DateTimeOffset.UtcNow.AddDays(-1)))]);
        }

        var ini = Path.Combine(fixture.Home, "keepassxc.ini");
        File.WriteAllText(ini, $"[General]\nLastDatabases={string.Join(", ", others.Select(other => $"\"{other.Replace('\\', '/')}\""))}\n");
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { }, keePassXcConfigs: () => keePassXcDatabases > 0 ? [ini] : [])
        {
            CrashChoice = new CrashChoiceViewModel(TermsOfUse.Carried, _ => true),
        };
        var window = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
        window.Show();
        RenderedShell.Drain();

        Assert.Equal(keePassXcDatabases, model.KeePassXc.Count);
        Assert.Equal(recentVaults > 0, model.ShowsRecent);
        AssertAnswersInView(window);
        window.Close();
    });

    // The fullest screen: Windows Hello, More options and a note on why it locked above the card, and recent vaults below it.
    [Fact]
    public Task The_two_answers_and_the_link_stay_in_view_under_Windows_Hello_and_More_options() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        using var session = new AppVaultSession(
            new ManualClock(AppClock.Start), home: fixture.Home, hardwareKeys: new SoftwareYubiKey(new byte[20]), quickUnlock: new FakeQuickUnlockSigner());

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        Assert.Equal(QuickUnlockOutcome.Done, session.KeepForQuickUnlock());
        session.Lock(VaultLockReason.Manual);

        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { }, lockedBy: VaultLockReason.Manual)
        {
            CrashChoice = new CrashChoiceViewModel(TermsOfUse.Carried, _ => true),
        };
        var window = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
        window.Show();
        RenderedShell.Drain();

        Assert.True(model.OffersQuickUnlock);
        Assert.True(model.OffersMoreOptions);
        Assert.True(model.HasLockNote);
        AssertAnswersInView(window);
        window.Close();
    });

    [Fact]
    public Task Answering_moves_the_keyboard_to_the_password_and_says_what_was_chosen() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        var (window, model) = ShowUnlock(session, fixture.Home, new CrashChoiceViewModel(TermsOfUse.Carried, crashes.Choose));

        using (model)
        {
            Named<Button>(window, "DontSend").Focus();
            RenderedShell.Drain();
            Key(window, PhysicalKey.Enter);
            RenderedShell.Drain();

            Assert.False(crashes.IsOn);
            Assert.Equal(false, AppSettings.Load(Toml(fixture.Home)).SendCrashReports);
            Assert.Contains("No crash reports will be sent. You can change this in Settings.", Texts(window));
            Assert.False(Named<Button>(window, "SendCrashReports").IsEffectivelyVisible);
            Assert.True(Named<Keypaste.App.Controls.MaskedInput>(window, "Password").IsFocused);
            window.Close();
        }
    });

    [Fact]
    public Task Unlocking_past_the_choice_sends_nothing_and_no_later_lock_or_start_asks_again_while_Settings_holds_it() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod("SubscribeGlobalEvents", BindingFlags.Instance | BindingFlags.NonPublic);
        subscribe!.Invoke(lifetime, null);
        using var app = new App { Preferences = preferences, Crashes = crashes };
        app.Launch(lifetime);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
        var main = lifetime.MainWindow!;
        main.Show();
        RenderedShell.Drain();
        var root = main.FindControl<ContentControl>("Root")!;

        var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
        Assert.NotNull(unlock.CrashChoice);

        Assert.True(unlock.Offer(fixture.Path_, null));

        foreach (var c in TempVault.Password)
        {
            unlock.Type(c);
        }

        await unlock.UnlockCommand.PressAsync();
        await Until(() => root.Content is ShellView);

        Assert.False(crashes.IsOn);
        Assert.Null(AppSettings.Load(Toml(fixture.Home)).SendCrashReports);

        app.CloseToTray();
        await Until(() => root.Content is UnlockView);
        Assert.Null(Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext).CrashChoice);

        var nextPreferences = new DesktopPreferences(fixture.Home);
        using var next = new CrashReporting(fixture.Home, nextPreferences, endpoint: null);
        Assert.False(next.AsksThisRun);
        Assert.False(next.IsOn);
        Assert.False(next.HasAnswered);
        Assert.Equal(TermsOfUse.Carried.Version, nextPreferences.Current.CrashChoiceAsked);
    });

    [Fact]
    public Task A_start_at_login_that_opens_no_window_has_not_asked_and_opening_it_does() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        File.WriteAllText(Toml(fixture.Home), "[[settings]]\nstay_in_tray = 1\n");
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod("SubscribeGlobalEvents", BindingFlags.Instance | BindingFlags.NonPublic);
        subscribe!.Invoke(lifetime, null);
        using var app = new App { Preferences = preferences, Crashes = crashes };
        app.Launch(lifetime, background: true);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
        RenderedShell.Drain();

        Assert.Null(AppSettings.Load(Toml(fixture.Home)).CrashChoiceAsked);

        app.OpenWindow();
        RenderedShell.Drain();

        Assert.Equal(TermsOfUse.Carried.Version, AppSettings.Load(Toml(fixture.Home)).CrashChoiceAsked);
    });

    // An existing app.toml: an answer under these terms stands, and older terms stop sending without asking, since a later start is not "at first run"; Settings holds that choice.
    [Theory]
    [InlineData(1, true, true)]
    [InlineData(0, true, false)]
    [InlineData(1, false, false)]
    [InlineData(0, false, false)]
    public void An_existing_answer_is_not_asked_again_and_sends_only_under_these_terms(int chose, bool sameTerms, bool sends)
    {
        using var fixture = new TempVault();
        var terms = sameTerms ? TermsOfUse.Carried.Version : "2020-01-01";
        File.WriteAllText(Toml(fixture.Home), $"[[settings]]\ncrash_reports = {chose}\nterms = \"{terms}\"\n");
        var preferences = new DesktopPreferences(fixture.Home);

        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        using var settings = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { Crashes = crashes };

        Assert.Equal((false, sends, !sameTerms), (crashes.AsksThisRun, crashes.IsOn, settings.HasCrashChoice));
    }

    [Fact]
    public void Settings_shows_the_short_choice_until_answered_and_its_switch_cannot_turn_on_first()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var settings = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { Crashes = crashes };

        Assert.True(settings.HasCrashChoice);
        Assert.False(settings.ShowsCrashSwitch);

        settings.SendCrashReports = true;
        Assert.False(crashes.IsOn);
        Assert.False(File.Exists(Toml(fixture.Home)));

        settings.CrashChoice!.SendCommand.Press();

        Assert.True(crashes.IsOn);
        Assert.True(settings.SendCrashReports);
        Assert.True(settings.ShowsCrashSwitch);
    }

    [Fact]
    public Task Continue_on_Share_holds_for_the_run_and_leaves_a_file_that_does_not_read_as_it_is() => HeadlessSession.On(() =>
    {
        const string handEdit = "[[settings]\nhalf an edit";
        using var shell = new RenderedShell();
        var path = KeypasteHome.SettingsPath(shell.Home);
        File.WriteAllText(path, handEdit);

        shell.Shell.ShareCommand.Press("github");
        RenderedShell.Drain();
        Key(shell.Window, PhysicalKey.Enter);
        RenderedShell.Drain();

        Assert.False(shell.Shell.Share!.IsReadingTerms);
        Assert.Equal(TermsOfUse.Carried.Version, shell.Shell.Preferences.Current.ShareTermsLinked);
        Assert.Equal(handEdit, File.ReadAllText(path));

        using var next = new CrashReporting(shell.Home, new DesktopPreferences(shell.Home), endpoint: null);
        Assert.False(next.AsksThisRun);
    });

    [Fact]
    public Task Share_links_the_terms_first_makes_no_link_until_Continue_and_remembers_apart_from_the_crash_choice() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell();
        shell.Shell.ShareCommand.Press("github");
        RenderedShell.Drain();
        var share = shell.Shell.Share!;

        Assert.True(share.IsReadingTerms);
        Assert.DoesNotContain(TermsOfUse.Carried.Title, Texts(shell.Window));
        Assert.DoesNotContain("primary", shell.Named<Button>("CancelTerms").Classes);
        Assert.False(shell.Named<Border>("ShareDialog").IsEffectivelyVisible);
        Assert.True(shell.Named<Button>("ContinueToShare").IsFocused);
        Assert.False(share.CreateCommand.CanExecute(null));
        await share.CreateCommand.PressAsync();
        Assert.Null(shell.Shell.Preferences.Current.ShareTermsLinked);

        // Return on the link opens the terms and answers nothing, and Escape there leaves the step open.
        var link = shell.Named<Button>("OpenTerms");
        link.Focus();
        RenderedShell.Drain();
        Key(shell.Window, PhysicalKey.Enter);
        RenderedShell.Drain();
        var terms = Assert.IsType<TermsWindow>(Assert.Single(shell.Window.OwnedWindows));
        Assert.True(share.IsReadingTerms);
        terms.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        RenderedShell.Drain();
        Assert.True(link.IsFocused);
        Assert.Same(share, shell.Shell.Share);
        Assert.True(share.IsReadingTerms);
        Assert.Null(shell.Shell.Preferences.Current.ShareTermsLinked);

        // Cancelled, nothing is recorded, and the step shows again.
        share.CancelCommand.Press();
        RenderedShell.Drain();
        shell.Shell.ShareCommand.Press("github");
        RenderedShell.Drain();
        Assert.True(shell.Shell.Share!.IsReadingTerms);

        Key(shell.Window, PhysicalKey.Enter);
        RenderedShell.Drain();

        Assert.False(shell.Shell.Share!.IsReadingTerms);
        Assert.True(shell.Shell.Share!.CreateCommand.CanExecute(null));
        Assert.True(shell.Named<TextBox>("Recipient").IsFocused);
        Assert.Equal(TermsOfUse.Carried.Version, shell.Shell.Preferences.Current.ShareTermsLinked);
        Assert.Null(shell.Shell.Preferences.Current.CrashChoiceTerms);
        Assert.Null(shell.Shell.Preferences.Current.SendCrashReports);

        shell.Shell.Share!.CancelCommand.Press();
        RenderedShell.Drain();
        shell.Shell.ShareCommand.Press("github");
        RenderedShell.Drain();
        Assert.False(shell.Shell.Share!.IsReadingTerms);
    });
}
