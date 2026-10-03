using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Which names are profiles, which of them are protected, and how a set is named (D-0348, D-0416).</summary>
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
    public void The_dev_set_is_named_for_the_project_and_any_other_for_its_profile()
    {
        Assert.Equal("env/acme-api", EnvProfileNames.SetName("acme-api", "dev"));
        Assert.Equal("env/acme-api/staging", EnvProfileNames.SetName("acme-api", "staging"));
    }
}
