using System.Text.RegularExpressions;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Browser;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Settings › Browser fill turns keypaste on and off as each browser's host for its extension, says who each browser starts
/// and whether the extension is in it, and asks before replacing another program's manifest; each start re-points a stale one (8.1e).
/// </summary>
public sealed partial class BrowserHostSettingsTests : IDisposable
{
    private const string _other = "/opt/other/host";

    private readonly TempBrowserHosts _browsers = new();

    public void Dispose() => _browsers.Dispose();

    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?|relay)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [Fact]
    public void Five_browsers_none_set_up_each_offer_Turn_on()
    {
        var screen = Screen();

        Assert.True(screen.IsAvailable);
        Assert.Equal(["Chrome", "Chromium", "Edge", "Brave", "Firefox"], screen.Rows.Select(row => row.Name));
        Assert.All(screen.Rows, row => Assert.Equal($"Not set up. Turn on so {row.Name} can start keypaste for its extension.", row.Answer));
        Assert.All(screen.Rows, row => Assert.Equal($"{row.Name} isn't installed.", row.Extension));
        Assert.All(screen.Rows, row => Assert.Equal(("Turn on", $"Turn on for {row.Name}"), (row.ActionLabel, row.ActionName)));
        Assert.Equal(OperatingSystem.IsWindows() ? "Chrome and Brave read one setting, so they turn on and off together." : string.Empty, screen.SharedText);
        Assert.False(screen.HasMessage);
    }

    [Fact]
    public void Turn_on_writes_the_manifest_and_Turn_off_removes_it()
    {
        var screen = Screen();
        var manifest = _browsers.Places.ManifestFile(HostBrowser.Firefox)!;

        screen.ChangeCommand.Press(Row(screen, HostBrowser.Firefox));

        Assert.True(File.Exists(manifest));
        Assert.Contains(_browsers.Program.Replace(@"\", @"\\", StringComparison.Ordinal), File.ReadAllText(manifest), StringComparison.Ordinal);
        Assert.Equal($"Firefox starts keypaste at {_browsers.Program}.", Row(screen, HostBrowser.Firefox).Answer);
        Assert.Equal(("Turn off", "Turn off for Firefox"), (Row(screen, HostBrowser.Firefox).ActionLabel, Row(screen, HostBrowser.Firefox).ActionName));
        Assert.Equal($"Firefox now starts keypaste at {_browsers.Program}.", screen.Message);

        screen.ChangeCommand.Press(Row(screen, HostBrowser.Firefox));

        Assert.False(File.Exists(manifest));
        Assert.Equal("Not set up. Turn on so Firefox can start keypaste for its extension.", Row(screen, HostBrowser.Firefox).Answer);
        Assert.Equal("Firefox no longer starts keypaste.", screen.Message);
    }

    [Fact]
    public void On_Windows_turning_on_Brave_says_Chrome_is_on_too()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has Brave read Chrome's manifest.");

        var screen = Screen();
        screen.ChangeCommand.Press(Row(screen, HostBrowser.Brave));

        Assert.Equal($"Brave now starts keypaste at {_browsers.Program}. Chrome reads the same setting, so it is on too.", screen.Message);
        Assert.True(Row(screen, HostBrowser.Chrome).IsOn);
    }

    [Fact]
    public void Another_programs_manifest_is_named_and_replaced_only_once_confirmed()
    {
        _browsers.PlantOther(HostBrowser.Edge, _other);
        var screen = Screen();
        var edge = Row(screen, HostBrowser.Edge);

        Assert.Equal($"Edge starts another program for keypaste's extension: {_other}.", edge.Answer);
        Assert.Equal("Turn on", edge.ActionLabel);

        screen.ChangeCommand.Press(edge);
        Assert.True(edge.IsConfirmingReplace);
        Assert.Equal($"Turning on replaces {_other} for Edge, and keypaste can't put it back.", edge.ReplacePrompt);
        Assert.Equal(HostState.Other, _browsers.Hosts().Read(HostBrowser.Edge).State);

        screen.CancelReplaceCommand.Press(edge);
        Assert.False(edge.IsConfirmingReplace);
        Assert.Equal(HostState.Other, _browsers.Hosts().Read(HostBrowser.Edge).State);

        screen.ChangeCommand.Press(edge);
        screen.ReplaceCommand.Press(edge);

        Assert.Equal(HostState.Keypaste, _browsers.Hosts().Read(HostBrowser.Edge).State);
        Assert.Equal($"Edge now starts keypaste at {_browsers.Program}, in place of {_other}.", screen.Message);
        Assert.Equal($"Edge starts keypaste at {_browsers.Program}.", Row(screen, HostBrowser.Edge).Answer);
    }

    [Fact]
    public void A_manifest_naming_a_keypaste_that_is_gone_says_so_and_Turn_on_points_it_here()
    {
        var gone = Path.Combine(_browsers.Root, "gone", "keypaste");
        Assert.Equal(HostOutcome.On, _browsers.Hosts(new HostProgram(gone)).TurnOn(HostBrowser.Chromium).Outcome);
        var screen = Screen();

        Assert.Equal($"Chromium looks for keypaste at {gone}, which no longer exists. Turn on to point it at this keypaste.", Row(screen, HostBrowser.Chromium).Answer);
        Assert.Equal("Turn on", Row(screen, HostBrowser.Chromium).ActionLabel);

        screen.ChangeCommand.Press(Row(screen, HostBrowser.Chromium));

        Assert.Equal($"Chromium starts keypaste at {_browsers.Program}.", Row(screen, HostBrowser.Chromium).Answer);
    }

    [Fact]
    public void The_extension_line_reads_the_browsers_folders_and_names_the_store_to_get_it_from()
    {
        _browsers.Profile(HostBrowser.Chrome, withExtension: false);
        _browsers.Profile(HostBrowser.Edge, withExtension: false);
        _browsers.Profile(HostBrowser.Brave, withExtension: true);
        var firefox = Path.Combine(_browsers.Places.ProfileFolders(HostBrowser.Firefox)[0], "abcd1234.default-release");
        TempBrowserHosts.Made(Path.Combine(firefox, "prefs.js"));
        var screen = Screen();

        Assert.Equal("keypaste's extension isn't installed in Chrome. Get it from the Chrome Web Store.", Row(screen, HostBrowser.Chrome).Extension);
        Assert.Equal("keypaste's extension isn't installed in Edge. Get it from Microsoft Edge Add-ons.", Row(screen, HostBrowser.Edge).Extension);
        Assert.Equal("keypaste's extension isn't installed in Firefox. Get it from Firefox Add-ons.", Row(screen, HostBrowser.Firefox).Extension);
        Assert.Equal("keypaste's extension is installed in Brave.", Row(screen, HostBrowser.Brave).Extension);
        Assert.Equal("Chromium isn't installed.", Row(screen, HostBrowser.Chromium).Extension);
    }

    [Fact]
    public void A_change_that_fails_says_why_and_leaves_the_row_as_it_was()
    {
        var screen = new BrowserHostsViewModel(_browsers.Hosts(new HostProgram(_browsers.Program, () => "/home/person/.local/bin/keypaste could not be made")), string.Empty);

        screen.ChangeCommand.Press(Row(screen, HostBrowser.Chromium));

        Assert.Equal("Chromium was not turned on: /home/person/.local/bin/keypaste could not be made. Try again.", screen.Message);
        Assert.False(Row(screen, HostBrowser.Chromium).IsOn);
        Assert.False(File.Exists(_browsers.Places.ManifestFile(HostBrowser.Chromium)));
    }

    [Fact]
    public void Without_a_keypaste_to_name_the_reason_shows_in_place_of_the_browsers()
    {
        var screen = new BrowserHostsViewModel(null, "keypaste was not found beside the app.");

        Assert.False(screen.IsAvailable);
        Assert.Empty(screen.Rows);
        Assert.Equal("keypaste can't be set up in browsers: keypaste was not found beside the app.", screen.UnavailableText);
        Assert.False(screen.HasSharedText);
    }

    [Fact]
    public void A_start_re_points_a_manifest_naming_a_keypaste_that_is_gone_before_the_app_runs()
    {
        using var home = new TempHome();
        var gone = Path.Combine(_browsers.Root, "gone", "keypaste");
        _browsers.Hosts(new HostProgram(gone)).TurnOn(HostBrowser.Chromium);
        HostState? beforeTheApp = null;

        Assert.Equal(7, Program.Run([], home.Path, null, _ => { beforeTheApp = _browsers.Hosts().Read(HostBrowser.Chromium).State; return 7; }, TextWriter.Null, TimeSpan.FromSeconds(10), _browsers.Hosts()));

        Assert.Equal(HostState.Keypaste, beforeTheApp);
        Assert.Equal(_browsers.Program, _browsers.Hosts().Read(HostBrowser.Chromium).Program);
    }

    [Fact]
    public Task Settings_lists_the_browsers_above_the_pairings_and_names_each_button() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home), null, () => new NobodyToAsk());

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start)) { BrowserHosts = _browsers.Setup };
        shell.Current = Destinations.Of(DestinationKind.Settings);
        var settings = Assert.IsType<SettingsViewModel>(shell.Content);
        settings.Section = SettingsSectionChoice.Browser;
        var window = new Window { Content = new SettingsView { DataContext = settings } };
        window.Show();
        WindowInput.Drain();

        var hosts = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "BrowserHosts");
        Assert.True(hosts.IsEffectivelyVisible);
        Assert.True(AppCommands.HasContextMenu(settings.BrowserHosts!.Rows[0]));

        var names = hosts.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
            .Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()).ToList();
        Assert.Equal(["Turn on for Chrome", "Turn on for Chromium", "Turn on for Edge", "Turn on for Brave", "Turn on for Firefox"], names);

        var said = hosts.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
            .Select(block => (block.Text ?? string.Empty).Replace(_browsers.Root, string.Empty, StringComparison.Ordinal)).ToList();
        Assert.Contains("Browsers", said);
        Assert.DoesNotContain(said, text => Retired().IsMatch(text));

        window.Close();
    });

    private BrowserHostsViewModel Screen() => new(_browsers.Hosts(), string.Empty);

    private static BrowserHostRow Row(BrowserHostsViewModel screen, HostBrowser browser) => screen.Rows.Single(row => row.Browser == browser);
}
