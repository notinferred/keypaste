using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What a project tag change reaches, said before it is written (D-0415).</summary>
public sealed class ProjectTagChangeTests : IDisposable
{
    private static readonly EntryName _stripe = new("services", "Stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-tag-change-tests-").FullName;
    private readonly Vault _vault;

    public ProjectTagChangeTests()
    {
        _vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), EnvStoreTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "login" });
        Assert.True(_vault.SetFields(_stripe, [new FieldWrite("STRIPE_KEY", "s"), new FieldWrite("WEBHOOK", "w"), new FieldWrite("Region", "eu")]));
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void An_ordinary_tag_needs_no_question()
    {
        Assert.Null(ProjectTagChange.Preview(_vault, _stripe, ["finance"], adding: true));
    }

    [Fact]
    public void Adding_a_member_tag_names_the_environment_its_protection_and_only_the_variable_fields()
    {
        var change = ProjectTagChange.Preview(_vault, _stripe, ["env:billing:prod"], adding: true)!;

        Assert.True(change.Moves);
        Assert.Equal("billing/prod", change.Environment);
        Assert.Equal(
            [
                "env:billing:prod puts services/Stripe in billing/prod, a protected environment whose every release is asked live.",
                "Joining billing/prod: STRIPE_KEY, WEBHOOK.",
            ],
            change.Describe());
        Assert.DoesNotContain(change.Describe(), line => line.Contains("Region", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tag_that_leaves_the_entry_where_another_tag_puts_it_changes_no_environment()
    {
        Assert.True(_vault.AddTag(_stripe, "env:billing"));

        Assert.Equal(
            ["services/Stripe is already in billing/dev through another tag, so adding env:billing:dev changes no environment."],
            ProjectTagChange.Preview(_vault, _stripe, ["env:billing:dev"], adding: true)!.Describe());

        Assert.True(_vault.AddTag(_stripe, "env:billing:dev"));
        Assert.Equal(
            ["services/Stripe stays in billing/dev through another tag, so removing env:billing:dev changes no environment."],
            ProjectTagChange.Preview(_vault, _stripe, ["env:billing:dev"], adding: false)!.Describe());

        Assert.Equal(
            ["Removing env:billing, env:billing:dev takes services/Stripe out of billing/dev.", "Leaving billing/dev: STRIPE_KEY, WEBHOOK."],
            ProjectTagChange.Preview(_vault, _stripe, ["env:billing", "env:billing:dev"], adding: false)!.Describe());
    }

    [Fact]
    public void A_malformed_tag_says_it_joins_no_project_and_whether_it_protects()
    {
        var shouted = ProjectTagChange.Preview(_vault, _stripe, ["env:billing:Prod"], adding: true)!;
        var described = shouted.Describe();

        Assert.Null(shouted.Environment);
        Assert.StartsWith("env:billing:Prod puts services/Stripe in no project: ", described[0], StringComparison.Ordinal);
        Assert.Equal("While the entry carries it, every agent request for the entry is asked live.", described[1]);
        Assert.Equal("It changes no environment.", ProjectTagChange.Preview(_vault, _stripe, ["Env:billing"], adding: true)!.Describe()[1]);
    }
}
