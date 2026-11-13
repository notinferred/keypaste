using Keypaste.Core.Clients;

namespace Keypaste.Core.Browser;

/// <summary>A browser keypaste can be the host of its extension for.</summary>
public enum HostBrowser
{
    /// <summary>Google Chrome.</summary>
    Chrome,

    /// <summary>Chromium.</summary>
    Chromium,

    /// <summary>Microsoft Edge.</summary>
    Edge,

    /// <summary>Brave.</summary>
    Brave,

    /// <summary>Firefox.</summary>
    Firefox,
}

/// <summary>Where each browser reads the user's manifest for <see cref="HostName"/> and keeps its profiles, under one user's folders.</summary>
/// <param name="Platform">The operating system.</param>
/// <param name="Home"><c>HOME</c>, or <c>USERPROFILE</c> on Windows.</param>
/// <param name="ConfigHome">On Linux, <c>XDG_CONFIG_HOME</c>, else <c>~/.config</c>.</param>
/// <param name="AppData"><c>APPDATA</c>, on Windows.</param>
/// <param name="LocalAppData"><c>LOCALAPPDATA</c>, on Windows.</param>
/// <param name="RegistryRoot">The key under <c>HKCU</c> the browsers' keys are read under: <c>Software</c>, or a test's own.</param>
/// <remarks>A folder that is not absolute is taken as unset, so nothing is ever written against the directory keypaste runs in.</remarks>
public sealed record HostPlaces(
    McpClientPlatform Platform,
    string? Home,
    string? ConfigHome,
    string? AppData,
    string? LocalAppData,
    string RegistryRoot)
{
    /// <summary>The host name keypaste's extension asks its browser to start.</summary>
    public const string HostName = "com.keypaste.browser";

    /// <summary>The key under <c>HKCU</c> each browser reads.</summary>
    public const string DefaultRegistryRoot = "Software";

    /// <summary>The five, in the order Settings lists them.</summary>
    public static IReadOnlyList<HostBrowser> Browsers { get; } = [HostBrowser.Chrome, HostBrowser.Chromium, HostBrowser.Edge, HostBrowser.Brave, HostBrowser.Firefox];

    /// <summary>The places of the user this process runs as.</summary>
    /// <returns>The places, with the profile's own folders where a variable is unset.</returns>
    public static HostPlaces ForThisUser()
    {
        var places = From(Environment.GetEnvironmentVariable, McpClientMachine.CurrentPlatform, DefaultRegistryRoot);

        return places with
        {
            Home = places.Home ?? Folder(Environment.SpecialFolder.UserProfile),
            AppData = places.Platform == McpClientPlatform.Windows ? places.AppData ?? Folder(Environment.SpecialFolder.ApplicationData) : null,
            LocalAppData = places.Platform == McpClientPlatform.Windows ? places.LocalAppData ?? Folder(Environment.SpecialFolder.LocalApplicationData) : null,
            ConfigHome = places.Platform == McpClientPlatform.Linux ? places.ConfigHome ?? Under(places.Home ?? Folder(Environment.SpecialFolder.UserProfile), ".config") : null,
        };
    }

    /// <summary>Places read only from <paramref name="variable"/>, never from the real profile.</summary>
    /// <param name="variable">The environment, such as the CLI's probe.</param>
    /// <param name="platform">The platform whose variables to read.</param>
    /// <param name="registryRoot">The key under <c>HKCU</c>, <see cref="DefaultRegistryRoot"/> unless a test names its own.</param>
    /// <returns>The places.</returns>
    public static HostPlaces From(Func<string, string?> variable, McpClientPlatform platform, string registryRoot = DefaultRegistryRoot)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentException.ThrowIfNullOrEmpty(registryRoot);

        string? Absolute(string name) => variable(name) is { Length: > 0 } value && McpClientMachine.IsAbsolute(value, platform) ? value : null;

        return platform switch
        {
            McpClientPlatform.Windows => new(platform, Absolute("USERPROFILE"), ConfigHome: null, Absolute("APPDATA"), Absolute("LOCALAPPDATA"), registryRoot),
            McpClientPlatform.Linux => new(platform, Absolute("HOME"), Absolute("XDG_CONFIG_HOME") ?? Under(Absolute("HOME"), ".config"), AppData: null, LocalAppData: null, registryRoot),
            _ => new(platform, Absolute("HOME"), ConfigHome: null, AppData: null, LocalAppData: null, registryRoot),
        };
    }

    /// <summary>The browser whose manifest <paramref name="browser"/> reads: Chrome's for Brave on Windows, which reads no key of its own, else itself.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The browser that owns the manifest.</returns>
    public HostBrowser Slot(HostBrowser browser) =>
        Platform == McpClientPlatform.Windows && browser == HostBrowser.Brave ? HostBrowser.Chrome : browser;

    /// <summary>The browsers that read one manifest with <paramref name="browser"/>.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>Every browser whose slot is <paramref name="browser"/>'s, itself included.</returns>
    public IReadOnlyList<HostBrowser> SharingSlot(HostBrowser browser) => [.. Browsers.Where(other => Slot(other) == Slot(browser))];

    /// <summary>The manifest file keypaste writes for <paramref name="browser"/>: in the browser's own directory, or on Windows under <c>%LOCALAPPDATA%\keypaste</c>.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>Its path, or null when the folder it lives in is unknown.</returns>
    public string? ManifestFile(HostBrowser browser)
    {
        var slot = Slot(browser);

        if (Platform == McpClientPlatform.Windows)
        {
            return Under(LocalAppData, "keypaste", "NativeMessagingHosts", $"{HostName}_{slot.ToString().ToLowerInvariant()}.json");
        }

        var directory = Platform == McpClientPlatform.MacOS
            ? Under(Home, "Library", "Application Support", MacFolder(slot), "NativeMessagingHosts")
            : slot == HostBrowser.Firefox
                ? Under(Home, ".mozilla", "native-messaging-hosts")
                : Under(ConfigHome, LinuxFolder(slot), "NativeMessagingHosts");

        return Under(directory, HostName + ".json");
    }

    /// <summary>The key under <c>HKCU</c> whose default value names the manifest <paramref name="browser"/> reads, on Windows.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The key, such as <c>Software\Google\Chrome\NativeMessagingHosts\com.keypaste.browser</c>.</returns>
    public string RegistryKey(HostBrowser browser) => Slot(browser) switch
    {
        HostBrowser.Chromium => $@"{RegistryRoot}\Chromium\NativeMessagingHosts\{HostName}",
        HostBrowser.Edge => $@"{RegistryRoot}\Microsoft\Edge\NativeMessagingHosts\{HostName}",
        HostBrowser.Firefox => $@"{RegistryRoot}\Mozilla\NativeMessagingHosts\{HostName}",
        _ => $@"{RegistryRoot}\Google\Chrome\NativeMessagingHosts\{HostName}",
    };

    /// <summary>The folder the browser keeps its profiles in: a Chromium-family user data directory, or Firefox's profiles folders.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The folders that may hold profiles; none when the platform's folder is unknown.</returns>
    public IReadOnlyList<string> ProfileFolders(HostBrowser browser)
    {
        IEnumerable<string?> folders = (Platform, browser) switch
        {
            (McpClientPlatform.Windows, HostBrowser.Firefox) => [Under(AppData, "Mozilla", "Firefox", "Profiles")],
            (McpClientPlatform.Windows, _) => [Under(LocalAppData, WindowsFolder(browser), "User Data")],
            (McpClientPlatform.MacOS, HostBrowser.Firefox) => [Under(Home, "Library", "Application Support", "Firefox", "Profiles")],
            (McpClientPlatform.MacOS, _) => [Under(Home, "Library", "Application Support", MacFolder(browser))],
            (_, HostBrowser.Firefox) => [Under(Home, ".mozilla", "firefox"), Under(ConfigHome, "mozilla", "firefox")],
            _ => [Under(ConfigHome, LinuxFolder(browser))],
        };

        return [.. folders.OfType<string>()];
    }

    private static string MacFolder(HostBrowser browser) => browser switch
    {
        HostBrowser.Chromium => "Chromium",
        HostBrowser.Edge => "Microsoft Edge",
        HostBrowser.Brave => Path.Combine("BraveSoftware", "Brave-Browser"),
        HostBrowser.Firefox => "Mozilla",
        _ => Path.Combine("Google", "Chrome"),
    };

    private static string LinuxFolder(HostBrowser browser) => browser switch
    {
        HostBrowser.Chromium => "chromium",
        HostBrowser.Edge => "microsoft-edge",
        HostBrowser.Brave => Path.Combine("BraveSoftware", "Brave-Browser"),
        _ => "google-chrome",
    };

    private static string WindowsFolder(HostBrowser browser) => browser switch
    {
        HostBrowser.Chromium => "Chromium",
        HostBrowser.Edge => Path.Combine("Microsoft", "Edge"),
        HostBrowser.Brave => Path.Combine("BraveSoftware", "Brave-Browser"),
        _ => Path.Combine("Google", "Chrome"),
    };

    private static string? Under(string? root, params string[] parts) => root is null ? null : Path.Combine([root, .. parts]);

    private static string? Folder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder) is { Length: > 0 } path && Path.IsPathFullyQualified(path) ? path : null;
}
