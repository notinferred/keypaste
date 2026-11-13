using System.Text;
using Keypaste.App.Session;
using Keypaste.Core.Browser;
using Keypaste.Core.Clients;
using Microsoft.Win32;

namespace Keypaste.App.Tests;

/// <summary>Browsers' manifest and profile folders under a temporary home and, on Windows, a throwaway registry branch, with a <c>keypaste</c> for them to name.</summary>
internal sealed class TempBrowserHosts : IDisposable
{
    private readonly string _registryRoot = $@"Software\keypaste-tests\{Guid.NewGuid():n}";

    internal TempBrowserHosts()
    {
        Root = Directory.CreateTempSubdirectory("keypaste-browsers-").FullName;
        var variables = new Dictionary<string, string>
        {
            ["HOME"] = Path.Combine(Root, "home"),
            ["USERPROFILE"] = Path.Combine(Root, "home"),
            ["XDG_CONFIG_HOME"] = Path.Combine(Root, "config"),
            ["APPDATA"] = Path.Combine(Root, "roaming"),
            ["LOCALAPPDATA"] = Path.Combine(Root, "local"),
        };

        Places = HostPlaces.From(name => variables.GetValueOrDefault(name), McpClientMachine.CurrentPlatform, _registryRoot);
        Program = Made(Path.Combine(Root, "bin", OperatingSystem.IsWindows() ? "keypaste.exe" : "keypaste"));
    }

    internal string Root { get; }

    internal HostPlaces Places { get; }

    /// <summary>The <c>keypaste</c> the hosts name, which exists.</summary>
    internal string Program { get; }

    /// <summary>What the shell is given, naming <see cref="Program"/>.</summary>
    internal BrowserHostSetup Setup => new(Hosts(), string.Empty);

    internal BrowserHosts Hosts(HostProgram? program = null) =>
        new(Places, OperatingSystem.IsWindows() ? new RegistryHostKeys() : null, program ?? new HostProgram(Program));

    /// <summary>A manifest another program wrote where <paramref name="browser"/> looks, naming <paramref name="program"/>.</summary>
    internal void PlantOther(HostBrowser browser, string program)
    {
        var file = OperatingSystem.IsWindows() ? Path.Combine(Root, "other", $"{browser}.json") : Places.ManifestFile(browser)!;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes($"{{\"name\":\"com.keypaste.browser\",\"description\":\"another\",\"path\":\"{program}\",\"type\":\"stdio\",\"allowed_origins\":[]}}"));

        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(Places.RegistryKey(browser));
            key.SetValue(null, file);
        }
    }

    /// <summary>A Chromium-family browser's default profile, holding keypaste's extension or not.</summary>
    internal void Profile(HostBrowser browser, bool withExtension)
    {
        var extensions = Path.Combine(Places.ProfileFolders(browser)[0], "Default", "Extensions");
        Directory.CreateDirectory(withExtension ? Path.Combine(extensions, BrowserIdentity.ChromeWebStore.Id) : extensions);
    }

    internal static string Made(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, throwOnMissingSubKey: false);
        }

        Directory.Delete(Root, recursive: true);
    }
}
