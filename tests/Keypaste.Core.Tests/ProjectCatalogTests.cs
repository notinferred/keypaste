using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Projects come from their entries' own tags alone; no group makes one (D-0416).</summary>
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
    public void Untagged_entries_under_env_make_no_project_and_no_environment()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), _master);
        vault.AddEntry(new VaultEntry { GroupPath = "env/acme", Title = "OLD_KEY", Password = "o" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/acme/prod", Title = "DB", Password = "d" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing/staging", Title = "KEY", Password = "k" });
        Add(vault, "services", "Stripe", "env:billing:prod");

        var billing = Assert.Single(ProjectCatalog.Read(vault).Projects);

        Assert.Equal("billing", billing.Name);
        Assert.Equal(
            [new ProjectEnvironment("prod", true, [new EntryName("services", "Stripe")])],
            billing.Environments,
            new EnvironmentComparer());
    }

    [Fact]
    public void Home_entries_make_their_environments_and_an_untagged_entry_or_a_subgroup_beside_them_adds_none()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), _master);
        ProjectVariables.Set(vault, "home", "TOKEN", "t");
        ProjectVariables.Set(vault, "home", "staging", "TOKEN", "s");
        vault.AddEntry(new VaultEntry { GroupPath = "env/home", Title = "OLD", Password = "o" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/home/qa", Title = "OLD", Password = "o" });

        var home = Assert.Single(ProjectCatalog.Read(vault).Projects);

        Assert.Equal("home", home.Name);
        Assert.Equal(
            [
                new ProjectEnvironment("dev", false, [ProjectVariables.Home("home")]),
                new ProjectEnvironment("staging", false, [ProjectVariables.Home("home", "staging")]),
            ],
            home.Environments,
            new EnvironmentComparer());
    }

    [Fact]
    public void Environments_come_dev_first_then_by_name_and_protected_last()
    {
        using var vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), _master);

        foreach (var environment in new[] { "prod", "staging", "production-eu", "alpha", "dev" })
        {
            ProjectVariables.Set(vault, "acme-api", environment, "A", "v");
        }

        Assert.Equal(
            [("dev", false), ("alpha", false), ("staging", false), ("prod", true), ("production-eu", true)],
            Assert.Single(ProjectCatalog.Read(vault).Projects).Environments.Select(environment => (environment.Name, environment.IsProtected)));
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
