using Keypaste.Core.Approval;
using Keypaste.Core.Browser;

namespace Keypaste.Core;

/// <summary>What an agent searches for: any of text, a host, a kind, an item it is related to and a project, all of which must match.</summary>
/// <param name="Text">Text found, ignoring case, in a title, group, host, username, field name or project.</param>
/// <param name="Host">A host or URL, matched by its registrable domain, its own host ranked first.</param>
/// <param name="Kind">The kind of item.</param>
/// <param name="RelatedTo">An entry's handle as <see cref="EntryHandle.For"/> makes it, matching the items linked to that one.</param>
/// <param name="Project">A project, matched ordinally, matching its members in any environment.</param>
public sealed record AccessQuery(string? Text = null, string? Host = null, ItemKind? Kind = null, string? RelatedTo = null, string? Project = null)
{
    /// <summary>The longest text, host, handle or project a search carries to the vault's owner.</summary>
    public const int MaximumPartLength = 256;

    /// <summary>Whether it asks for nothing, which <see cref="AccessIndex.Find"/> refuses.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text) && string.IsNullOrWhiteSpace(Host) && Kind is null && string.IsNullOrWhiteSpace(RelatedTo)
        && string.IsNullOrWhiteSpace(Project);
}

/// <summary>Another item one shares a site, a project or an account with, or that one's fields reference or are referenced by.</summary>
/// <param name="Name">The other item.</param>
/// <param name="Site">The registrable domain both origins belong to, or null when they share none.</param>
/// <param name="Projects">The projects both are members of, in any environment, ordinally.</param>
public sealed record AccessLink(EntryName Name, string? Site, IReadOnlyList<string> Projects)
{
    /// <summary>Whether both show the same username, ordinally; only an item a search shows whole carries one.</summary>
    public bool SameAccount { get; init; }

    /// <summary>Whether one of this item's fields holds a KeePass reference to the other by its UUID.</summary>
    public bool References { get; init; }

    /// <summary>Whether one of the other item's fields holds a KeePass reference to this one by its UUID.</summary>
    public bool ReferencedBy { get; init; }

    /// <summary>Whether it links by nothing, which no link carries.</summary>
    public bool IsEmpty => Site is null && Projects.Count == 0 && !SameAccount && !References && !ReferencedBy;

    internal int Strength => (Site is null ? 0 : 1) + Projects.Count + (SameAccount ? 1 : 0) + (References || ReferencedBy ? 1 : 0);
}

/// <summary>An item a search found, with the items it is linked to.</summary>
/// <param name="Entry">The item.</param>
/// <param name="Links">The items it is linked to, the most shared first, at most <see cref="AccessIndex.MaximumLinks"/>.</param>
public sealed record FoundAccess(IndexedEntry Entry, IReadOnlyList<AccessLink> Links);

/// <summary>A project the items a search found belong to, as the searched items show it.</summary>
/// <param name="Name">The project.</param>
/// <param name="Environments">The environments its searched members are in, ordinally, at most <see cref="AccessIndex.MaximumEnvironments"/>.</param>
/// <param name="Items">How many searched items are its members.</param>
public sealed record AccessProject(string Name, IReadOnlyList<string> Environments, int Items);

/// <summary>Searches the entries an exposure admits, links each one found to the others it shares a site, a project or an account with or references, and names their projects.</summary>
/// <remarks>Links are found only among the entries searched, so an entry the exposure leaves out is never named by one.</remarks>
public static class AccessIndex
{
    /// <summary>The most items one search returns.</summary>
    public const int MaximumResults = 25;

    /// <summary>The most links one item carries.</summary>
    public const int MaximumLinks = 8;

    /// <summary>The most projects one search names.</summary>
    public const int MaximumProjects = 8;

    /// <summary>The most environments one project names.</summary>
    public const int MaximumEnvironments = 8;

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
    /// A host that does not parse, or a handle no entry has, matches nothing; a project matches its members in any environment.
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
            throw new ArgumentException("a search names text, a host, a kind, a related item or a project", nameof(query));
        }

        var text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim();
        var handle = string.IsNullOrWhiteSpace(query.RelatedTo) ? null : query.RelatedTo.Trim();
        var project = string.IsNullOrWhiteSpace(query.Project) ? null : query.Project.Trim();
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
                    && (project is null || match.Entry.Projects.Contains(project, StringComparer.Ordinal))
                    && (related is null || match.Link is not null))
                .OrderByDescending(match => match.Host)
                .ThenByDescending(match => match.Text)
                .ThenByDescending(match => match.Link?.Strength ?? 0)
                .ThenBy(match => match.Entry.Listing.Name.GroupPath, StringComparer.Ordinal)
                .ThenBy(match => match.Entry.Listing.Name.Title, StringComparer.Ordinal)
                .ThenBy(match => match.Entry.Listing.Name.Uuid, StringComparer.Ordinal)
                .Take(limit)
                .Select(match => new FoundAccess(match.Entry, Linked(match.Entry, entries))),
        ];
    }

    /// <summary>The projects the items found are members of, each with the environments and the count of its members among <paramref name="entries"/>.</summary>
    /// <param name="found">The items a search returns.</param>
    /// <param name="entries">The entries searched, which the exposure has already admitted, so a project only a hidden item belongs to is never named.</param>
    /// <returns>At most <see cref="MaximumProjects"/>, the most members first, then by name ordinally.</returns>
    public static IReadOnlyList<AccessProject> Projects(IEnumerable<FoundAccess> found, IReadOnlyList<IndexedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(entries);

        AccessProject Of(string project)
        {
            List<IndexedEntry> members = [.. entries.Where(entry => entry.Projects.Contains(project, StringComparer.Ordinal))];
            List<string> environments =
            [
                .. members
                    .SelectMany(member => member.Listing.Tags)
                    .Select(ProjectTag.Read)
                    .Where(tag => tag.Kind == ProjectTagKind.Member && tag.Project == project)
                    .Select(tag => tag.Environment!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Take(MaximumEnvironments),
            ];

            return new AccessProject(project, environments, members.Count);
        }

        return
        [
            .. found
                .SelectMany(item => item.Entry.Projects)
                .Distinct(StringComparer.Ordinal)
                .Select(Of)
                .OrderByDescending(project => project.Items)
                .ThenBy(project => project.Name, StringComparer.Ordinal)
                .Take(MaximumProjects),
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

    /// <summary>The items one entry is linked to among <paramref name="entries"/>, exactly as a search that found it names them.</summary>
    /// <param name="entry">The entry, one of <paramref name="entries"/>.</param>
    /// <param name="entries">The entries searched, which the exposure has already admitted.</param>
    /// <returns>At most <see cref="MaximumLinks"/>, the most shared first.</returns>
    public static IReadOnlyList<AccessLink> LinksOf(IndexedEntry entry, IReadOnlyList<IndexedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entries);

        return Linked(entry, entries);
    }

    private static List<AccessLink> Linked(IndexedEntry entry, IReadOnlyList<IndexedEntry> entries) =>
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
        var link = new AccessLink(other.Listing.Name, site, projects)
        {
            SameAccount = entry.Username is { } username && username == other.Username,
            References = References(entry, other),
            ReferencedBy = References(other, entry),
        };

        return link.IsEmpty ? null : link;
    }

    private static bool References(IndexedEntry entry, IndexedEntry other) =>
        other.Listing.Name.Uuid is { } uuid && entry.References.Contains(uuid, StringComparer.OrdinalIgnoreCase);
}
