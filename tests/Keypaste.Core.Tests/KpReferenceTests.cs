using Xunit;

namespace Keypaste.Core.Tests;

/// <summary><c>kp://</c> references: one canonical spelling each, and nothing malformed read as one (D-0349).</summary>
public sealed class KpReferenceTests
{
    [Fact]
    public void An_env_reference_round_trips()
    {
        var text = KpReferences.For("acme-api", "staging", "DATABASE_URL");

        Assert.Equal("kp://acme-api/staging/DATABASE_URL", text);
        Assert.True(KpReferences.TryParse(text, out var reference, out _));
        Assert.Equal(new EnvReference("acme-api", "staging", "DATABASE_URL"), reference);
        Assert.Equal(text, reference.ToString());
    }

    [Fact]
    public void The_dev_profile_names_the_flat_project_group()
    {
        Assert.True(KpReferences.TryParse("kp://acme-api/dev/KEY", out var reference, out _));
        Assert.Equal(new EnvReference("acme-api", "dev", "KEY"), reference);
    }

    [Theory]
    [InlineData("", "github", "password", "kp:///github")]
    [InlineData("work/cloud", "aws", "username", "kp:///work/cloud/aws#username")]
    [InlineData("a", "one/two", "password", "kp:///a/one%2Ftwo")]
    [InlineData("a", "hash#tag", "url", "kp:///a/hash%23tag#url")]
    [InlineData("a", "100%", "notes", "kp:///a/100%25#notes")]
    [InlineData("my group", "with space", "password", "kp:///my%20group/with%20space")]
    [InlineData("a", "café", "password", "kp:///a/caf%C3%A9")]
    public void An_entry_reference_round_trips(string group, string title, string field, string expected)
    {
        var entry = new EntryName(group, title);
        var text = KpReferences.For(entry, field);

        Assert.Equal(expected, text);
        Assert.True(KpReferences.TryParse(text, out var reference, out var error), error);
        Assert.Equal(new EntryReference(entry, field), reference);
        Assert.Equal(text, reference.ToString());
    }

    [Fact]
    public void An_explicit_password_fragment_reads_as_the_default()
    {
        Assert.True(KpReferences.TryParse("kp:///github#password", out var reference, out _));
        Assert.Equal("kp:///github", reference.ToString());
    }

    [Theory]
    [InlineData("https://acme-api/dev/KEY")]
    [InlineData("KP://acme-api/dev/KEY")]
    [InlineData("kp:/acme-api/dev/KEY")]
    [InlineData("kp://acme-api//KEY")]
    [InlineData("kp:///a//b")]
    [InlineData("kp:///")]
    [InlineData("kp://")]
    [InlineData("kp://acme-api/KEY")]
    [InlineData("kp://acme-api/dev/KEY/MORE")]
    [InlineData("kp://acme-api/dev/KEY#password")]
    [InlineData("kp://acme-api/dev/BAD-KEY")]
    [InlineData("kp://acme-api/Dev/KEY")]
    [InlineData("kp://acme-api/dev_x/KEY")]
    [InlineData("kp:///github#secret")]
    [InlineData("kp:///a%2")]
    [InlineData("kp:///a%GG")]
    [InlineData("kp:///a%0A")]
    [InlineData("kp:///a%FF")]
    [InlineData("kp:///a b")]
    [InlineData("kp:///a%2Fb/title")]
    public void A_malformed_reference_is_refused_with_a_reason(string text)
    {
        Assert.False(KpReferences.TryParse(text, out var reference, out var error));
        Assert.Null(reference);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_reference_longer_than_2048_characters_is_refused()
    {
        var atLimit = "kp:///" + new string('a', KpReferences.MaximumLength - "kp:///".Length);

        Assert.True(KpReferences.TryParse(atLimit, out _, out _));
        Assert.False(KpReferences.TryParse(atLimit + "a", out _, out var error));
        Assert.Contains("2048", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("env/acme-api", "KEY", "kp://acme-api/dev/KEY")]
    [InlineData("env/acme-api/staging", "KEY", "kp://acme-api/staging/KEY")]
    [InlineData("env/acme-api/dev", "KEY", "kp:///env/acme-api/dev/KEY")]
    [InlineData("env/acme-api/Prod", "KEY", "kp:///env/acme-api/Prod/KEY")]
    [InlineData("env/acme-api/staging/deeper", "KEY", "kp:///env/acme-api/staging/deeper/KEY")]
    [InlineData("env/acme-api", "not-a-key", "kp:///env/acme-api/not-a-key")]
    [InlineData("work", "github", "kp:///work/github")]
    public void ForEntry_uses_the_env_form_only_for_a_variable_keypaste_resolves(string group, string title, string expected) =>
        Assert.Equal(expected, KpReferences.ForEntry(new EntryName(group, title)));

    [Fact]
    public void ForEntry_names_nothing_for_a_reserved_or_untitled_entry()
    {
        Assert.Null(KpReferences.ForEntry(new EntryName(ReservedGroups.Tokens, "t1")));
        Assert.Null(KpReferences.ForEntry(new EntryName("work", string.Empty)));
    }
}
