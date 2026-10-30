using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Browser;

namespace Keypaste.Browser;

/// <summary>
/// The arguments a browser starts its native messaging host with, and nothing else: a Chrome-family browser's caller origin,
/// followed on Windows by the window it was called from, or Firefox's manifest path and the calling add-on's ID (D-0624).
/// </summary>
/// <remarks>
/// Read from the arguments alone, so <c>keypaste</c> decides it is a relay before any vault code runs, as it decides
/// <c>keypaste mcp</c> is the bridge (D-0418).
/// </remarks>
internal static class BrowserCaller
{
    internal const string ExtensionOrigin = "chrome-extension://";

    internal const string ParentWindow = "--parent-window=";

    /// <summary>The caller a browser's arguments name, or null when they are not a browser's.</summary>
    /// <param name="args">The arguments, without the program.</param>
    /// <param name="windows">Whether this is Windows, where Chrome adds the window it was called from.</param>
    /// <returns>The extension origin or add-on ID.</returns>
    internal static string? Of(string[] args, bool windows) => args switch
    {
        [var origin] when IsOrigin(origin) => origin,
        [var origin, var parent] when windows && IsOrigin(origin) && IsParentWindow(parent) => origin,
        [var manifest, var addOn] when IsManifest(manifest) && addOn.Length > 0 && !addOn.StartsWith('-') => addOn,
        _ => null,
    };

    /// <summary>The KeePassXC-Browser build a browser's arguments name, or why keypaste does not answer it.</summary>
    /// <param name="args">The arguments, without the program.</param>
    /// <param name="windows">Whether this is Windows.</param>
    /// <param name="identity">The build, when it is one keypaste answers.</param>
    /// <param name="problem">Why not, otherwise.</param>
    /// <returns>Whether keypaste answers the caller.</returns>
    internal static bool TryRead(string[] args, bool windows, [NotNullWhen(true)] out BrowserIdentity? identity, out string problem)
    {
        var caller = Of(args, windows);
        identity = BrowserIdentity.Of(caller);
        problem = identity is not null ? string.Empty
            : caller is null ? "these are not the arguments a browser starts its native messaging host with"
            : $"{BrowserIdentity.Only}, and the browser named {caller}";

        return identity is not null;
    }

    private static bool IsOrigin(string argument) => argument.StartsWith(ExtensionOrigin, StringComparison.Ordinal);

    private static bool IsParentWindow(string argument) =>
        argument.StartsWith(ParentWindow, StringComparison.Ordinal)
        && argument.Length > ParentWindow.Length
        && !argument.AsSpan(ParentWindow.Length).ContainsAnyExceptInRange('0', '9');

    private static bool IsManifest(string argument) =>
        Path.IsPathFullyQualified(argument) && argument.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
