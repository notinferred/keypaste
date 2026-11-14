using Keypaste.Core.Approval;
using Keypaste.Core.Browser;

namespace Keypaste.Core;

/// <summary>What an agent searches for: any of text, a host, a kind and an item it is related to, all of which must match.</summary>
/// <param name="Text">Text found, ignoring case, in a title, group, host, username, field name or project.</param>
/// <param name="Host">A host or URL, matched by its registrable domain, its own host ranked first.</param>
/// <param name="Kind">The kind of item.</param>
/// <param name="RelatedTo">An entry's handle as <see cref="EntryHandle.For"/> makes it, matching the items linked to that one.</param>
public sealed record AccessQuery(string? Text = null, string? Host = null, ItemKind? Kind = null, string? RelatedTo = null)
{
    /// <summary>Whether it asks for nothing, which <see cref="AccessIndex.Find"/> refuses.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text) && string.IsNullOrWhiteSpace(Host) && Kind is null && string.IsNullOrWhiteSpace(RelatedTo);
}

/// <summary>Another item one shares a site or a project with.</summary>
/// <param name="Name">The other item.</param>
/// <param name="Site">The registrable domain both origins belong to, or null when they share none.</param>
/// <param name="Projects">The projects both are members of, in any environment, ordinally.</param>
public sealed record AccessLink(EntryName Name, string? Site, IReadOnlyList<string> Projects)
{
    internal int Strength => (Site is null ? 0 : 1) + Projects.Count;
}

/// <summary>An item a search found, with the items it is linked to.</summary>
/// <param name="Entry">The item.</param>
/// <param name="Links">The items it shares a site or a project with, the most shared first, at most <see cref="AccessIndex.MaximumLinks"/>.</param>
public sealed record FoundAccess(IndexedEntry Entry, IReadOnlyList<AccessLink> Links);

/// <summary>Searches the entries an exposure admits and links each one found to the others it shares a site or a project with.</summary>
/// <remarks>Links are found only among the entries searched, so an entry the exposure leaves out is never named by one.</remarks>
public static class AccessIndex
{
    /// <summary>The most items one search returns.</summary>
    public const int MaximumResults = 25;

    /// <summary>The most links one item carries.</summary>
    public const int MaximumLinks = 8;

    /// <summary>The entries an exposure reaches, each as its reach shows it, at most <see cref="VaultEntryNameLister.MaximumNames"/>.</summary>
    /// <param name="index">Every entry, as <see cref="Vault.ReadSavedIndex"/> reads them.</param>
    /// <param name="exposure">What may be named.</param>
    /// <returns>The admitted entries, in the vault's order.</returns>
    public static IReadOnlyList<IndexedEntry> Admitted(IEnumerable<IndexedEntry> index, EntryExposure exposure)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(exposure);

        return
        [
            .. index
                .Select(entry => (Entry: entry, Reach: exposure.Reach(entry.Listing.Name, entry.Listing.Tags)))
                .Where(admitted => admitted.Reach != ExposureReach.None)
                .Select(admitted => admitted.Entry.Within(admitted.Reach))
                .Take(VaultEntryNameLister.MaximumNames),
        ];
    }

    /// <summary>The entries matching a query, best first, each with its links among <paramref name="entries"/>.</summary>
    /// <param name="entries">The entries to search, which the exposure has already admitted.</param>
    /// <param name="query">What to match; every part given must match.</param>
    /// <param name="limit">The most items returned.</param>
    /// <returns>
    /// The matches ranked by host (its own host above its site), then text (title exact, prefix, anywhere, then host or username,
    /// then group, project or field name), then how much each shares with the related item, then group, title and UUID ordinally.
    /// A host that does not parse, or a handle no entry has, matches nothing.
    /// </returns>
    /// <exception cref="ArgumentException">The query is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is below 1.</exception>
    public static IReadOnlyList<FoundAccess> Find(IReadOnlyList<IndexedEntry> entries, AccessQuery query, int limit = MaximumResults)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (query.IsEmpty)
        {
            throw new ArgumentException("a search names text, a host, a kind or a related item", nameof(query));
        }

        var text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim();
        var handle = string.IsNullOrWhiteSpace(query.RelatedTo) ? null : query.RelatedTo.Trim();
        WebOrigin? host = null;

        if (!string.IsNullOrWhiteSpace(query.Host) && !WebOrigin.TryReadItem(query.Host.Trim(), out host))
        {
            return [];
        }

        var related = handle is null ? null : entries.FirstOrDefault(entry => EntryHandle.For(entry.Listing.Name) == handle);

        if (handle is not null && related is null)
        {
            return [];
        }

        return
        [
            .. entries
                .Select(entry => (
                    Entry: entry,
                    Host: host is null ? 0 : HostRank(entry, host),
                    Text: text is null ? 0 : TextRank(entry, text),
                    Link: related is null ? null : Link(related, entry)))
                .Where(match => (host is null || match.Host > 0)
                    && (text is null || match.Text > 0)
                    && (query.Kind is null || match.Entry.Kind == query.Kind)
                    && (related is null || match.Link is not null))
                .OrderByDescending(match => match.Host)
                .ThenByDescending(match => match.Text)
                .ThenByDescending(match => match.Link?.Strength ?? 0)
                .ThenBy(match => match.Entry.Listing.Name.GroupPath, StringComparer.Ordinal)
                .ThenBy(match => match.Entry.Listing.Name.Title, StringComparer.Ordinal)
                .ThenBy(match => match.Entry.Listing.Name.Uuid, StringComparer.Ordinal)
                .Take(limit)
                .Select(match => new FoundAccess(match.Entry, LinksOf(match.Entry, entries))),
        ];
    }

    private static int HostRank(IndexedEntry entry, WebOrigin host) =>
        entry.Origin is not { } origin ? 0
        : origin.Host == host.Host ? 2
        : host.Site is not null && origin.Site == host.Site ? 1
        : 0;

    private static int TextRank(IndexedEntry entry, string text)
    {
        var name = entry.Listing.Name;
        bool Holds(string? value) => value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase);

        return name.Title.Equals(text, StringComparison.OrdinalIgnoreCase) ? 5
            : name.Title.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 4
            : Holds(name.Title) ? 3
            : Holds(entry.Origin?.Host) || Holds(entry.Username) ? 2
            : Holds(name.GroupPath) || entry.Projects.Any(Holds) || entry.Listing.Fields.Any(Holds) ? 1
            : 0;
    }

    private static List<AccessLink> LinksOf(IndexedEntry entry, IReadOnlyList<IndexedEntry> entries) =>
        [
            .. entries
                .Select(other => Link(entry, other))
                .OfType<AccessLink>()
                .OrderByDescending(link => link.Strength)
                .ThenBy(link => link.Name.GroupPath, StringComparer.Ordinal)
                .ThenBy(link => link.Name.Title, StringComparer.Ordinal)
                .ThenBy(link => link.Name.Uuid, StringComparer.Ordinal)
                .Take(MaximumLinks),
        ];

    private static AccessLink? Link(IndexedEntry entry, IndexedEntry other)
    {
        if (ReferenceEquals(entry, other))
        {
            return null;
        }

        var site = entry.Origin?.Site is { } own && own == other.Origin?.Site ? own : null;
        List<string> projects = [.. entry.Projects.Intersect(other.Projects, StringComparer.Ordinal)];

        return site is null && projects.Count == 0 ? null : new AccessLink(other.Listing.Name, site, projects);
    }
}
