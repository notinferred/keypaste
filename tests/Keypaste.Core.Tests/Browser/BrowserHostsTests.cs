using System.Text;
using System.Text.Json;
using Keypaste.Core.Browser;
using Keypaste.Core.Clients;
using Microsoft.Win32;
using Xunit;

namespace Keypaste.Core.Tests.Browser;

/// <summary>
/// keypaste turns itself on and off as each browser's host for its extension at the user-level place the browser reads, under
/// a temporary home and, on Windows, a throwaway registry branch, and touches only a manifest it wrote (8.1e).
/// </summary>
public sealed class BrowserHostsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-hosts-").FullName;
    private readonly string _registryRoot = $@"Software\keypaste-tests\{Guid.NewGuid():n}";
    private readonly HostPlaces _places;
    private readonly string _program;

    public BrowserHostsTests()
    {
        var variables = new Dictionary<string, string>
        {
            ["HOME"] = Path.Combine(_root, "home"),
            ["USERPROFILE"] = Path.Combine(_root, "home"),
            ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
            ["APPDATA"] = Path.Combine(_root, "roaming"),
            ["LOCALAPPDATA"] = Path.Combine(_root, "local"),
        };

        _places = HostPlaces.From(name => variables.GetValueOrDefault(name), McpClientMachine.CurrentPlatform, _registryRoot);
        _program = Path.Combine(_root, "bin", OperatingSystem.IsWindows() ? "keypaste.exe" : "keypaste");
        Directory.CreateDirectory(Path.GetDirectoryName(_program)!);
        File.WriteAllText(_program, string.Empty);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, throwOnMissingSubKey: false);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Each_browser_reads_its_manifest_where_KeePassXC_writes_its_own_under_keypastes_host_name()
    {
        string[] expected = McpClientMachine.CurrentPlatform switch
        {
            McpClientPlatform.Windows =>
            [
                @"local\keypaste\NativeMessagingHosts\com.keypaste.browser_chrome.json",
                @"local\keypaste\NativeMessagingHosts\com.keypaste.browser_chromium.json",
                @"local\keypaste\NativeMessagingHosts\com.keypaste.browser_edge.json",
                @"local\keypaste\NativeMessagingHosts\com.keypaste.browser_chrome.json",
                @"local\keypaste\NativeMessagingHosts\com.keypaste.browser_firefox.json",
            ],
            McpClientPlatform.MacOS =>
            [
                "home/Library/Application Support/Google/Chrome/NativeMessagingHosts/com.keypaste.browser.json",
                "home/Library/Application Support/Chromium/NativeMessagingHosts/com.keypaste.browser.json",
                "home/Library/Application Support/Microsoft Edge/NativeMessagingHosts/com.keypaste.browser.json",
                "home/Library/Application Support/BraveSoftware/Brave-Browser/NativeMessagingHosts/com.keypaste.browser.json",
                "home/Library/Application Support/Mozilla/NativeMessagingHosts/com.keypaste.browser.json",
            ],
            _ =>
            [
                "config/google-chrome/NativeMessagingHosts/com.keypaste.browser.json",
                "config/chromium/NativeMessagingHosts/com.keypaste.browser.json",
                "config/microsoft-edge/NativeMessagingHosts/com.keypaste.browser.json",
                "config/BraveSoftware/Brave-Browser/NativeMessagingHosts/com.keypaste.browser.json",
                "home/.mozilla/native-messaging-hosts/com.keypaste.browser.json",
            ],
        };

        Assert.Equal(expected.Select(path => Path.Combine(_root, path)), HostPlaces.Browsers.Select(browser => _places.ManifestFile(browser)));

        if (McpClientMachine.CurrentPlatform == McpClientPlatform.Windows)
        {
            Assert.Equal(
                [
                    $@"{_registryRoot}\Google\Chrome\NativeMessagingHosts\com.keypaste.browser",
                    $@"{_registryRoot}\Chromium\NativeMessagingHosts\com.keypaste.browser",
                    $@"{_registryRoot}\Microsoft\Edge\NativeMessagingHosts\com.keypaste.browser",
                    $@"{_registryRoot}\Google\Chrome\NativeMessagingHosts\com.keypaste.browser",
                    $@"{_registryRoot}\Mozilla\NativeMessagingHosts\com.keypaste.browser",
                ],
                HostPlaces.Browsers.Select(_places.RegistryKey));
        }
    }

    [Fact]
    public void A_variable_that_is_not_an_absolute_folder_is_unset_so_nothing_is_written_against_the_working_directory()
    {
        var places = HostPlaces.From(_ => "relative", McpClientMachine.CurrentPlatform, _registryRoot);

        Assert.All(HostPlaces.Browsers, browser => Assert.Null(places.ManifestFile(browser)));
        Assert.Equal(HostOutcome.Failed, Hosts(places).TurnOn(HostBrowser.Chrome).Outcome);
    }

    [Fact]
    public void Turning_on_writes_a_manifest_allowing_only_keypastes_three_builds_and_naming_the_program()
    {
        var hosts = Hosts();

        foreach (var browser in HostPlaces.Browsers)
        {
            var change = hosts.TurnOn(browser);
            Assert.Contains(change.Outcome, new[] { HostOutcome.On, HostOutcome.Unchanged });
            var status = hosts.Read(browser);
            Assert.Equal((HostState.Keypaste, _program, ExtensionState.BrowserNotFound), (status.State, status.Program, status.Extension));
            Assert.Equal(Shared(browser), status.SharesWith);

            var manifest = JsonDocument.Parse(File.ReadAllBytes(_places.ManifestFile(browser)!)).RootElement;
            Assert.Equal(("com.keypaste.browser", _program, "stdio"), (manifest.GetProperty("name").GetString(), manifest.GetProperty("path").GetString(), manifest.GetProperty("type").GetString()));

            string[] allowed = browser == HostBrowser.Firefox
                ? [.. manifest.GetProperty("allowed_extensions").EnumerateArray().Select(id => id.GetString()!)]
                : [.. manifest.GetProperty("allowed_origins").EnumerateArray().Select(id => id.GetString()!)];
            Assert.Equal(browser == HostBrowser.Firefox ? [BrowserIdentity.FirefoxAddOns.Caller] : [BrowserIdentity.ChromeWebStore.Caller, BrowserIdentity.EdgeAddOns.Caller], allowed);

            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(_places.RegistryKey(browser));
                Assert.Equal(_places.ManifestFile(browser), key?.GetValue(null));
            }
        }

        Assert.Equal(HostOutcome.Unchanged, hosts.TurnOn(HostBrowser.Chrome).Outcome);
    }

    [Fact]
    public void Turning_off_removes_keypastes_manifest_and_leaves_one_another_program_wrote_byte_for_byte()
    {
        var hosts = Hosts();
        Assert.Equal(HostOutcome.On, hosts.TurnOn(HostBrowser.Edge).Outcome);
        Assert.Equal(HostOutcome.Off, hosts.TurnOff(HostBrowser.Edge).Outcome);
        Assert.False(File.Exists(_places.ManifestFile(HostBrowser.Edge)));
        Assert.Equal(HostState.NotSetUp, hosts.Read(HostBrowser.Edge).State);
        Assert.Equal(HostOutcome.Unchanged, hosts.TurnOff(HostBrowser.Edge).Outcome);

        var other = PlantOther(HostBrowser.Firefox, "/opt/other/host");
        Assert.Equal(new HostChange(HostBrowser.Firefox, HostOutcome.LeftOther, Replaced: "/opt/other/host"), hosts.TurnOff(HostBrowser.Firefox));
        Assert.Equal(other, File.ReadAllBytes(OtherFile(HostBrowser.Firefox)));
        Assert.Equal((HostState.Other, "/opt/other/host"), Status(hosts, HostBrowser.Firefox));
    }

    [Fact]
    public void Turning_on_over_another_programs_manifest_names_the_program_it_replaced()
    {
        var hosts = Hosts();
        PlantOther(HostBrowser.Chromium, "/opt/other/host");

        Assert.Equal(new HostChange(HostBrowser.Chromium, HostOutcome.On, Replaced: "/opt/other/host"), hosts.TurnOn(HostBrowser.Chromium));
        Assert.Equal((HostState.Keypaste, _program), Status(hosts, HostBrowser.Chromium));
    }

    [Fact]
    public void A_manifest_naming_a_keypaste_that_is_gone_reads_as_missing_and_keeping_current_re_points_it_alone()
    {
        var gone = Path.Combine(_root, "gone", "keypaste");
        Hosts(new HostProgram(gone)).TurnOn(HostBrowser.Chrome);
        PlantOther(HostBrowser.Firefox, "/opt/other/host");
        var hosts = Hosts();

        Assert.Equal((HostState.KeypasteMissing, gone), Status(hosts, HostBrowser.Chrome));
        Assert.Equal([HostBrowser.Chrome], hosts.KeepCurrent());
        Assert.Equal((HostState.Keypaste, _program), Status(hosts, HostBrowser.Chrome));
        Assert.Equal((HostState.Other, "/opt/other/host"), Status(hosts, HostBrowser.Firefox));
        Assert.Equal((HostState.NotSetUp, null), Status(hosts, HostBrowser.Edge));
        Assert.Empty(hosts.KeepCurrent());
    }

    [Fact]
    public void Keeping_current_re_points_another_bundles_keypaste_and_leaves_a_terminals_own()
    {
        var bundled = Path.Combine(_root, "Old.app", "Contents", "MacOS", "keypaste");
        var own = Path.Combine(_root, "own", "keypaste");

        foreach (var program in new[] { bundled, own })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(program)!);
            File.WriteAllText(program, string.Empty);
        }

        Hosts(new HostProgram(bundled)).TurnOn(HostBrowser.Chrome);
        Hosts(new HostProgram(own)).TurnOn(HostBrowser.Edge);

        Assert.Equal(OperatingSystem.IsWindows() ? [] : [HostBrowser.Chrome], Hosts().KeepCurrent());
        Assert.Equal((HostState.Keypaste, own), Status(Hosts(), HostBrowser.Edge));
    }

    [Fact]
    public void A_program_named_by_a_relative_path_is_refused_since_no_browser_starts_one() =>
        Assert.Throws<ArgumentException>(() => Hosts(new HostProgram(Path.Combine("artifacts", "keypaste"))));

    [Fact]
    public void A_program_that_cannot_be_made_writes_no_manifest()
    {
        var hosts = Hosts(new HostProgram(_program, () => "~/.local/bin/keypaste could not be made"));

        Assert.Equal(new HostChange(HostBrowser.Chrome, HostOutcome.Failed, Reason: "~/.local/bin/keypaste could not be made"), hosts.TurnOn(HostBrowser.Chrome));
        Assert.False(File.Exists(_places.ManifestFile(HostBrowser.Chrome)));
    }

    [Fact]
    public void On_Windows_Brave_reads_Chromes_key_so_the_two_turn_on_and_off_together()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows looks up manifests in the registry.");

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var hosts = Hosts();
        Assert.Equal([HostBrowser.Brave], hosts.Read(HostBrowser.Chrome).SharesWith);
        Assert.Equal(HostOutcome.On, hosts.TurnOn(HostBrowser.Brave).Outcome);
        Assert.Equal(HostState.Keypaste, hosts.Read(HostBrowser.Chrome).State);
        Assert.Equal(HostOutcome.Off, hosts.TurnOff(HostBrowser.Chrome).Outcome);
        Assert.Equal(HostState.NotSetUp, hosts.Read(HostBrowser.Brave).State);
        Assert.Null(Registry.CurrentUser.OpenSubKey(_places.RegistryKey(HostBrowser.Chrome)));
    }

    [Fact]
    public void The_extension_is_found_in_any_profile_and_a_browser_with_no_profiles_is_not_found()
    {
        var hosts = Hosts();
        Assert.All(HostPlaces.Browsers, browser => Assert.Equal(ExtensionState.BrowserNotFound, hosts.Read(browser).Extension));

        var chrome = _places.ProfileFolders(HostBrowser.Chrome)[0];
        Directory.CreateDirectory(Path.Combine(chrome, "Default", "Extensions", "someotherextensionidaaaaaaaaaaa"));
        Assert.Equal(ExtensionState.NotFound, hosts.Read(HostBrowser.Chrome).Extension);
        Directory.CreateDirectory(Path.Combine(chrome, "Profile 2", "Extensions", BrowserIdentity.ChromeWebStore.Id, "1.0.0_0"));
        Assert.Equal(ExtensionState.Installed, hosts.Read(HostBrowser.Chrome).Extension);

        var edge = _places.ProfileFolders(HostBrowser.Edge)[0];
        Directory.CreateDirectory(Path.Combine(edge, "Default", "Extensions", BrowserIdentity.EdgeAddOns.Id));
        Assert.Equal(ExtensionState.Installed, hosts.Read(HostBrowser.Edge).Extension);

        var firefox = Path.Combine(_places.ProfileFolders(HostBrowser.Firefox)[0], "abcd1234.default-release");
        Directory.CreateDirectory(firefox);
        File.WriteAllText(Path.Combine(firefox, "prefs.js"), string.Empty);
        Assert.Equal(ExtensionState.NotFound, hosts.Read(HostBrowser.Firefox).Extension);
        Directory.CreateDirectory(Path.Combine(firefox, "extensions"));
        File.WriteAllText(Path.Combine(firefox, "extensions", BrowserIdentity.FirefoxAddOns.Id + ".xpi"), string.Empty);
        Assert.Equal(ExtensionState.Installed, hosts.Read(HostBrowser.Firefox).Extension);
    }

    private static (HostState, string?) Status(BrowserHosts hosts, HostBrowser browser) => hosts.Read(browser) is var status ? (status.State, status.Program) : default;

    private IReadOnlyList<HostBrowser> Shared(HostBrowser browser) => [.. _places.SharingSlot(browser).Where(other => other != browser)];

    private BrowserHosts Hosts(HostProgram? program = null) => Hosts(_places, program);

    private BrowserHosts Hosts(HostPlaces places, HostProgram? program = null) =>
        new(places, OperatingSystem.IsWindows() ? new RegistryHostKeys() : null, program ?? new HostProgram(_program));

    // Another program's manifest, where the browser looks: its own file named by the key on Windows.
    private string OtherFile(HostBrowser browser) =>
        OperatingSystem.IsWindows() ? Path.Combine(_root, "other", $"{browser}.json") : _places.ManifestFile(browser)!;

    private byte[] PlantOther(HostBrowser browser, string program)
    {
        var bytes = Encoding.UTF8.GetBytes($"{{\"name\":\"com.keypaste.browser\",\"description\":\"another\",\"path\":\"{program}\",\"type\":\"stdio\",\"allowed_origins\":[]}}");
        var file = OtherFile(browser);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);

        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(_places.RegistryKey(browser));
            key.SetValue(null, file);
        }

        return bytes;
    }
}
