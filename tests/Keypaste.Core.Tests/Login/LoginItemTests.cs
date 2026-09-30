using System.Xml;
using System.Xml.Linq;
using Keypaste.Core.Login;
using Microsoft.Win32;
using Xunit;

namespace Keypaste.Core.Tests.Login;

/// <summary>Each platform's open-at-login entry starts this app with no window, and turning it off removes it (G.4a).</summary>
public sealed class LoginItemTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-login-").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void The_Windows_run_value_quotes_the_program_and_asks_for_the_background()
    {
        var key = new FakeRunKey();
        var item = new WindowsRunKey(key, new FakeApproval(), @"C:\Program Files\keypaste\keypaste-app.exe");

        Assert.False(item.IsEnabled);
        Assert.True(item.Enable());
        Assert.Equal("\"C:\\Program Files\\keypaste\\keypaste-app.exe\" --background", key.Values[WindowsRunKey.ValueName]);
        Assert.True(item.IsEnabled);

        Assert.True(item.Disable());
        Assert.Empty(key.Values);
        Assert.False(item.IsEnabled);
    }

    [Fact]
    public void A_run_value_naming_another_copy_reads_as_off_and_enabling_replaces_it()
    {
        var key = new FakeRunKey();
        key.Values[WindowsRunKey.ValueName] = "\"C:\\old\\keypaste-app.exe\" --background";
        var item = new WindowsRunKey(key, new FakeApproval(), @"C:\new\keypaste-app.exe");

        Assert.False(item.IsEnabled);
        Assert.True(item.Enable());
        Assert.True(item.IsEnabled);
    }

    [Fact]
    public void A_value_Windows_turned_off_reads_as_off_and_enabling_turns_it_back_on()
    {
        var key = new FakeRunKey();
        var approval = new FakeApproval();
        var item = new WindowsRunKey(key, approval, @"C:\keypaste\keypaste-app.exe");
        Assert.True(item.Enable());

        approval.Disabled.Add(WindowsRunKey.ValueName);

        Assert.False(item.IsEnabled);
        Assert.True(item.Enable());
        Assert.Empty(approval.Disabled);
        Assert.True(item.IsEnabled);
    }

    [Fact]
    public void A_program_path_holding_a_quote_is_refused_on_Windows()
    {
        var key = new FakeRunKey();

        Assert.False(new WindowsRunKey(key, new FakeApproval(), "C:\\odd\"name\\keypaste-app.exe").Enable());
        Assert.Empty(key.Values);
    }

    [Fact]
    public void The_registry_run_key_writes_reads_and_deletes_under_the_current_user()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists on Windows only.");

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var subKey = $@"Software\keypaste-tests\{Guid.NewGuid():n}";

        try
        {
            var item = new WindowsRunKey(new RegistryRunKey(subKey), new RegistryStartupApproval($@"{subKey}\Approved"), @"C:\keypaste\keypaste-app.exe");

            Assert.True(item.Enable());
            Assert.True(item.IsEnabled);

            // What Task Manager writes when it turns a startup app off: an odd first byte, then when.
            byte[] off = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

            using (var approved = Registry.CurrentUser.CreateSubKey($@"{subKey}\Approved"))
            {
                approved.SetValue(WindowsRunKey.ValueName, off, RegistryValueKind.Binary);
            }

            Assert.False(item.IsEnabled);
            Assert.True(item.Enable());
            Assert.True(item.IsEnabled);
            Assert.True(item.Disable());
            Assert.False(item.IsEnabled);
            Assert.True(item.Disable());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void The_macOS_launch_agent_starts_the_program_in_the_background_at_load()
    {
        var program = "/Applications/key & paste.app/Contents/MacOS/keypaste-app";
        var item = new MacLaunchAgent(_home, program);

        Assert.False(item.IsEnabled);
        Assert.True(item.Enable());
        Assert.Equal(Path.Combine(_home, "Library", "LaunchAgents", "com.keypaste.app.plist"), item.FilePath);
        Assert.True(item.IsEnabled);

        List<XElement> plist;

        using (var reader = XmlReader.Create(item.FilePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
        {
            plist = [.. XDocument.Load(reader).Root!.Element("dict")!.Elements()];
        }

        string[] arguments = [.. After(plist, "ProgramArguments").Elements("string").Select(e => e.Value)];
        Assert.Equal(new[] { program, "--background" }, arguments);
        Assert.Equal("com.keypaste.app", After(plist, "Label").Value);
        Assert.Equal("true", After(plist, "RunAtLoad").Name.LocalName);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(item.FilePath);
            Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        }

        Assert.True(item.Disable());
        Assert.False(File.Exists(item.FilePath));
        Assert.True(item.Disable());
    }

    [Fact]
    public void A_launch_agent_for_another_copy_reads_as_off()
    {
        Assert.True(new MacLaunchAgent(_home, "/old/keypaste-app").Enable());

        Assert.False(new MacLaunchAgent(_home, "/new/keypaste-app").IsEnabled);
    }

    [Fact]
    public void The_XDG_entry_quotes_the_program_for_the_desktop_entry_spec()
    {
        var directory = XdgAutostart.DirectoryFor(null, _home);
        var item = new XdgAutostart(directory, "/opt/key paste/$HOME/100%/a\"b\\c/keypaste-app");

        Assert.Equal(Path.Combine(_home, ".config", "autostart"), directory);
        Assert.False(item.IsEnabled);
        Assert.True(item.Enable());
        Assert.True(item.IsEnabled);

        var lines = File.ReadAllLines(Path.Combine(directory, "keypaste.desktop"));
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains("Type=Application", lines);
        Assert.Contains("Exec=\"/opt/key paste/\\\\$HOME/100%%/a\\\\\"b\\\\\\\\c/keypaste-app\" --background", lines);

        Assert.True(item.Disable());
        Assert.False(item.IsEnabled);
    }

    [Fact]
    public void The_XDG_directory_follows_an_absolute_config_home_only()
    {
        var config = Path.Combine(_home, "config");

        Assert.Equal(Path.Combine(config, "autostart"), XdgAutostart.DirectoryFor(config, _home));
        Assert.Equal(Path.Combine(_home, ".config", "autostart"), XdgAutostart.DirectoryFor("relative", _home));
    }

    [Fact]
    public void A_program_path_with_a_line_break_is_refused_on_Linux()
    {
        var item = new XdgAutostart(_home, "/opt/keypaste\n/keypaste-app");

        Assert.False(item.Enable());
        Assert.False(File.Exists(item.FilePath));
    }

    [Fact]
    public void Inside_an_AppImage_the_login_starts_the_image_and_not_its_mount()
    {
        var image = Path.Combine(_home, "keypaste.AppImage");
        var mount = Path.Combine(_home, "mount");
        var app = Path.Combine(mount, "usr", "bin");

        Assert.Equal(Path.GetFullPath(image), LoginItems.ProgramPath(Path.Combine(app, "keypaste-app"), app, image, mount));
        Assert.Equal("/usr/bin/keypaste-app", LoginItems.ProgramPath("/usr/bin/keypaste-app", "/usr/bin", image, mount));
        Assert.Null(LoginItems.ProgramPath(null, app, null, null));
    }

    [Fact]
    public void Only_the_background_flag_starts_without_a_window()
    {
        Assert.True(LoginItems.StartsInBackground(["keypaste-app", "--background"]));
        Assert.False(LoginItems.StartsInBackground(["keypaste-app"]));
        Assert.False(LoginItems.StartsInBackground(["keypaste-app", "--BACKGROUND"]));
    }

    private static XElement After(List<XElement> dict, string key) =>
        dict[dict.FindIndex(e => e.Name.LocalName == "key" && e.Value == key) + 1];

    private sealed class FakeApproval : IStartupApproval
    {
        internal HashSet<string> Disabled { get; } = new(StringComparer.Ordinal);

        public bool IsDisabled(string name) => Disabled.Contains(name);

        public bool Clear(string name)
        {
            Disabled.Remove(name);
            return true;
        }
    }

    private sealed class FakeRunKey : IRunKey
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? Read(string name) => Values.GetValueOrDefault(name);

        public bool Write(string name, string value)
        {
            Values[name] = value;
            return true;
        }

        public bool Delete(string name)
        {
            Values.Remove(name);
            return true;
        }
    }
}
