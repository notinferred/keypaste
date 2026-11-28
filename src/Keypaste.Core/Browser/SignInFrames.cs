using System.Collections.Frozen;

namespace Keypaste.Core.Browser;

/// <summary>
/// The sign-in frames of another site keypaste offers logins in without asking: each is where its own site's sign-in is served
/// inside the pages named beside it, and it is offered only its own site's logins (THREATS.md T-62).
/// </summary>
/// <remarks>Each entry is a supply of trust nobody is asked for, so one is added only with the reason it is there.</remarks>
internal static class SignInFrames
{
    private static readonly FrozenDictionary<string, FrozenSet<string>> _known = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
    {
        // iCloud's pages show the Apple Account sign-in in a frame from Apple's identity service, idmsa.apple.com, which
        // KeePassXC-Browser's own list of sign-in sites names for the same reason. Apple's own pages need no entry: they are
        // apple.com, the frame's own site, which is filled as the page.
        ["idmsa.apple.com"] = new[] { "www.icloud.com", "icloud.com" }.ToFrozenSet(StringComparer.Ordinal),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Whether a frame from <paramref name="frame"/> is a known sign-in frame in pages of <paramref name="top"/>.</summary>
    /// <param name="top">The host of the tab's page.</param>
    /// <param name="frame">The frame's host.</param>
    /// <returns>Whether it is on the list.</returns>
    internal static bool IsKnown(string top, string frame) =>
        _known.TryGetValue(frame, out var pages) && pages.Contains(top);
}
