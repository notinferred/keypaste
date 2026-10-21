using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Keypaste.Core.Clients;

/// <summary>The link a tool opens to add an MCP server to its own configuration.</summary>
public enum McpInstallLinkFormat
{
    /// <summary>The tool takes no such link.</summary>
    None,

    /// <summary>
    /// Cursor's deeplink: the server's name, and its <c>mcp.json</c> entry as base64 (cursor.com/docs/context/mcp/install-links).
    /// </summary>
    Cursor,

    /// <summary>
    /// VS Code's URL handler: the server's <c>mcp.json</c> entry with its name, URL-encoded
    /// (code.visualstudio.com/api/extension-guides/ai/mcp).
    /// </summary>
    VsCode,
}

/// <summary>
/// Composes the link that has Cursor or VS Code add keypaste itself, from the registration <c>keypaste setup</c> and
/// Connect write.
/// </summary>
/// <remarks>
/// The link carries the tool's file entry for that registration and nothing else, so what the tool saves is what
/// keypaste would have written. A registration the tool would rewrite gets no link, as it gets no plan
/// (<see cref="McpClientSetup.TryConnect"/>).
/// </remarks>
public static class McpInstallLink
{
    /// <summary>What every Cursor install link starts with.</summary>
    public const string CursorPrefix = "cursor://anysphere.cursor-deeplink/mcp/install?";

    /// <summary>What every VS Code install link starts with.</summary>
    public const string VsCodePrefix = "vscode:mcp/install?";

    /// <summary>The link that has <paramref name="client"/> start <paramref name="registration"/>, or why there is none.</summary>
    /// <param name="client">The tool.</param>
    /// <param name="platform">Where the tool runs, which decides what it expands.</param>
    /// <param name="registration">What the tool is told to start.</param>
    /// <param name="link">The link, when there is one.</param>
    /// <param name="error">Why not: the tool takes no link, or it would rewrite part of the registration.</param>
    /// <returns>Whether there is a link.</returns>
    public static bool TryCreate(
        McpClient client,
        McpClientPlatform platform,
        McpServerRegistration registration,
        [NotNullWhen(true)] out string? link,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(registration);

        link = null;

        if (client.InstallLink == McpInstallLinkFormat.None)
        {
            error = $"{client.DisplayName} takes no install link";
            return false;
        }

        if (McpClientSetup.Rewritten(client, platform, registration) is { } rewritten)
        {
            error = rewritten;
            return false;
        }

        var entry = registration.FileEntry(client.TypedEntry);

        link = client.InstallLink == McpInstallLinkFormat.Cursor
            ? CursorPrefix
                + "name=" + Uri.EscapeDataString(McpClientCatalog.ServerName)
                + "&config=" + Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.ToJsonString())))
            : VsCodePrefix + Uri.EscapeDataString(Named(entry).ToJsonString());
        error = string.Empty;
        return true;
    }

    private static JsonObject Named(JsonObject entry)
    {
        var named = new JsonObject { ["name"] = McpClientCatalog.ServerName };

        foreach (var (key, value) in entry)
        {
            named[key] = value?.DeepClone();
        }

        return named;
    }
}
