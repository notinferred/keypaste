using System.Globalization;

namespace Keypaste.Core.Browser;

/// <summary>A matched item a page gets no login from, and why, in words an audit line completes.</summary>
/// <param name="Item">The item.</param>
/// <param name="Reason">Why, such as <c>KeePassXC hides it from browsers</c>.</param>
internal sealed record RefusedMatch(BrowserItem Item, string Reason);

/// <summary>Which of the vault's items a page's request for logins may fill, and which matched and are kept out.</summary>
/// <remarks>
/// An item matches by <see cref="LoginLookup.MatchesItem"/>. One that matches is kept out when KeePassXC's browser settings on it
/// or its groups hide it, offer it only to HTTP authentication or only to one KeePassXC connection, or name the page's host, or
/// its form's, among the hosts KeePassXC was told to deny it to; and when it has expired, as a run refuses an expired variable.
/// KeePassXC's allow lists are not read: what the person allowed in keypaste decides.
/// </remarks>
internal static class LoginCandidates
{
    /// <summary>The items a page may fill, and the matched items kept out.</summary>
    /// <param name="lookup">The page's request.</param>
    /// <param name="items">The vault's items, as <see cref="Vault.ReadSavedBrowserItems"/> read them.</param>
    /// <param name="now">The time an expiry is compared with.</param>
    /// <returns>Both, each in the vault's order.</returns>
    internal static (IReadOnlyList<BrowserItem> Candidates, IReadOnlyList<RefusedMatch> Refused) Of(LoginLookup lookup, IEnumerable<BrowserItem> items, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(items);

        List<BrowserItem> candidates = [];
        List<RefusedMatch> refused = [];

        foreach (var item in items.Where(item => lookup.MatchesItem(item.Url, item.Addresses)))
        {
            if (KeptOut(lookup, item, now) is { } reason)
            {
                refused.Add(new RefusedMatch(item, reason));
            }
            else
            {
                candidates.Add(item);
            }
        }

        return (candidates, refused);
    }

    private static string? KeptOut(LoginLookup lookup, BrowserItem item, DateTimeOffset now)
    {
        if (item.Narrowing.HasFlag(BrowserNarrowing.Hidden))
        {
            return "KeePassXC hides it from browsers";
        }

        if (item.Narrowing.HasFlag(BrowserNarrowing.OnlyHttpAuthentication))
        {
            return "KeePassXC offers it only to HTTP authentication";
        }

        if (item.Narrowing.HasFlag(BrowserNarrowing.RestrictedToKey))
        {
            return "KeePassXC offers it only to one of its own browser connections";
        }

        if (item.DeniedHosts.FirstOrDefault(host => host == lookup.Page.Host || host == lookup.OtherSiteForm?.Host) is { } denied)
        {
            return $"KeePassXC was told to deny it to {denied}";
        }

        return item.Expires is { } expires && expires <= now
            ? $"expired {expires.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)}"
            : null;
    }
}
