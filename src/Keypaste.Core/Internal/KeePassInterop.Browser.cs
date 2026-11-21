extern alias KeePass;

using KeePass::KeePassLib;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>KeePassXC's custom data keeping an entry, or a group's entries, from its browser extension.</summary>
    private const string _browserHideEntry = "BrowserHideEntry";

    /// <summary>KeePassXC's custom data offering an entry, or a group's entries, only to HTTP authentication.</summary>
    private const string _browserOnlyHttpAuthentication = "BrowserOnlyHttpAuth";

    /// <summary>KeePassXC's group custom data offering its entries to one browser connection alone.</summary>
    private const string _browserRestrictKey = "BrowserRestrictKey";

    /// <summary>KeePassXC's entry custom data holding the hosts its access dialog allowed and denied.</summary>
    private const string _keePassXcBrowserSettings = "KeePassXC-Browser Settings";

    /// <summary>The prefix of the custom fields KeePassXC and Keepass2Android read as more of an entry's addresses.</summary>
    private const string _moreAddresses = "KP2A_URL";

    /// <summary>Every live entry outside keypaste's own groups that a page's request for logins may match, with no password, by the traversal <see cref="Collect"/> uses.</summary>
    internal IReadOnlyList<Browser.BrowserItem> ReadBrowserItems()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<Browser.BrowserItem> items = [];
        CollectBrowserItems(_database.RootGroup, string.Empty, items, Bin(), BrowserGroupSettings.Of(_database.RootGroup, BrowserGroupSettings.Unset));
        return items;
    }

    private static void CollectBrowserItems(PwGroup group, string groupPath, List<Browser.BrowserItem> items, PwGroup? bin, BrowserGroupSettings settings)
    {
        if (ReservedGroups.IsReserved(groupPath))
        {
            return;
        }

        foreach (PwEntry entry in group.Entries)
        {
            var narrowing = Browser.BrowserNarrowing.None;

            if (settings.Hide == true || (settings.Hide is null && entry.CustomData.Get(_browserHideEntry) == "true"))
            {
                narrowing |= Browser.BrowserNarrowing.Hidden;
            }

            if (settings.OnlyHttpAuthentication == true || entry.CustomData.Get(_browserOnlyHttpAuthentication) == "true")
            {
                narrowing |= Browser.BrowserNarrowing.OnlyHttpAuthentication;
            }

            if (settings.RestrictKey is { Length: > 0 })
            {
                narrowing |= Browser.BrowserNarrowing.RestrictedToKey;
            }

            var read = Read(entry, groupPath);

            items.Add(new Browser.BrowserItem(
                new EntryName(groupPath, read.Title) { Uuid = read.Uuid },
                read.Username,
                read.Url,
                read.Expires)
            {
                Addresses =
                [
                    .. entry.Strings
                        .Where(field => field.Key.StartsWith(_moreAddresses, StringComparison.Ordinal))
                        .Select(field => new KeyValuePair<string, string>(field.Key, field.Value.ReadString())),
                ],
                Narrowing = narrowing,
                DeniedHosts = DeniedHostsOf(entry.CustomData.Get(_keePassXcBrowserSettings)),
            });
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectBrowserItems(child, ChildPath(groupPath, child.Name), items, bin, BrowserGroupSettings.Of(child, settings));
            }
        }
    }

    /// <summary>The hosts a <c>KeePassXC-Browser Settings</c> value's <c>Deny</c> list names; none when it is absent or not that JSON.</summary>
    private static string[] DeniedHostsOf(string? settings)
    {
        if (string.IsNullOrEmpty(settings))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(settings);

            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("Deny", out var deny)
                && deny.ValueKind == System.Text.Json.JsonValueKind.Array
                ? [.. deny.EnumerateArray().Where(host => host.ValueKind == System.Text.Json.JsonValueKind.String).Select(host => host.GetString()!.ToLowerInvariant())]
                : [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>A group's KeePassXC browser settings as KeePassXC resolves them: its own where it has one, else its parent's.</summary>
    /// <param name="Hide"><c>BrowserHideEntry</c>: true or false where a group up the tree sets it, null where none does.</param>
    /// <param name="OnlyHttpAuthentication"><c>BrowserOnlyHttpAuth</c>, likewise.</param>
    /// <param name="RestrictKey"><c>BrowserRestrictKey</c>, likewise.</param>
    private readonly record struct BrowserGroupSettings(bool? Hide, bool? OnlyHttpAuthentication, string? RestrictKey)
    {
        internal static BrowserGroupSettings Unset => default;

        internal static BrowserGroupSettings Of(PwGroup group, BrowserGroupSettings parent) => new(
            group.CustomData.Get(_browserHideEntry) is { } hide ? hide == "true" : parent.Hide,
            group.CustomData.Get(_browserOnlyHttpAuthentication) is { } only ? only == "true" : parent.OnlyHttpAuthentication,
            group.CustomData.Get(_browserRestrictKey) ?? parent.RestrictKey);
    }

    private static Browser.WebOrigin? OriginOf(string value) => Browser.WebOrigin.TryReadItem(value.Trim(), out var origin) ? origin : null;
}
