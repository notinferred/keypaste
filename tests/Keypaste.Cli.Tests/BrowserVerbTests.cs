using System.Text.Json;
using System.Text.RegularExpressions;
using Keypaste.Core.Browser;
using Keypaste.Core.Clients;
using Microsoft.Win32;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste browser</c> lists who each browser starts for keypaste's extension and whether the extension is installed, and
/// turns this keypaste on and off as that host, under the harness's folders and, on Windows, its own registry branch (8.1e).
/// </summary>
public sealed partial class BrowserVerbTests : IDisposable
{
    private const string _otherProgram = "/opt/other/host";

    private readonly CliHarness _harness = new();
    private readonly HostPlaces _places;
    private readonly string _program;

    public BrowserVerbTests()
    {
        _harness.Environment["XDG_CONFIG_HOME"] = Path.Combine(_harness.Directory, "config");
        _harness.Environment["LOCALAPPDATA"] = Path.Combine(_harness.Directory, "local");
        _places = HostPlaces.From(name => _harness.Environment.GetValueOrDefault(name), McpClientMachine.CurrentPlatform, _harness.HostRegistryRoot);

        _harness.ProcessPath = Path.Combine(_harness.Directory, "bin", McpServerLocator.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(_harness.ProcessPath)!);
        File.WriteAllText(_harness.ProcessPath, string.Empty);
        Assert.True(HostProgram.TryForTheCli(_harness.ProcessPath, appImage: null, out var program, out var error), error);
        _program = program.Path;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void With_nothing_set_up_it_lists_the_five_and_writes_nothing()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));

        foreach (var browser in HostPlaces.Browsers)
        {
            Assert.Equal($"{browser}  browser not found  not set up", Row(browser));
            Assert.False(File.Exists(_places.ManifestFile(browser)));
        }
    }

    [Fact]
    public void On_names_this_keypaste_and_off_removes_it()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "on", "chrome", "edge"));

        Assert.Contains($"Chrome · now starts {_program} for keypaste's extension", _harness.Err, StringComparison.Ordinal);
        Assert.Contains($"Edge · now starts {_program} for keypaste's extension", _harness.Err, StringComparison.Ordinal);
        Assert.True(File.Exists(_places.ManifestFile(HostBrowser.Chrome)));
        Assert.True(File.Exists(_places.ManifestFile(HostBrowser.Edge)));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));
        Assert.Equal($"Chrome  browser not found  keypaste at {_program}", Row(HostBrowser.Chrome));
        Assert.Equal($"Edge  browser not found  keypaste at {_program}", Row(HostBrowser.Edge));
        Assert.EndsWith("not set up", Row(HostBrowser.Chromium), StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "on", "chrome"));
        Assert.Contains("Chrome · already on", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "off", "chrome", "edge"));
        Assert.Contains("Chrome · off; keypaste's manifest is removed", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(_places.ManifestFile(HostBrowser.Chrome)));
        Assert.False(File.Exists(_places.ManifestFile(HostBrowser.Edge)));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "off", "edge"));
        Assert.Contains("Edge · already off", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_programs_manifest_is_reported_left_by_off_and_replaced_by_on_by_name()
    {
        var other = PlantOther(HostBrowser.Firefox);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));
        Assert.EndsWith($"another program: {_otherProgram}", Row(HostBrowser.Firefox), StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "off", "firefox"));
        Assert.Contains($"Firefox · another program's manifest is there ({_otherProgram}); keypaste leaves it", _harness.Err, StringComparison.Ordinal);
        Assert.True(File.Exists(other));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "on", "firefox"));
        Assert.Contains(
            $"Firefox · now starts {_program} for keypaste's extension; it replaced {_otherProgram}, which Firefox started before",
            _harness.Err,
            StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));
        Assert.EndsWith($"keypaste at {_program}", Row(HostBrowser.Firefox), StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_naming_a_keypaste_that_is_gone_says_so()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "on", "chromium"));
        File.Delete(_harness.ProcessPath!);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));
        Assert.EndsWith($"keypaste at {_program}, which no longer exists", Row(HostBrowser.Chromium), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_browser_is_a_usage_error_naming_the_five_and_changes_nothing()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("browser", "on", "chrome", "safari"));

        Assert.Contains("keypaste browser on: unknown browser 'safari'; name chrome, chromium, edge, brave, firefox", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(_places.ManifestFile(HostBrowser.Chrome)));
    }

    [Fact]
    public void A_keypaste_run_through_another_program_is_refused()
    {
        _harness.ProcessPath = Path.Combine(_harness.Directory, "dotnet");

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("browser", "on", "chrome"));

        Assert.Contains("keypaste browser on: ", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(_places.ManifestFile(HostBrowser.Chrome)));
    }

    [Fact]
    public void Json_is_one_object_per_browser_with_stable_keys()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "on", "edge"));
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "--json"));

        using var json = JsonDocument.Parse(_harness.Out);
        var rows = json.RootElement.EnumerateArray().ToList();

        Assert.Equal(["chrome", "chromium", "edge", "brave", "firefox"], rows.Select(row => row.GetProperty("browser").GetString()));
        Assert.All(rows, row => Assert.Equal(["browser", "state", "program", "extension", "shares_with"], row.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(("not-set-up", JsonValueKind.Null, "browser-not-found"), (rows[0].GetProperty("state").GetString(), rows[0].GetProperty("program").ValueKind, rows[0].GetProperty("extension").GetString()));
        Assert.Equal(("keypaste", _program), (rows[2].GetProperty("state").GetString(), rows[2].GetProperty("program").GetString()));
        Assert.Equal(OperatingSystem.IsWindows() ? ["brave"] : [], rows[0].GetProperty("shares_with").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public void The_extension_is_found_in_a_profile_folder_that_holds_it()
    {
        Directory.CreateDirectory(Path.Combine(_places.ProfileFolders(HostBrowser.Chrome)[0], "Default", "Extensions", BrowserIdentity.ChromeWebStore.Id));
        Directory.CreateDirectory(Path.Combine(_places.ProfileFolders(HostBrowser.Edge)[0], "Profile 1"));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));

        Assert.StartsWith("Chrome  installed  ", Row(HostBrowser.Chrome), StringComparison.Ordinal);
        Assert.StartsWith("Edge  not found  ", Row(HostBrowser.Edge), StringComparison.Ordinal);
        Assert.StartsWith("Firefox  browser not found  ", Row(HostBrowser.Firefox), StringComparison.Ordinal);
    }

    [Fact]
    public void On_Windows_Brave_turns_with_Chrome_and_elsewhere_alone()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser", "on", "brave"));

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("Brave · now starts", _harness.Err, StringComparison.Ordinal);
            Assert.Contains("; Chrome reads the same manifest, so it is on too", _harness.Err, StringComparison.Ordinal);
            _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));
            Assert.Contains("keypaste browser: Brave reads Chrome's manifest, so they turn on and off together", _harness.Err, StringComparison.Ordinal);
            Assert.EndsWith($"keypaste at {_program}", Row(HostBrowser.Chrome), StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("same manifest", _harness.Err, StringComparison.Ordinal);
            _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("browser"));
            Assert.EndsWith("not set up", Row(HostBrowser.Chrome), StringComparison.Ordinal);
        }
    }

    /// <summary>A browser's last row in the list, its margin dropped and its columns two spaces apart.</summary>
    private string Row(HostBrowser browser) =>
        _harness.Out.Split(Environment.NewLine).Select(line => Columns().Replace(line.Trim(), "  ")).Last(line => line.StartsWith(browser + "  ", StringComparison.Ordinal));

    [GeneratedRegex(" {2,}")]
    private static partial Regex Columns();

    private string PlantOther(HostBrowser browser)
    {
        var file = OperatingSystem.IsWindows() ? Path.Combine(_harness.Directory, "other", "host.json") : _places.ManifestFile(browser)!;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"{{\"name\":\"{HostPlaces.HostName}\",\"description\":\"another\",\"path\":\"{_otherProgram}\",\"type\":\"stdio\",\"allowed_origins\":[]}}");

        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(_places.RegistryKey(browser));
            key.SetValue(null, file);
        }

        return file;
    }
}
