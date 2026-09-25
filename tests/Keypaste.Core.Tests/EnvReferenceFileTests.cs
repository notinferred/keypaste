using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The commit-safe <c>.env.keypaste</c>: references only when written, and a found file used only for its own project (T-31).</summary>
public sealed class EnvReferenceFileTests
{
    [Fact]
    public void Format_HasTheHeaderAndReferencesOnly()
    {
        var text = EnvReferenceFile.Format("acme-api", "staging", ["DATABASE_URL", "STRIPE_SECRET_KEY"]);

        Assert.Equal(
            "# keypaste references: safe to commit, no value is stored here.\n" +
            "# `keypaste run --env-file .env.keypaste -- <command>` resolves them.\n" +
            "DATABASE_URL=kp://acme-api/staging/DATABASE_URL\n" +
            "STRIPE_SECRET_KEY=kp://acme-api/staging/STRIPE_SECRET_KEY\n",
            text);

        Assert.True(EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes(text), out var document));
        Assert.Equal(
            [
                ("DATABASE_URL", (KpReference?)new EnvReference("acme-api", "staging", "DATABASE_URL"), (string?)null),
                ("STRIPE_SECRET_KEY", new EnvReference("acme-api", "staging", "STRIPE_SECRET_KEY"), null),
            ],
            document.Lines.Select(line => (line.Name, line.Reference, line.Literal)));
    }

    [Theory]
    [InlineData("refs.env", "refs.env")]
    [InlineData("my refs.env", "<this file>")]
    [InlineData("x\nEVIL=kp://acme-api/prod/KEY", "<this file>")]
    public void Format_NamesTheFileItIsSavedAs_OnlyWhenThatNameIsSafeToRepeat(string fileName, string named)
    {
        var text = EnvReferenceFile.Format("acme-api", "staging", ["KEY"], fileName);

        Assert.Contains($"# `keypaste run --env-file {named} -- <command>` resolves them.\n", text, StringComparison.Ordinal);
        Assert.True(EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes(text), out var document));
        Assert.Equal(["KEY"], document.Lines.Select(line => line.Name));
    }

    [Fact]
    public void Parse_LiteralsPassThrough()
    {
        var document = Parse("A=kp://acme-api/dev/A\nHTTPS_PROXY=http://proxy.internal:3128\nQUOTED=\"two words\"\n");

        Assert.Empty(document.Problems);
        Assert.Equal(new ReferenceLine("HTTPS_PROXY", null, "http://proxy.internal:3128", 2), document.Lines[1]);
        Assert.Equal(new ReferenceLine("QUOTED", null, "two words", 3), document.Lines[2]);
    }

    [Fact]
    public void Parse_ABadReferenceIsAProblem()
    {
        Assert.False(EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes("GOOD=kp://acme-api/dev/GOOD\nBAD=kp://acme-api/Dev/BAD\n"), out var document));

        var problem = Assert.Single(document.Problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("'BAD' is not a usable reference", problem.Message, StringComparison.Ordinal);
        Assert.False(EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes("not a dotenv line\n"), out _));
        Assert.False(EnvReferenceFile.TryParse(new byte[DotEnv.MaximumBytes + 1], out var tooLarge));
        Assert.NotEmpty(tooLarge.Problems);
    }

    [Fact]
    public void QuotedReferenceRoundTrips()
    {
        var entry = new EntryReference(new EntryName("work", "aws"), "username");
        var text = $"AWS_USER=\"{entry}\"\nLEGACY=\"{KpReferences.For("acme-api", "dev", "LEGACY")}\"\n";

        var document = Parse(text);

        Assert.Equal(entry, document.Lines[0].Reference);
        Assert.Equal(new EnvReference("acme-api", "dev", "LEGACY"), document.Lines[1].Reference);

        // Unquoted, a fragment survives too: DotEnv strips a comment only after whitespace.
        Assert.Equal(entry, Parse($"AWS_USER={entry}\n").Lines[0].Reference);
    }

    [Fact]
    public void IsDiscoverableFor_RefusesEntryReferencesTwoProjectsAndAnotherProject()
    {
        Assert.True(EnvReferenceFile.IsDiscoverableFor(
            Parse("A=kp://acme-api/dev/A\nB=kp://acme-api/prod/B\nPROXY=http://p\n"), "acme-api", out var none));
        Assert.Empty(none);

        Assert.False(EnvReferenceFile.IsDiscoverableFor(Parse("A=kp://acme-api/dev/A\nB=kp:///work/aws\n"), "acme-api", out var entries));
        Assert.Equal("vault entries", entries);

        Assert.False(EnvReferenceFile.IsDiscoverableFor(Parse("A=kp://acme-api/dev/A\nB=kp://billing/dev/B\n"), "acme-api", out var two));
        Assert.Equal("several projects (acme-api, billing)", two);

        Assert.False(EnvReferenceFile.IsDiscoverableFor(Parse("A=kp://billing/dev/A\n"), "acme-api", out var other));
        Assert.Equal("project 'billing'", other);

        Assert.False(EnvReferenceFile.IsDiscoverableFor(Parse("NODE_OPTIONS=--require ./x.js\n"), "acme-api", out var literals));
        Assert.Equal("no project", literals);
    }

    [Fact]
    public void WithProfile_RewritesEnvReferencesOnly()
    {
        var document = Parse("A=kp://acme-api/dev/A\nB=kp://acme-api/staging/B\nC=kp:///work/aws#url\nD=literal\n");

        var rewritten = EnvReferenceFile.WithProfile(document, "prod");

        Assert.Equal(
            [
                new EnvReference("acme-api", "prod", "A"),
                new EnvReference("acme-api", "prod", "B"),
                new EntryReference(new EntryName("work", "aws"), "url"),
                null,
            ],
            rewritten.Lines.Select(line => line.Reference));
        Assert.Equal("literal", rewritten.Lines[3].Literal);
    }

    private static EnvReferenceDocument Parse(string text)
    {
        Assert.True(EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes(text), out var document), string.Join("; ", document.Problems));
        return document;
    }
}
