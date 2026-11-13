using System.Diagnostics;
using System.Text.Json;
using Keypaste.Core.Browser;
using Microsoft.Win32;

namespace Keypaste.FakeBrowser;

/// <summary>Finds <c>com.keypaste.browser</c> where a browser looks for it, and starts its program as that browser starts it.</summary>
internal static class HostLookup
{
    /// <summary>The exit code when no manifest is where the browser looks.</summary>
    internal const int NotFound = 20;

    /// <summary>The exit code when a manifest is found that the browser would refuse.</summary>
    internal const int Refused = 21;

    /// <summary>The host's start, or why the browser would start nothing and the exit code that says so.</summary>
    internal static (ProcessStartInfo? Start, string Refusal, int Code) Find(HostPlaces places, HostBrowser browser)
    {
        string? manifest;

        if (OperatingSystem.IsWindows())
        {
            var key = places.RegistryKey(browser);
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            manifest = opened?.GetValue(null) as string;

            if (manifest is null)
            {
                return (null, $@"HKCU\{key} names no manifest", NotFound);
            }
        }
        else
        {
            manifest = places.ManifestFile(browser);
        }

        if (manifest is null || !File.Exists(manifest))
        {
            return (null, $"no manifest at {manifest ?? "a folder the browser does not know"}", NotFound);
        }

        Console.Error.WriteLine($"found {manifest}");

        JsonElement read;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            read = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return (null, $"{manifest} is not JSON: {ex.Message}", Refused);
        }

        var firefox = browser == HostBrowser.Firefox;
        var caller = firefox ? BrowserIdentity.FirefoxAddOns.Caller
            : browser == HostBrowser.Edge ? BrowserIdentity.EdgeAddOns.Caller
            : BrowserIdentity.ChromeWebStore.Caller;
        var allowed = firefox ? "allowed_extensions" : "allowed_origins";

        if (read.ValueKind != JsonValueKind.Object
            || Text(read, "name") != HostPlaces.HostName
            || Text(read, "type") != "stdio"
            || Text(read, "path") is not { } program
            || !Path.IsPathFullyQualified(program))
        {
            return (null, $"{manifest} does not name {HostPlaces.HostName} as a stdio host at an absolute path", Refused);
        }

        if (!read.TryGetProperty(allowed, out var callers) || callers.ValueKind != JsonValueKind.Array
            || !callers.EnumerateArray().Any(listed => listed.ValueKind == JsonValueKind.String && listed.GetString() == caller))
        {
            return (null, $"{manifest}'s {allowed} does not list {caller}", Refused);
        }

        var start = new ProcessStartInfo(program);

        if (firefox)
        {
            start.ArgumentList.Add(manifest);
            start.ArgumentList.Add(caller);
        }
        else if (OperatingSystem.IsWindows())
        {
            // Chrome starts a host through cmd, the whole command in one pair of quotes that /s strips, and a service worker calls from window 0.
            start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            {
                Arguments = $"/d /s /c \"\"{program}\" {caller} --parent-window=0\"",
            };
        }
        else
        {
            start.ArgumentList.Add(caller);
        }

        return (start, string.Empty, 0);
    }

    private static string? Text(JsonElement manifest, string name) =>
        manifest.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
