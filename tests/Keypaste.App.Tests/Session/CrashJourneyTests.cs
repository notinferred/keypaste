using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Terms;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The crash-report choice and its link to the terms on the unlock screen, and what an exception on the UI thread does to a launched app.
/// </summary>
/// <remarks>
/// The app is launched as <see cref="ClosingTheMainWindowTests"/> launches it, with crash reports composed for its home
/// but none of the process's hooks set, and <see cref="App.EndAfterCrash"/> run as the dispatcher's hook runs it; its
/// ending is captured, since the lifetime's shutdown would end the dispatcher this assembly shares.
/// </remarks>
public sealed class CrashJourneyTests
{
    private const string _copied = "SENTINEL-COPIED-PASSWORD-7a3e19";
    private const string _message = "SENTINEL-CRASH-MESSAGE-5d02b8";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task A_fresh_home_opens_on_the_unlock_screen_with_the_link_and_two_equal_choices_and_never_asks_again() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var preferences = new DesktopPreferences(fixture.Home);
            using var crashes = new CrashReporting(fixture.Home, preferences, endpoint: null);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = preferences, Crashes = crashes };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Show();
            WindowInput.Drain();
            var root = main.FindControl<ContentControl>("Root")!;

            // No terms screen: the unlock screen asks, below everything else, and takes no Return.
            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
            var first = Assert.IsType<CrashChoiceViewModel>(unlock.CrashChoice);
            Assert.Equal("unlock", crashes.Screen);
            Assert.DoesNotContain(TermsOfUse.Carried.Title, Texts(main));
            Assert.Contains("Crash reports", Texts(main));
            Assert.True(main.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OpenTerms").IsEffectivelyVisible);

            var send = main.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SendCrashReports");
            var dontSend = main.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "DontSend");
            Assert.Equal(send.Classes, dontSend.Classes);
            Assert.DoesNotContain("primary", send.Classes);
            Assert.False(send.IsDefault || dontSend.IsDefault);
            Assert.False(send.IsFocused || dontSend.IsFocused);
            Key(main, PhysicalKey.Enter);
            Assert.True(first.IsAsking);
            var asked = AppSettings.Load(KeypasteHome.SettingsPath(fixture.Home));
            Assert.Equal((TermsOfUse.Carried.Version, null, null), (asked.CrashChoiceAsked, asked.SendCrashReports, asked.CrashChoiceTerms));
            Assert.False(crashes.IsOn);

            first.DontSendCommand.Press();

            Assert.IsType<UnlockView>(root.Content);
            Assert.False(first.IsAsking);
            var saved = AppSettings.Load(KeypasteHome.SettingsPath(fixture.Home));
            Assert.Equal((false, TermsOfUse.Carried.Version), (saved.SendCrashReports, saved.CrashChoiceTerms));

            // A lock or a close into the tray rebuilds the unlock screen, and an answered choice is not asked again.
            app.CloseToTray();
            await Until(() => root.Content is UnlockView);
            app.OpenWindow();
            Assert.Null(Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext).CrashChoice);

            using var again = new CrashReporting(fixture.Home, new DesktopPreferences(fixture.Home), endpoint: null);
            Assert.False(again.AsksThisRun);
        });

    [Fact]
    public Task A_ui_thread_exception_keeps_its_report_then_locks_clears_the_copy_and_ends_with_its_exit_code() =>
        HeadlessSession.On(async () =>
        {
            using var crashed = await CrashedAsync(send: true);

            Assert.Equal(App.CrashExitCode, crashed.Ended);
            Assert.False(crashed.Session.IsUnlocked);
            Assert.Null(crashed.Clipboard.Content);
            Assert.Null(crashed.App.Authority);
            Assert.False(crashed.Main.IsVisible);

            var report = Assert.Single(crashed.Crashes.Waiting()).Text;
            Assert.Contains("\"kind\":\"crash\"", report, StringComparison.Ordinal);
            Assert.Contains("\"screen\":\"items\"", report, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"System.InvalidOperationException\"", report, StringComparison.Ordinal);
            Assert.DoesNotContain(_message, report, StringComparison.Ordinal);
            Assert.DoesNotContain(_copied, report, StringComparison.Ordinal);
        });

    [Fact]
    public Task Switched_off_the_same_exception_still_locks_clears_and_ends_but_keeps_nothing() =>
        HeadlessSession.On(async () =>
        {
            using var crashed = await CrashedAsync(send: false);

            Assert.Equal(App.CrashExitCode, crashed.Ended);
            Assert.False(crashed.Session.IsUnlocked);
            Assert.Null(crashed.Clipboard.Content);
            Assert.False(Directory.Exists(crashed.Crashes.Folder));
        });

    [Fact]
    public Task A_second_exception_while_the_app_ends_is_only_reported() =>
        HeadlessSession.On(async () =>
        {
            using var crashed = await CrashedAsync(send: true);

            crashed.App.EndAfterCrash(Thrown(_message));
            WindowInput.Drain();

            Assert.Equal(1, crashed.Endings);
            Assert.Contains("\"count\":2", Assert.Single(crashed.Crashes.Waiting()).Text, StringComparison.Ordinal);
        });

    /// <summary>A launched app whose person answered the first run, unlocked with a password copied, then an exception on the UI thread.</summary>
    private static async Task<Crashed> CrashedAsync(bool send)
    {
        var crashed = new Crashed();

        try
        {
            File.WriteAllText(
                KeypasteHome.SettingsPath(crashed.Fixture.Home),
                $"[[settings]]\ncrash_reports = {(send ? 1 : 0)}\nterms = \"{TermsOfUse.Carried.Version}\"\n");

            crashed.Launch();
            var root = crashed.Main.FindControl<ContentControl>("Root")!;
            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);

            Assert.True(unlock.Offer(crashed.Fixture.Path_, null));

            foreach (var c in TempVault.Password)
            {
                unlock.Type(c);
            }

            await unlock.UnlockCommand.PressAsync();
            await Until(() => root.Content is ShellView);

            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);
            crashed.Session = crashed.App.Authority!.Session;
            await shell.Clipboard.CopyAsync(_copied, "Password");
            Assert.Equal(_copied, crashed.Clipboard.Content);

            crashed.App.EndAfterCrash(Thrown(_message));
            await Until(() => crashed.Ended is not null);
            return crashed;
        }
        catch
        {
            crashed.Dispose();
            throw;
        }
    }

    private static InvalidOperationException Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static List<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty)];

    private static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        WindowInput.Drain();
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

    /// <summary>One launched app and what its crash left.</summary>
    private sealed class Crashed : IDisposable
    {
        private readonly ClassicDesktopStyleApplicationLifetime _lifetime = new();
        private App? _app;
        private CrashReporting? _crashes;

        internal TempVault Fixture { get; } = new();

        internal FakeClipboard Clipboard { get; } = new();

        internal App App => _app ?? throw new InvalidOperationException("not launched");

        internal CrashReporting Crashes => _crashes ?? throw new InvalidOperationException("not launched");

        internal Window Main => _lifetime.MainWindow ?? throw new InvalidOperationException("not launched");

        internal AppVaultSession Session { get; set; } = null!;

        internal int? Ended { get; private set; }

        internal int Endings { get; private set; }

        internal void Launch()
        {
            var preferences = new DesktopPreferences(Fixture.Home);
            _crashes = new CrashReporting(Fixture.Home, preferences, endpoint: null);
            Attach(_lifetime);
            _app = new App
            {
                Preferences = preferences,
                Crashes = _crashes,
                Clipboards = _ => Clipboard,
                Ending = code =>
                {
                    Ended = code;
                    Endings++;
                },
            };
            _app.Launch(_lifetime);
            _lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            Main.Show();
            WindowInput.Drain();
        }

        public void Dispose()
        {
            _app?.Dispose();
            _crashes?.Dispose();
            _lifetime.Dispose();
            Fixture.Dispose();
        }
    }
}
