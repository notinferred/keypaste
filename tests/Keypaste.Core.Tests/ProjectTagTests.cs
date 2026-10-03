using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The project-tag grammar: which tags put an entry in a project, which are reported, and which protect.</summary>
public sealed class ProjectTagTests
{
    [Theory]
    [InlineData("env:billing", "billing", "dev", false)]
    [InlineData("env:billing:dev", "billing", "dev", false)]
    [InlineData("env:billing:prod", "billing", "prod", true)]
    [InlineData("env:billing:production-eu", "billing", "production-eu", true)]
    [InlineData("env:billing:staging", "billing", "staging", false)]
    [InlineData("env:acme api:qa-2", "acme api", "qa-2", false)]
    public void A_well_formed_tag_names_a_project_and_an_environment(string tag, string project, string environment, bool protects)
    {
        var read = ProjectTag.Read(tag);

        Assert.Equal(ProjectTagKind.Member, read.Kind);
        Assert.Equal(project, read.Project);
        Assert.Equal(environment, read.Environment);
        Assert.Equal(protects, read.Protects);
        Assert.Empty(read.Problem);
    }

    [Theory]
    [InlineData("env:billing:Prod", true)]
    [InlineData("env:billing:PRODUCTION", true)]
    [InlineData("env:billing:prod:eu", true)]
    [InlineData("env:billing:Staging", false)]
    [InlineData("env:", false)]
    [InlineData("env::prod", true)]
    [InlineData("env: billing", false)]
    [InlineData("env:bill/ing", false)]
    [InlineData("env:billing:-dev", false)]
    [InlineData("env:billing:" + "a123456789012345678901234567890123", false)]
    [InlineData("Env:billing:prod", true)]
    [InlineData("ENV:billing", false)]
    public void A_malformed_tag_is_reported_grants_nothing_and_protects_when_it_names_a_protected_environment(string tag, bool protects)
    {
        var read = ProjectTag.Read(tag);

        Assert.Equal(ProjectTagKind.Malformed, read.Kind);
        Assert.Null(read.Project);
        Assert.Null(read.Environment);
        Assert.Equal(protects, read.Protects);
        Assert.NotEmpty(read.Problem);
    }

    [Theory]
    [InlineData("finance")]
    [InlineData("environment")]
    [InlineData("envy:billing")]
    [InlineData("prod")]
    [InlineData("env-billing")]
    public void Any_other_tag_is_not_a_project_tag_and_protects_nothing(string tag)
    {
        var read = ProjectTag.Read(tag);

        Assert.Equal(ProjectTagKind.None, read.Kind);
        Assert.False(read.Protects);
    }

    [Theory]
    [InlineData("billing", "dev", "env:billing")]
    [InlineData("billing", "prod", "env:billing:prod")]
    public void The_tag_for_a_project_and_environment_reads_back_as_them(string project, string environment, string expected)
    {
        Assert.True(ProjectTag.TryFor(project, environment, out var tag, out _));
        Assert.Equal(expected, tag);

        var read = ProjectTag.Read(tag);
        Assert.Equal((project, environment), (read.Project, read.Environment));
    }

    [Theory]
    [InlineData("bill:ing", "dev")]
    [InlineData("bill/ing", "dev")]
    [InlineData("", "dev")]
    [InlineData("billing", "Prod")]
    [InlineData("billing", "")]
    public void No_tag_is_made_for_a_name_the_grammar_refuses(string project, string environment)
    {
        Assert.False(ProjectTag.TryFor(project, environment, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a,b")]
    [InlineData("a;b")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    public void A_tag_KeePass_would_split_or_trim_is_refused(string tag)
    {
        Assert.False(TagRules.IsValid(tag, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("env:billing:prod")]
    [InlineData("finance")]
    [InlineData("two words")]
    public void An_ordinary_tag_is_accepted(string tag)
    {
        Assert.True(TagRules.IsValid(tag, out _));
    }

    [Fact]
    public void An_entry_is_live_only_by_a_tag_that_protects()
    {
        Assert.False(EnvProfileNames.RequiresLiveApproval(["finance", "env:billing", "env:billing:staging"]));
        Assert.True(EnvProfileNames.RequiresLiveApproval(["finance", "env:billing:prod"]));
        Assert.True(EnvProfileNames.RequiresLiveApproval(["env:billing:Prod"]));
        Assert.False(EnvProfileNames.RequiresLiveApproval([]));
    }
}
