using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>How a search ranks what an exposure admits, and links each item found only to other admitted items sharing its site or a project.</summary>
public sealed class AccessIndexTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _github = new("Work", "GitHub");
    private static readonly EntryName _token = new("Work", "GitHub token");
    private static readonly EntryName _gist = new("Work", "Gist");
    private static readonly EntryName _database = new("Ops", "Database");
    private static readonly EntryName _stripe = new("Ops", "Stripe");
    private static readonly EntryName _bank = new("Personal", "Bank");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-access-index-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_host_matches_its_site_with_its_own_host_first()
    {
        var found = Find(Everything(), new AccessQuery(Host: "https://github.com/login"));

        Assert.Equal([_github, _gist, _token], Names(found));
    }

    [Theory]
    [InlineData("https://evilgithub.com")]
    [InlineData("https://github.com.evil.io")]
    [InlineData("mailto:dan@github.com")]
    public void A_host_on_another_site_or_one_that_does_not_parse_matches_nothing(string host) =>
        Assert.Empty(Find(Everything(), new AccessQuery(Host: host)));

    [Fact]
    public void Text_ranks_a_title_above_a_host_above_a_group_or_project_ignoring_case()
    {
        Assert.Equal([_github, _token, _gist], Names(Find(Everything(), new AccessQuery(Text: "GITHUB"))));
        Assert.Equal([_stripe], Names(Find(Everything(), new AccessQuery(Text: "billing"))));
        Assert.Equal([_database, _stripe], Names(Find(Everything(), new AccessQuery(Text: "ops"))));
    }

    [Fact]
    public void Every_part_of_a_query_must_match()
    {
        Assert.Equal([_token], Names(Find(Everything(), new AccessQuery(Text: "github", Kind: ItemKind.ApiKey))));
        Assert.Empty(Find(Everything(), new AccessQuery(Host: "github.com", Kind: ItemKind.Database)));
    }

    [Fact]
    public void Related_to_finds_the_items_sharing_its_site_or_a_project_most_shared_first()
    {
        var found = Find(Everything(), new AccessQuery(RelatedTo: EntryHandle.For(_github)));

        Assert.Equal([_gist, _database, _token], Names(found));
    }

    [Fact]
    public void A_handle_no_searched_item_has_matches_nothing() =>
        Assert.Empty(Find(Everything(), new AccessQuery(RelatedTo: EntryHandle.For(new EntryName("Work", "Nobody")))));

    [Fact]
    public void Each_link_names_what_the_two_items_share_and_links_run_both_ways()
    {
        var entries = Everything();

        var gist = Assert.Single(Find(entries, new AccessQuery(Host: "github.com"))[0].Links, link => link.Name == _gist);
        Assert.Equal("github.com", gist.Site);
        Assert.Equal(["web"], gist.Projects);

        var links = Find(entries, new AccessQuery(Text: "password")).ToDictionary(found => found.Entry.Listing.Name, found => found.Links.Select(link => link.Name).ToList());
        Assert.Equal(entries.Count, links.Count);
        Assert.All(links, pair => Assert.All(pair.Value, linked => Assert.Contains(pair.Key, links[linked])));
    }

    [Fact]
    public void An_item_carries_at_most_eight_links_and_a_search_at_most_its_limit()
    {
        var entries = Index(Enumerable.Range(0, 12).Select(number => (new VaultEntry { Title = $"Service {number:00}", GroupPath = "Work", Password = "pw" }, new[] { "env:web" })).ToArray());

        var found = Find(entries, new AccessQuery(Text: "service"), limit: 5);

        Assert.Equal(5, found.Count);
        Assert.All(found, one => Assert.Equal(AccessIndex.MaximumLinks, one.Links.Count));
        Assert.Equal(["Service 01", "Service 02", "Service 03", "Service 04", "Service 05", "Service 06", "Service 07", "Service 08"], found[0].Links.Select(link => link.Name.Title));
    }

    [Fact]
    public void An_item_the_exposure_leaves_out_is_never_found_or_linked()
    {
        Assert.True(EntryExposure.TryCreate(["Work/**"], out var work, out var error), error);

        var admitted = AccessIndex.Admitted(Everything(), work);
        var found = Find(admitted, new AccessQuery(RelatedTo: EntryHandle.For(_github)));

        Assert.Equal([_gist, _token], Names(found));
        Assert.DoesNotContain(found.SelectMany(one => one.Links), link => link.Name.GroupPath != "Work");
        Assert.Empty(Find(admitted, new AccessQuery(Text: "database")));
    }

    [Fact]
    public void An_item_reached_only_through_a_project_shows_no_origin_or_username_and_links_by_project_alone()
    {
        var admitted = AccessIndex.Admitted(Everything(), EntryExposure.Default);

        Assert.All(admitted, entry => Assert.Null(entry.Origin));
        Assert.All(admitted, entry => Assert.Null(entry.Username));
        Assert.Empty(Find(admitted, new AccessQuery(Host: "github.com")));

        var github = Assert.Single(Find(admitted, new AccessQuery(Text: "GitHub", Kind: ItemKind.Login)));
        Assert.Equal([_database, _gist], github.Links.Select(link => link.Name));
        Assert.All(github.Links, link => Assert.Null(link.Site));
    }

    [Fact]
    public void A_project_finds_its_members_in_every_environment()
    {
        Assert.Equal([_database, _gist, _github], Names(Find(Everything(), new AccessQuery(Project: "web"))));
        Assert.Equal([_gist], Names(Find(Everything(), new AccessQuery(Text: "gist", Project: "web"))));
        Assert.Empty(Find(Everything(), new AccessQuery(Project: "Web")));
    }

    [Fact]
    public void The_projects_of_what_was_found_count_only_admitted_members()
    {
        var entries = Everything();
        Assert.True(EntryExposure.TryCreate(["Work/**"], out var work, out var error), error);
        var admitted = AccessIndex.Admitted(entries, work);

        var everywhere = AccessIndex.Projects(Find(entries, new AccessQuery(Text: "GitHub")), entries);
        var withinWork = AccessIndex.Projects(Find(admitted, new AccessQuery(Text: "GitHub")), admitted);

        Assert.Equal([("web", "dev, prod", 3)], everywhere.Select(Shown));
        Assert.Equal([("web", "dev, prod", 2)], withinWork.Select(Shown));
        Assert.Empty(AccessIndex.Projects(Find(admitted, new AccessQuery(Text: "token")), admitted));
    }

    [Fact]
    public void Two_items_shown_whole_with_one_username_link_as_one_account()
    {
        IndexedEntry[] entries =
        [
            Item("Work", "GitHub", username: "dan"),
            Item("Work", "Gist", username: "dan"),
            Item("Work", "GitLab", username: "Dan"),
            Item("Work", "Bitbucket"),
        ];

        var github = Assert.Single(Find(entries, new AccessQuery(Text: "GitHub")));

        var link = Assert.Single(github.Links);
        Assert.Equal(new EntryName("Work", "Gist"), link.Name);
        Assert.True(link.SameAccount);

        var throughProject = AccessIndex.Admitted([.. entries.Select(entry => entry with { Listing = entry.Listing with { Tags = ["env:web"] } })], EntryExposure.Default);
        var links = Assert.Single(Find(throughProject, new AccessQuery(Text: "GitHub"))).Links;
        Assert.Equal(3, links.Count);
        Assert.All(links, each => Assert.False(each.SameAccount));
    }

    [Fact]
    public void A_reference_links_both_ends_only_to_an_admitted_item()
    {
        const string root = "0123456789ABCDEF0123456789ABCDEF";
        const string secret = "FEDCBA9876543210FEDCBA9876543210";
        IndexedEntry[] entries =
        [
            Item("Ops", "Deploy", references: [root.ToLowerInvariant(), secret, new string('A', 32)]),
            Item("Ops", "Root", uuid: root),
            Item("Personal", "Secret", uuid: secret),
        ];
        Assert.True(EntryExposure.TryCreate(["Ops/**"], out var ops, out var error), error);
        var admitted = AccessIndex.Admitted(entries, ops);

        var deploy = Assert.Single(Find(admitted, new AccessQuery(Text: "Deploy")));
        var rootFound = Assert.Single(Find(admitted, new AccessQuery(Text: "Root")));

        var references = Assert.Single(deploy.Links);
        Assert.Equal((new EntryName("Ops", "Root"), true, false), (references.Name, references.References, references.ReferencedBy));
        var referencedBy = Assert.Single(rootFound.Links);
        Assert.Equal((new EntryName("Ops", "Deploy"), false, true), (referencedBy.Name, referencedBy.References, referencedBy.ReferencedBy));
    }

    [Fact]
    public void An_empty_query_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AccessIndex.Find(Everything(), new AccessQuery(Text: "  ")));
    }

    private static IReadOnlyList<FoundAccess> Find(IReadOnlyList<IndexedEntry> entries, AccessQuery query, int limit = AccessIndex.MaximumResults) =>
        AccessIndex.Find(entries, query, limit);

    private static EntryName[] Names(IEnumerable<FoundAccess> found) => [.. found.Select(one => one.Entry.Listing.Name)];

    private static (string, string, int) Shown(AccessProject project) => (project.Name, string.Join(", ", project.Environments), project.Items);

    private static IndexedEntry Item(string group, string title, string? uuid = null, string? username = null, string[]? references = null) =>
        new(new ListedEntry(new EntryName(group, title) { Uuid = uuid ?? Guid.NewGuid().ToString("N").ToUpperInvariant() }, ["password"], []), ItemKind.Login, null, username)
        {
            References = references ?? [],
        };

    private IReadOnlyList<IndexedEntry> Everything() => Index(
        (new VaultEntry { Title = _github.Title, GroupPath = _github.GroupPath, Kind = ItemKind.Login, Url = "https://github.com", Username = "dan", Password = "pw" }, ["env:web"]),
        (new VaultEntry { Title = _token.Title, GroupPath = _token.GroupPath, Kind = ItemKind.ApiKey, Url = "https://api.github.com", Password = "pw" }, []),
        (new VaultEntry { Title = _gist.Title, GroupPath = _gist.GroupPath, Kind = ItemKind.Login, Url = "https://gist.github.com", Password = "pw" }, ["env:web:prod"]),
        (new VaultEntry { Title = _database.Title, GroupPath = _database.GroupPath, Kind = ItemKind.Database, Password = "pw" }, ["env:web:prod"]),
        (new VaultEntry { Title = _stripe.Title, GroupPath = _stripe.GroupPath, Url = "https://example.com", Password = "pw" }, ["env:billing"]),
        (new VaultEntry { Title = _bank.Title, GroupPath = _bank.GroupPath, Url = "https://bank.co.uk", Password = "pw" }, []));

    private IReadOnlyList<IndexedEntry> Index(params (VaultEntry Entry, string[] Tags)[] entries)
    {
        using var vault = Vault.Create(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx"), _master);

        foreach (var (entry, tags) in entries)
        {
            vault.AddEntry(entry);

            if (entry.Kind != ItemKind.None)
            {
                vault.SetKind(EntryName.Of(entry), entry.Kind);
            }

            foreach (var tag in tags)
            {
                vault.AddTag(EntryName.Of(entry), tag);
            }
        }

        vault.Save();
        Assert.Equal(SavedRead.Current, vault.ReadSavedIndex(showsUsername: true, showsLinkedHosts: false, out var index));
        return index!;
    }
}
