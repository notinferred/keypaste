using System.Text;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>
/// The project tags KeePassXC writes are the ones keypaste reads and the ones keypaste writes are the ones KeePassXC reads; the
/// fields of entries KeePassXC tagged are a project's variables, as KeePassXC shows them, and an untagged entry under env/ is none.
/// </summary>
/// <remarks>How an edit is previewed, confirmed and refused, and every refusal's words, are the tests' of those calls.</remarks>
public sealed class ProjectsTests
{
    private static readonly EntryName _stripe = new("services", "Stripe");
    private static readonly EntryName _other = new("services", "Other");

    [Fact]
    public void Keypaste_lists_the_projects_KeePassXC_tagged_and_reports_a_tag_in_the_wrong_case_and_ignores_group_and_ordinary_tags()
    {
        using var file = Imported(TaggedSeed(groupTag: true));
        Assert.Equal(1, File.ReadAllBytes(file.Path_)[8]);

        using var vault = file.Open();
        var catalog = ProjectCatalog.Read(vault);

        var billing = Assert.Single(catalog.Projects);
        Assert.Equal("billing", billing.Name);
        Assert.Equal(
            [("dev", false, "services/Stripe"), ("prod", true, "services/Database")],
            billing.Environments.Select(e => (e.Name, e.IsProtected, string.Join(",", e.Members.Select(m => $"{m.GroupPath}/{m.Title}")))));
        var problem = Assert.Single(catalog.Problems);
        Assert.Equal(("env:billing:Prod", "Odd"), (problem.Tag, problem.Entry.Title));
    }

    [Fact]
    public void KeePassXC_reads_the_tags_keypaste_adds_and_removes_with_the_files_version_and_group_tag_kept()
    {
        using var plain = Imported(TaggedSeed(groupTag: false));
        Assert.Equal(0, File.ReadAllBytes(plain.Path_)[8]);
        Tag(plain, vault => vault.AddTag(_stripe, "env:billing:prod"));
        Assert.Equal(["env:billing", "env:billing:prod", "finance"], Tags(plain, "services/Stripe"));
        Assert.Equal(0, File.ReadAllBytes(plain.Path_)[8]);
        Tag(plain, vault => vault.RemoveTags(_stripe, ["env:billing"]));
        Assert.Equal(["env:billing:prod", "finance"], Tags(plain, "services/Stripe"));

        using var grouped = Imported(TaggedSeed(groupTag: true));
        var version = File.ReadAllBytes(grouped.Path_)[8];
        Tag(grouped, vault => vault.AddTag(_other, "env:billing:qa"));
        Assert.Contains("env:billing:qa", Tags(grouped, "services/Other"));
        Assert.Equal(version, File.ReadAllBytes(grouped.Path_)[8]);
        Assert.Single(KeePassXcCli.Export(grouped.Path_).Descendants("Tags"), tags => tags.Value == "env:billing:staging");
    }

    [Fact]
    public void A_tag_KeePassXC_merged_in_is_read_and_reported()
    {
        using var file = Imported(TaggedSeed(groupTag: true));
        var newer = Path.Combine(file.Directory_, "newer.kdbx");
        var seed = Path.Combine(file.Directory_, "newer.xml");
        File.WriteAllText(seed, TaggedSeed(groupTag: true)
            .Replace("<Tags>finance</Tags>", "<Tags>finance,env:billing:Prod</Tags><Times><LastModificationTime>2037-01-01T00:00:00Z</LastModificationTime></Times>", StringComparison.Ordinal));
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", seed, newer]).Exit);
        Assert.Equal(0, KeePassXcCli.Try(KeePassXcCli.Password, ["merge", "-q", "-s", file.Path_, newer]).Exit);

        Assert.Contains("env:billing:Prod", Tags(file, "services/Other"));
        using var vault = file.Open();
        Assert.Contains(ProjectCatalog.Read(vault).Problems, problem => problem.Entry.Title == "Other" && problem.Tag == "env:billing:Prod");
    }

