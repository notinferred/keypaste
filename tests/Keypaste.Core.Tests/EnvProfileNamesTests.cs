using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Which names are profiles, which of them are protected, and where each lives (D-0347, D-0348).</summary>
public sealed class EnvProfileNamesTests
{
    [Theory]
    [InlineData("dev")]
    [InlineData("staging")]
    [InlineData("prod-eu-1")]
    [InlineData("0")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void A_lowercase_name_of_up_to_32_characters_is_a_profile(string name) =>
        Assert.True(EnvProfileNames.IsValid(name, out _));

    [Theory]
    [InlineData("")]
    [InlineData("Dev")]
    [InlineData("-x")]
    [InlineData("a_b")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Anything_else_is_refused_with_a_reason(string name)
    {
        Assert.False(EnvProfileNames.IsValid(name, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("prod", true)]
    [InlineData("Prod", true)]
    [InlineData("PRODUCTION", true)]
    [InlineData("production-eu", true)]
    [InlineData("prod-1", true)]
    [InlineData("producer", false)]
    [InlineData("preprod", false)]
    [InlineData("staging", false)]
    [InlineData("dev", false)]
    public void Prod_and_production_and_their_dashed_variants_are_protected_in_any_case(string name, bool expected) =>
        Assert.Equal(expected, EnvProfileNames.IsProtected(name));

    [Fact]
    public void The_dev_profile_is_the_project_group_and_any_other_is_a_subgroup()
    {
        Assert.Equal("env/acme-api", EnvProfileNames.GroupPath("acme-api", "dev"));
        Assert.Equal("env/acme-api/staging", EnvProfileNames.GroupPath("acme-api", "staging"));
    }

    [Theory]
    [InlineData("env/a/prod", true)]
    [InlineData("env/a/Prod", true)]
    [InlineData("env/a/staging/prod", true)]
    [InlineData("env/a", false)]
    [InlineData("env/a/staging", false)]
    [InlineData("env/prod", false)]
    [InlineData("other/prod", false)]
    public void Releasing_an_entry_is_asked_live_when_any_segment_below_its_project_is_protected(string group, bool expected) =>
        Assert.Equal(expected, EnvProfileNames.RequiresLiveApproval(new EntryName(group, "K")));
}
