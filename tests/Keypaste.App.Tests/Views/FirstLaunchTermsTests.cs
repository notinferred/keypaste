using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Terms;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The terms of use are agreed to once, on one first-launch screen whose one action is Continue, which includes sending crash
/// reports: nothing is sent before it, a kept answer stands, and nothing else in the app asks.
/// </summary>
public sealed class FirstLaunchTermsTests
{
    private static string Toml(string home) => KeypasteHome.SettingsPath(home);

    private static IEnumerable<string> Texts(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty);

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        WindowInput.Drain();
    }

    private static ClassicDesktopStyleApplicationLifetime Lifetime()
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        typeof(ClassicDesktopStyleApplicationLifetime).GetMethod("SubscribeGlobalEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(lifetime, null);
        return lifetime;
    }

    private static MainWindow ShowScreen(Action agree)
    {
        var window = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
        window.FindControl<ContentControl>("Root")!.Content = new FirstLaunchView { DataContext = new FirstLaunchViewModel(TermsOfUse.Carried, agree) };
        window.Show();
        RenderedShell.Drain();
        return window;
    }

    [Fact]
    public Task The_screen_says_what_continuing_means_links_the_terms_and_has_one_action_that_Return_takes() => HeadlessSession.On(() =>
    {
        var agreed = 0;
        var window = ShowScreen(() => agreed++);

        Assert.Contains("Continuing agrees to the terms of use, which include sending crash reports. You can turn crash reports off in Settings.", Texts(window));
        Assert.True(Named<Button>(window, "OpenTerms").IsEffectivelyVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), _ => true);
        var action = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.Name != "OpenTerms" && button.IsEffectivelyVisible && button.GetVisualAncestors().OfType<FirstLaunchView>().Any());
        Assert.Equal("Continue", action.Name);
        Assert.Contains("primary", action.Classes);
        Assert.True(action.IsFocused);

        Key(window, PhysicalKey.Enter);

        Assert.Equal(1, agreed);
        window.Close();
    });

    [Fact]
    public Task The_screen_says_what_continuing_means_to_a_screen_reader_as_it_opens() => HeadlessSession.On(() =>
    {
        var window = ShowScreen(() => { });

        var live = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("live"));
        Assert.Contains("Continuing agrees to the terms of use", live.Text, StringComparison.Ordinal);
        Assert.Contains("A report holds", live.Text, StringComparison.Ordinal);
        window.Close();
    });

    [Fact]
    public Task Settings_switch_under_terms_not_agreed_to_asks_in_a_window_that_stays_off_on_Escape_and_starts_on_Continue() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        File.WriteAllText(Toml(fixture.Home), "[[settings]]\nstay_in_tray = 1\ncrash_reports = 0\nterms = \"2020-01-01\"\n");
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var settings = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { Crashes = crashes };
        var window = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
        var root = window.FindControl<ContentControl>("Root")!;
        root.Content = new SettingsView { DataContext = settings };
        window.Show();
        RenderedShell.Drain();
        var toggle = Named<ToggleSwitch>(window, "SendCrashReports");

        toggle.IsChecked = true;
        RenderedShell.Drain();

        var asking = Assert.IsType<TermsAgreementWindow>(Assert.Single(window.OwnedWindows));
        Assert.Contains("Continuing agrees to the terms of use, which include sending crash reports. You can turn crash reports off in Settings.", Texts(asking));
        Assert.True(Named<Button>(asking, "Deny").IsFocused);

        asking.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        RenderedShell.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.False(crashes.IsOn);

        toggle.IsChecked = true;
        RenderedShell.Drain();
        Assert.IsType<TermsAgreementWindow>(Assert.Single(window.OwnedWindows)).KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        RenderedShell.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.False(crashes.IsOn);
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        RenderedShell.Drain();
        Named<Button>(Assert.IsType<TermsAgreementWindow>(Assert.Single(window.OwnedWindows)), "Continue").Command!.Execute(null);
        RenderedShell.Drain();

        Assert.True(crashes.IsOn);
        Assert.True(toggle.IsChecked);
        window.Close();
    });

    [Fact]
    public Task A_lock_while_the_switch_asks_closes_the_question() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        File.WriteAllText(Toml(fixture.Home), "[[settings]]\nstay_in_tray = 1\ncrash_reports = 0\n");
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var settings = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { Crashes = crashes };
        var window = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
        var root = window.FindControl<ContentControl>("Root")!;
        root.Content = new SettingsView { DataContext = settings };
        window.Show();
        RenderedShell.Drain();
        settings.SendCrashReports = true;
        RenderedShell.Drain();
        Assert.Single(window.OwnedWindows);

        root.Content = null;
        RenderedShell.Drain();

        Assert.Empty(window.OwnedWindows);
        Assert.False(crashes.IsOn);
        window.Close();
    });

    [Fact]
    public Task The_link_opens_the_whole_terms_which_the_keyboard_scrolls_and_Escape_closes_back_to_the_link_with_nothing_agreed() => HeadlessSession.On(() =>
    {
        var agreed = 0;
        var window = ShowScreen(() => agreed++);
        var link = Named<Button>(window, "OpenTerms");
        link.Focus();
        RenderedShell.Drain();

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
        Assert.Equal(0, agreed);
        window.Close();
    });

    [Fact]
    public Task A_screen_replaced_while_the_terms_are_open_closes_them() => HeadlessSession.On(() =>
    {
        var window = ShowScreen(() => { });

        Named<Button>(window, "OpenTerms").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RenderedShell.Drain();
        Assert.Single(window.OwnedWindows);

        window.FindControl<ContentControl>("Root")!.Content = null;
        RenderedShell.Drain();

        Assert.Empty(window.OwnedWindows);
        window.Close();
    });

    [Fact]
    public Task A_start_at_login_opens_no_window_and_sends_nothing_until_the_screen_it_then_shows_is_agreed_to() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        File.WriteAllText(Toml(fixture.Home), "[[settings]]\nstay_in_tray = 1\n");
        var written = File.ReadAllText(Toml(fixture.Home));
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        crashes.Start();
        using var lifetime = Lifetime();
        using var app = new App { Preferences = preferences, Crashes = crashes };
        app.Launch(lifetime, background: true);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
        RenderedShell.Drain();

        Assert.DoesNotContain(lifetime.Windows, window => window.IsVisible);
        Assert.False(crashes.IsOn);
        Assert.False(crashes.Crash(new InvalidOperationException("SENTINEL-BACKGROUND")));
        Assert.False(Directory.Exists(crashes.Folder));
        Assert.Equal(written, File.ReadAllText(Toml(fixture.Home)));

        app.OpenWindow();
        RenderedShell.Drain();
        var view = Assert.IsType<FirstLaunchView>(lifetime.MainWindow!.FindControl<ContentControl>("Root")!.Content);
        Assert.False(crashes.IsOn);

        Assert.IsType<FirstLaunchViewModel>(view.DataContext).ContinueCommand.Press();
        RenderedShell.Drain();

        Assert.True(crashes.IsOn);
        Assert.IsType<UnlockView>(lifetime.MainWindow!.FindControl<ContentControl>("Root")!.Content);
        Assert.Equal((true, TermsOfUse.Carried.Version), (AppSettings.Load(Toml(fixture.Home)).SendCrashReports, AppSettings.Load(Toml(fixture.Home)).TermsAgreed));
    });

    // Don't send keeps it off and is not asked even under a new date, a chosen Send keeps on, and a Send under older terms sees the screen once more before anything sends.
    [Theory]
    [InlineData(1, true, true, false)]
    [InlineData(0, true, false, false)]
    [InlineData(1, false, false, true)]
    [InlineData(0, false, false, false)]
    public void A_kept_answer_stands_and_only_a_Send_under_older_terms_is_shown_the_screen(int chose, bool sameTerms, bool sends, bool asks)
    {
        using var fixture = new TempVault();
        var terms = sameTerms ? TermsOfUse.Carried.Version : "2020-01-01";
        var written = $"[[settings]]\ncrash_reports = {chose}\nterms = \"{terms}\"\n";
        File.WriteAllText(Toml(fixture.Home), written);

        using var crashes = new CrashReporting(fixture.Home, new DesktopPreferences(fixture.Home), endpoint: null);
        crashes.Start();

        Assert.Equal((asks, sends), (crashes.AsksTerms, crashes.IsOn));
        Assert.Equal(written, File.ReadAllText(Toml(fixture.Home)));
    }

    [Fact]
    public void Continue_under_new_terms_keeps_sending_under_them_and_the_next_start_does_not_ask()
    {
        using var fixture = new TempVault();
        File.WriteAllText(Toml(fixture.Home), "[[settings]]\ncrash_reports = 1\nterms = \"2020-01-01\"\n");

        using (var crashes = new CrashReporting(fixture.Home, new DesktopPreferences(fixture.Home), endpoint: null))
        {
            crashes.Agree();
            Assert.True(crashes.IsOn);
            Assert.False(crashes.AsksTerms);
        }

        using var next = new CrashReporting(fixture.Home, new DesktopPreferences(fixture.Home), endpoint: null);
        Assert.Equal((true, false), (next.IsOn, next.AsksTerms));
    }

    [Fact]
    public void Settings_shows_its_plain_switch_whatever_the_file_says_and_turning_it_off_stops_reports()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
        using var settings = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { Crashes = crashes };
        crashes.Agree();

        Assert.True(settings.HasCrashReports);
        Assert.True(settings.SendCrashReports);

        settings.SendCrashReports = false;

        Assert.False(crashes.IsOn);
        Assert.False(crashes.AsksTerms);
        Assert.Equal(false, AppSettings.Load(Toml(fixture.Home)).SendCrashReports);
    }

    [Fact]
    public Task Share_opens_on_its_form_with_no_terms_step_and_records_nothing_of_them() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Shell.ShareCommand.Press(new EntryName(string.Empty, "github"));
        RenderedShell.Drain();

        Assert.True(shell.Named<Border>("ShareDialog").IsEffectivelyVisible);
        Assert.True(shell.Named<TextBox>("Recipient").IsFocused);
        Assert.True(shell.Shell.Share!.CreateCommand.CanExecute(null));
        Assert.DoesNotContain(shell.Window.GetVisualDescendants().OfType<Control>(), control => control.Name == "OpenTerms");
        Assert.Null(shell.Shell.Preferences.Current.TermsAgreed);
    });
}