    [Fact]
    public void Each_environment_resolves_to_exactly_the_tagged_fields_KeePassXC_wrote_and_an_untagged_entry_stays_ordinary()
    {
        using var file = Imported(FieldsSeed(databaseUrl: "db-c1b", extra: ""));
        using var vault = file.Open();

        Assert.Equal(
            [new EnvVariable("DATABASE_URL", "db-c1b"), new EnvVariable("STRIPE_SECRET_KEY", "stripe-c1b")],
            EnvResolution.Resolve(vault, "billing", TimeProvider.System).Variables);
        foreach (var environment in new[] { "staging", "prod" })
        {
            Assert.Equal([new EnvVariable("SHARED_KEY", "shared-c1b")], EnvResolution.Resolve(vault, "billing", environment, TimeProvider.System).Variables);
        }

        Assert.Equal("old-c1b", vault.Find(new EntryName("env/billing", "OLD_KEY"))!.Password);
    }

    [Fact]
    public void A_value_holding_PASSWORD_is_released_filled_in_from_its_own_entry_as_KeePassXC_shows_it()
    {
        using var file = Imported(FieldsSeed(databaseUrl: "postgres://u:{PASSWORD}@db-c1b", extra: ""));
        var shown = KeePassXcCli.Show(file.Path_, "services/Database", "DATABASE_URL")[0];
        Assert.Equal("postgres://u:database-login-c1b@db-c1b", shown);

        using var vault = file.Open();
        Assert.Contains(new EnvVariable("DATABASE_URL", shown), EnvResolution.Resolve(vault, "billing", TimeProvider.System).Variables);
    }

    [Fact]
    public void Values_using_KeePassXCs_escape_are_released_as_KeePassXC_2_7_11_gives_them_with_nothing_in_an_escape_filled_in()
    {
        const string escape = """
            <Entry><UUID>ZmVzY2FwZSAgICAgICAgIA==</UUID><Tags>env:billing</Tags><String><Key>Title</Key><Value>Escape</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">escape-login-c1b</Value></String>
            <String><Key>ESCAPED_WHOLE</Key><Value ProtectInMemory="True">\{PASSWORD\}</Value></String>
            <String><Key>ESCAPED_FIELD</Key><Value ProtectInMemory="True">x{S:ESCAPED_WHOLE}y</Value></String>
            <String><Key>ESCAPED_PART</Key><Value ProtectInMemory="True">a\{PASSWORD\}</Value></String></Entry>
            """;
        using var file = Imported(FieldsSeed(databaseUrl: "db-c1b", extra: escape));
        string[] expected = ["{PASSWORD}", "x{PASSWORD}y", @"a\{PASSWORD\}"];

        using (var vault = file.Open())
        {
            var released = EnvResolution.Resolve(vault, "billing", TimeProvider.System).Variables.ToDictionary(v => v.Key, v => v.Value);
            Assert.Equal(expected, new[] { "ESCAPED_WHOLE", "ESCAPED_FIELD", "ESCAPED_PART" }.Select(key => released[key]));
        }

        // KeePassXC reads the escape from 2.7.11 on; an earlier one holds keypaste to what 2.7.11 gives.
        var shown = KeePassXcCli.Show(file.Path_, "services/Escape", "ESCAPED_WHOLE", "ESCAPED_FIELD", "ESCAPED_PART");
        if (KeePassXcCli.Version >= new Version(2, 7, 11))
        {
            Assert.Equal(expected, shown);
        }
        else
        {
            Assert.NotEqual("{PASSWORD}", shown[0]);
        }
    }

    private static KeePassXcVault Imported(string seed)
    {
        var file = new KeePassXcVault();
        var xml = Path.Combine(file.Directory_, "seed.xml");
        File.WriteAllText(xml, seed);
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", xml, file.Path_]).Exit);
        return file;
    }

    private static void Tag(KeePassXcVault file, Func<Vault, bool> change)
    {
        using var vault = file.Open();
        Assert.True(change(vault));
        vault.Save();
    }

    private static string[] Tags(KeePassXcVault file, string entry) =>
        [.. KeePassXcCli.Show(file.Path_, entry, "Tags")[0].Split(',', StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal)];

