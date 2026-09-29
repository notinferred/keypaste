using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Projects are the tag projects together with the legacy <c>env/</c> groups.</summary>
public sealed class ProjectCatalogTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-catalog-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Tagged_entries_make_a_project_with_its_environments_and_exactly_its_members()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), _master);
        Add(vault, "services", "Stripe", "env:billing", "finance");
        Add(vault, "services", "Database", "env:billing:prod");
        Add(vault, "services", "Odd", "env:billing:Prod");
        Add(vault, "services", "Other", "finance");

        var catalog = ProjectCatalog.Read(vault);

        var billing = Assert.Single(catalog.Projects);
        Assert.Equal("billing", billing.Name);
        Assert.False(billing.IsLegacy);
        Assert.Equal(
            [
                new ProjectEnvironment("dev", false, [new EntryName("services", "Stripe")]),
                new ProjectEnvironment("prod", true, [new EntryName("services", "Database")]),
            ],
            billing.Environments,
            new EnvironmentComparer());

        var problem = Assert.Single(catalog.Problems);
        Assert.Equal((new EntryName("services", "Odd"), "env:billing:Prod", true), (problem.Entry, problem.Tag, problem.Protects));
        Assert.NotEmpty(problem.Problem);
    }

    [Fact]
    public void A_legacy_group_is_a_project_marked_legacy_and_shares_its_name_with_tags()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), _master);
        vault.AddEntry(new VaultEntry { GroupPath = "env/acme", Title = "TOKEN", Password = "t" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/acme/staging", Title = "TOKEN", Password = "t" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = "KEY", Password = "k" });
        Add(vault, "services", "Stripe", "env:billing:prod");

        var catalog = ProjectCatalog.Read(vault);

        Assert.Equal(["acme", "billing"], catalog.Projects.Select(project => project.Name));
        Assert.All(catalog.Projects, project => Assert.True(project.IsLegacy));
        Assert.Equal(["dev", "staging"], catalog.Projects[0].Environments.Select(environment => environment.Name));
        Assert.All(catalog.Projects[0].Environments, environment => Assert.Empty(environment.Members));
        Assert.Equal(["dev", "prod"], catalog.Projects[1].Environments.Select(environment => environment.Name));
        Assert.Equal([new EntryName("services", "Stripe")], catalog.Projects[1].Environments[1].Members);
    }

    [Fact]
    public void Entries_in_the_recycle_bin_or_keypastes_own_groups_belong_to_no_project()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), _master);
        Add(vault, ".keypaste/tokens", "planted", "env:billing");
        Add(vault, "services", "Deleted", "env:billing:prod");
        vault.RemoveEntry(new EntryName("services", "Deleted"), out _);

        Assert.Empty(ProjectCatalog.Read(vault).Projects);
    }

    private static void Add(Vault vault, string group, string title, params string[] tags)
    {
        var name = new EntryName(group, title);
        vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = "p" });

        foreach (var tag in tags)
        {
            vault.AddTag(name, tag);
        }
    }

    private sealed class EnvironmentComparer : IEqualityComparer<ProjectEnvironment>
    {
        public bool Equals(ProjectEnvironment? x, ProjectEnvironment? y) =>
            x is not null && y is not null && x.Name == y.Name && x.IsProtected == y.IsProtected && x.Members.SequenceEqual(y.Members);

        public int GetHashCode(ProjectEnvironment obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
