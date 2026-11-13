using System.Text.Json;
using System.Text.Json.Nodes;

namespace Keypaste.Core.Browser;

/// <summary>What a manifest for <see cref="HostPlaces.HostName"/> holds, as read from its file.</summary>
/// <param name="IsKeypaste">Whether it is exactly what <see cref="HostManifest.Document"/> writes.</param>
/// <param name="Program">The program it names, or null when the file is not a manifest at all.</param>
public sealed record ReadManifest(bool IsKeypaste, string? Program);

/// <summary>The manifest that tells a browser to start keypaste for its extension, allowing only keypaste's three builds.</summary>
public static class HostManifest
{
    /// <summary>The longest file read as a manifest.</summary>
    internal const int MaximumLength = 64 * 1024;

    private const string _description = "keypaste's browser fill";

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    /// <summary>The manifest's bytes for <paramref name="browser"/>.</summary>
    /// <param name="browser">The browser, which decides whether its builds are allowed by origin or by add-on ID.</param>
    /// <param name="program">The absolute path of the <c>keypaste</c> the browser starts.</param>
    /// <returns>UTF-8 JSON with a final line break.</returns>
    public static byte[] Document(HostBrowser browser, string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);

        var manifest = new JsonObject
        {
            ["name"] = HostPlaces.HostName,
            ["description"] = _description,
            ["path"] = program,
            ["type"] = "stdio",
        };

        if (browser == HostBrowser.Firefox)
        {
            manifest["allowed_extensions"] = new JsonArray(BrowserIdentity.FirefoxAddOns.Caller);
        }
        else
        {
            manifest["allowed_origins"] = new JsonArray(BrowserIdentity.ChromeWebStore.Caller, BrowserIdentity.EdgeAddOns.Caller);
        }

        return Encoding.UTF8.GetBytes(manifest.ToJsonString(_indented) + "\n");
    }

    /// <summary>Reads a manifest file's bytes.</summary>
    /// <param name="browser">The browser it was read for.</param>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>Whether it is keypaste's and the program it names.</returns>
    public static ReadManifest Read(HostBrowser browser, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumLength)
        {
            return new ReadManifest(IsKeypaste: false, Program: null);
        }

        string? program;

        try
        {
            program = JsonNode.Parse(bytes) is JsonObject manifest && manifest["path"] is JsonValue path && path.TryGetValue<string>(out var named) ? named : null;
        }
        catch (JsonException)
        {
            return new ReadManifest(IsKeypaste: false, Program: null);
        }

        // Writing what was read again must give the same bytes, so a manifest anything else changed is not keypaste's.
        var isKeypaste = program is { Length: > 0 } && bytes.SequenceEqual(Document(browser, program));
        return new ReadManifest(isKeypaste, program);
    }
}