    private static string Uuid(string name) => Convert.ToBase64String(Encoding.ASCII.GetBytes(name.PadRight(16)[..16]));

    private static string Untagged() => $"""
        <Group><UUID>{Uuid("untagged-env")}</UUID><Name>env</Name>
          <Group><UUID>{Uuid("untagged-bill")}</UUID><Name>billing</Name>
            <Entry><UUID>{Uuid("old-key")}</UUID><String><Key>Title</Key><Value>OLD_KEY</Value></String>
              <String><Key>Password</Key><Value ProtectInMemory="True">old-c1b</Value></String></Entry>
            <Group><UUID>{Uuid("untagged-prod")}</UUID><Name>prod</Name>
              <Entry><UUID>{Uuid("prod-db")}</UUID><String><Key>Title</Key><Value>DB</Value></String>
                <String><Key>Password</Key><Value ProtectInMemory="True">prod-db-c1b</Value></String></Entry>
            </Group>
          </Group>
        </Group>
        """;

    private static string TaggedSeed(bool groupTag) => $"""
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <KeePassFile><Meta><Generator>keypaste-tests</Generator></Meta><Root><Group><UUID>{Uuid("root")}</UUID><Name>Root</Name>
        {Untagged()}
        <Group><UUID>{Uuid("services")}</UUID><Name>services</Name>{(groupTag ? "<Tags>env:billing:staging</Tags>" : "")}
          <Entry><UUID>{Uuid("stripe")}</UUID><Tags>env:billing,finance</Tags>
            <CustomData><Item><Key>projects.entry</Key><Value>projects-entry-data</Value></Item></CustomData>
            <String><Key>Title</Key><Value>Stripe</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">stripe-login</Value></String>
            <String><Key>STRIPE_SECRET_KEY</Key><Value ProtectInMemory="True">sk_test_projects</Value></String>
            <String><Key>Region</Key><Value>eu</Value></String></Entry>
          <Entry><UUID>{Uuid("database")}</UUID><Tags>env:billing:prod</Tags>
            <String><Key>Title</Key><Value>Database</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">database-password</Value></String></Entry>
          <Entry><UUID>{Uuid("odd")}</UUID><Tags>env:billing:Prod</Tags>
            <String><Key>Title</Key><Value>Odd</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">odd-password</Value></String></Entry>
          <Entry><UUID>{Uuid("other")}</UUID><Tags>finance</Tags>
            <String><Key>Title</Key><Value>Other</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">other-password</Value></String></Entry>
        </Group>
        </Group></Root></KeePassFile>
        """;

    private static string FieldsSeed(string databaseUrl, string extra) => $"""
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <KeePassFile><Meta><Generator>keypaste-tests</Generator></Meta><Root><Group><UUID>{Uuid("froot")}</UUID><Name>Root</Name>
        {Untagged()}
        <Group><UUID>{Uuid("fservices")}</UUID><Name>services</Name>
          <Entry><UUID>{Uuid("fstripe")}</UUID><Tags>env:billing,finance</Tags>
            <String><Key>Title</Key><Value>Stripe</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">stripe-login-c1b</Value></String>
            <String><Key>STRIPE_SECRET_KEY</Key><Value ProtectInMemory="True">stripe-c1b</Value></String>
            <String><Key>Region</Key><Value>eu-c1b</Value></String></Entry>
          <Entry><UUID>{Uuid("fdatabase")}</UUID><Tags>env:billing</Tags>
            <String><Key>Title</Key><Value>Database</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">database-login-c1b</Value></String>
            <String><Key>DATABASE_URL</Key><Value ProtectInMemory="True">{databaseUrl}</Value></String></Entry>
          <Entry><UUID>{Uuid("fshared")}</UUID><Tags>env:billing:staging,env:billing:prod</Tags>
            <String><Key>Title</Key><Value>Shared</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">shared-login-c1b</Value></String>
            <String><Key>SHARED_KEY</Key><Value ProtectInMemory="True">shared-c1b</Value></String></Entry>
          {extra}
        </Group>
        </Group></Root></KeePassFile>
        """;
}
