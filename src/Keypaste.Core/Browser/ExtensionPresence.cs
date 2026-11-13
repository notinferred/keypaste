namespace Keypaste.Core.Browser;

/// <summary>Whether keypaste's extension is installed in a browser, as its profile folders show.</summary>
public enum ExtensionState
{
    /// <summary>No profile folder of the browser exists.</summary>
    BrowserNotFound,

    /// <summary>The browser has profiles and none holds the extension.</summary>
    NotFound,

    /// <summary>A profile holds the extension.</summary>
    Installed,
}

/// <summary>Finds keypaste's extension in a browser's profiles by reading their folders, never changing them.</summary>
/// <remarks>
/// A Chromium-family browser keeps a store-installed extension in <c>&lt;profile&gt;/Extensions/&lt;id&gt;</c>, and Firefox in
/// <c>&lt;profile&gt;/extensions/&lt;id&gt;.xpi</c>; an extension loaded unpacked appears in neither.
/// </remarks>
public static class ExtensionPresence
{
    /// <summary>Whether <paramref name="browser"/>'s profiles hold keypaste's extension.</summary>
    /// <param name="places">The user's folders.</param>
    /// <param name="browser">The browser.</param>
    /// <returns>What the folders show.</returns>
    public static ExtensionState Of(HostPlaces places, HostBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(places);

        var profiles = places.ProfileFolders(browser).Where(Directory.Exists).SelectMany(Profiles).ToList();

        if (profiles.Count == 0)
        {
            return ExtensionState.BrowserNotFound;
        }

        return profiles.Any(profile => Holds(browser, profile)) ? ExtensionState.Installed : ExtensionState.NotFound;
    }

    private static bool Holds(HostBrowser browser, string profile) =>
        browser == HostBrowser.Firefox
            ? File.Exists(Path.Combine(profile, "extensions", BrowserIdentity.FirefoxAddOns.Id + ".xpi"))
            : Directory.Exists(Path.Combine(profile, "Extensions", BrowserIdentity.ChromeWebStore.Id))
                || Directory.Exists(Path.Combine(profile, "Extensions", BrowserIdentity.EdgeAddOns.Id));

    // A Chromium user data directory holds "Default" and "Profile <n>" beside its own folders; Firefox's holds only profiles.
    private static IEnumerable<string> Profiles(string folder)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(folder).Where(IsProfile)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsProfile(string directory)
    {
        var name = Path.GetFileName(directory);

        return name == "Default"
            || name.StartsWith("Profile ", StringComparison.Ordinal)
            || File.Exists(Path.Combine(directory, "prefs.js"))
            || Directory.Exists(Path.Combine(directory, "extensions"));
    }
}
