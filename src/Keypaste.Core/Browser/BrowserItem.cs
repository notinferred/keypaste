namespace Keypaste.Core.Browser;

/// <summary>An item a page's request for logins may match, as the vault holds it, with no password.</summary>
/// <param name="Name">Its group and title, with its UUID.</param>
/// <param name="Username">Its username as stored, placeholders unfilled.</param>
/// <param name="Url">Its URL.</param>
/// <param name="Expires">When it expires, or null when it does not.</param>
internal sealed record BrowserItem(EntryName Name, string Username, string Url, DateTimeOffset? Expires)
{
    /// <summary>Its custom fields whose name starts <c>KP2A_URL</c>, by name and value.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Addresses { get; init; } = [];

    /// <summary>What KeePassXC's browser settings on the item or its groups keep it from.</summary>
    public BrowserNarrowing Narrowing { get; init; }

    /// <summary>The hosts KeePassXC's own access dialog was told to deny it to, from its <c>KeePassXC-Browser Settings</c>.</summary>
    public IReadOnlyList<string> DeniedHosts { get; init; } = [];
}

/// <summary>KeePassXC's browser settings that keep an item out of a page's logins, as KeePassXC 2.8.0's <c>BrowserService</c> reads them.</summary>
[Flags]
internal enum BrowserNarrowing
{
    /// <summary>Nothing keeps it out.</summary>
    None = 0,

    /// <summary><c>BrowserHideEntry</c> on the item, or on a group above it.</summary>
    Hidden = 1,

    /// <summary><c>BrowserOnlyHttpAuth</c>: only for HTTP authentication, which keypaste refuses.</summary>
    OnlyHttpAuthentication = 2,

    /// <summary><c>BrowserRestrictKey</c> on a group above it: only for one KeePassXC connection, which a keypaste pairing never is.</summary>
    RestrictedToKey = 4,
}
