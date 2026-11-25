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
/// and whether the extension is in it, and asks before replacing another program's manifest; each start re-points a stale one.
/// </summary>
public sealed partial class BrowserHostSettingsTests : IDisposable
{
    private const string _other = "/opt/other/host";

    private readonly TempBrowserHosts _browsers = new();

    public void Dispose() => _browsers.Dispose();

    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?|relay)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [Fact]
    public void Six_browsers_none_installed_go_quiet_and_leave_the_disclosure_closed()
    {
        var screen = Screen();

        Assert.True(screen.IsAvailable);
        Assert.Equal(["Chrome", "Chromium", "Edge", "Brave", "Firefox", "LibreWolf"], screen.Rows.Select(row => row.Name));
        Assert.All(screen.Rows, row => Assert.Equal(("Not installed", false, false), (row.Status, row.HasHint, row.OffersSwitch)));
        Assert.All(screen.Rows, row => Assert.Equal($"Turn on for {row.Name}", row.ActionName));
        Assert.False(screen.IsOpen);
        Assert.Equal(
            (OperatingSystem.IsLinux() ? string.Empty : "Chrome and Brave read one setting, so they turn on and off together. ")
                + "Firefox and LibreWolf read one setting, so they turn on and off together.",
            screen.SharedText);
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
        Assert.Equal((true, _browsers.Program), (Row(screen, HostBrowser.Firefox).SwitchedOn, Row(screen, HostBrowser.Firefox).Program));
        Assert.Equal("Turn off for Firefox", Row(screen, HostBrowser.Firefox).ActionName);
        Assert.Equal($"Firefox now starts keypaste at {_browsers.Program}. LibreWolf reads the same setting, so it is on too.", screen.Message);
        Assert.True(Row(screen, HostBrowser.LibreWolf).IsOn);

        screen.ChangeCommand.Press(Row(screen, HostBrowser.Firefox));

        Assert.False(File.Exists(manifest));
        Assert.False(Row(screen, HostBrowser.Firefox).SwitchedOn);
        Assert.Equal("Firefox no longer starts keypaste. LibreWolf reads the same setting, so it is off too.", screen.Message);
    }

    [Fact]
    public void Off_Linux_turning_on_Brave_says_Chrome_is_on_too()
    {
        Assert.SkipWhen(OperatingSystem.IsLinux(), "Brave reads its own manifest on Linux.");

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

        Assert.Equal((_other, false), (edge.Program, edge.SwitchedOn));

        screen.ChangeCommand.Press(edge);
        Assert.True(edge.IsConfirmingReplace);
        Assert.True(edge.SwitchedOn);
        Assert.Equal($"Turning on replaces {_other} for Edge, and keypaste can't put it back.", edge.ReplacePrompt);
        Assert.Equal(HostState.Other, _browsers.Hosts().Read(HostBrowser.Edge).State);

        screen.CancelReplaceCommand.Press(edge);
        Assert.False(edge.IsConfirmingReplace);
        Assert.False(edge.SwitchedOn);
        Assert.Equal(HostState.Other, _browsers.Hosts().Read(HostBrowser.Edge).State);

        screen.ChangeCommand.Press(edge);
        screen.ReplaceCommand.Press(edge);

        Assert.Equal(HostState.Keypaste, _browsers.Hosts().Read(HostBrowser.Edge).State);
        Assert.Equal($"Edge now starts keypaste at {_browsers.Program}, in place of {_other}.", screen.Message);
        Assert.Equal((true, _browsers.Program), (Row(screen, HostBrowser.Edge).IsOn, Row(screen, HostBrowser.Edge).Program));
    }

    [Fact]
    public void A_manifest_naming_a_keypaste_that_is_gone_says_so_and_Turn_on_points_it_here()
    {
        var gone = Path.Combine(_browsers.Root, "gone", "keypaste");
        Assert.Equal(HostOutcome.On, _browsers.Hosts(new HostProgram(gone)).TurnOn(HostBrowser.Chromium).Outcome);
        var screen = Screen();

        Assert.Equal((gone, false), (Row(screen, HostBrowser.Chromium).Program, Row(screen, HostBrowser.Chromium).IsOn));

        screen.ChangeCommand.Press(Row(screen, HostBrowser.Chromium));

        Assert.Equal((true, _browsers.Program), (Row(screen, HostBrowser.Chromium).IsOn, Row(screen, HostBrowser.Chromium).Program));
    }

    [Fact]
    public void The_status_reads_the_browsers_folders_names_the_store_and_opens_the_disclosure_for_an_unanswered_extension()
    {
        _browsers.Profile(HostBrowser.Chrome, withExtension: false);
        _browsers.Profile(HostBrowser.Edge, withExtension: false);
        _browsers.Profile(HostBrowser.Brave, withExtension: true);
        var firefox = Path.Combine(_browsers.Places.ProfileFolders(HostBrowser.Firefox)[0], "abcd1234.default-release");
        TempBrowserHosts.Made(Path.Combine(firefox, "prefs.js"));
        var screen = Screen();

        Assert.Equal(("Extension not installed", "Get it from the Chrome Web Store."), (Row(screen, HostBrowser.Chrome).Status, Row(screen, HostBrowser.Chrome).Hint));
        Assert.Equal(("Extension not installed", "Get it from Microsoft Edge Add-ons."), (Row(screen, HostBrowser.Edge).Status, Row(screen, HostBrowser.Edge).Hint));
        Assert.Equal(("Extension not installed", "Get it from Firefox Add-ons."), (Row(screen, HostBrowser.Firefox).Status, Row(screen, HostBrowser.Firefox).Hint));
        Assert.Equal(("Extension installed", true), (Row(screen, HostBrowser.Brave).Status, Row(screen, HostBrowser.Brave).OffersSwitch));
        Assert.Equal(("Not installed", false), (Row(screen, HostBrowser.Chromium).Status, Row(screen, HostBrowser.Chromium).OffersSwitch));
        Assert.True(screen.IsOpen);
    }

    [Fact]
    public void LibreWolf_with_Firefoxs_add_on_and_no_Firefox_offers_its_own_switch_and_turns_on()
    {
        var profile = Path.Combine(_browsers.Places.ProfileFolders(HostBrowser.LibreWolf)[0], "zixesjok.default-release");
        TempBrowserHosts.Made(Path.Combine(profile, "prefs.js"));
        TempBrowserHosts.Made(Path.Combine(profile, "extensions", BrowserIdentity.FirefoxAddOns.Id + ".xpi"));
        var screen = Screen();

        Assert.Equal(("Extension installed", true), (Row(screen, HostBrowser.LibreWolf).Status, Row(screen, HostBrowser.LibreWolf).OffersSwitch));
        Assert.Equal(("Not installed", false), (Row(screen, HostBrowser.Firefox).Status, Row(screen, HostBrowser.Firefox).OffersSwitch));
        Assert.True(screen.IsOpen);

        screen.ChangeCommand.Press(Row(screen, HostBrowser.LibreWolf));

        Assert.Equal($"LibreWolf now starts keypaste at {_browsers.Program}. Firefox reads the same setting, so it is on too.", screen.Message);
        Assert.True(Row(screen, HostBrowser.LibreWolf).IsOn);
        Assert.False(Row(screen, HostBrowser.LibreWolf).NeedsAttention);
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
    public Task Settings_lists_the_browsers_after_the_pairings_open_while_one_needs_attention_and_names_each_switch() => HeadlessSession.On(() =>
    {
        _browsers.Profile(HostBrowser.Chrome, withExtension: true);
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
        Assert.True(settings.BrowserHosts!.IsOpen);
        Assert.True(AppCommands.HasContextMenu(settings.BrowserHosts.Rows[0]));
        var sections = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "BrowserFillCard").Children;
        Assert.Equal(sections.Count - 1, sections.IndexOf(hosts));

        var names = hosts.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
            .Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()).ToList();
        Assert.Equal(["Browsers", "Turn on for Chrome"], names);

        var said = hosts.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
            .Select(block => (block.Text ?? string.Empty).Replace(_browsers.Root, string.Empty, StringComparison.Ordinal)).ToList();
        Assert.Contains("Browsers", said);
        Assert.DoesNotContain(said, text => Retired().IsMatch(text));

        window.Close();
    });

    private BrowserHostsViewModel Screen() => new(_browsers.Hosts(), string.Empty);

    private static BrowserHostRow Row(BrowserHostsViewModel screen, HostBrowser browser) => screen.Rows.Single(row => row.Browser == browser);
}
