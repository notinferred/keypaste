using System.Globalization;
using Keypaste.App.Terms;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>The terms of use the first run shows are read from the one copy the app carries, dated, in headed paragraphs (D-0597).</summary>
public sealed class TermsOfUseTests
{
    [Fact]
    public void The_carried_terms_read_with_a_date_and_headed_paragraphs()
    {
        var terms = TermsOfUse.Carried;

        Assert.True(DateOnly.TryParseExact(terms.Version, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), terms.Version);
        Assert.EndsWith(terms.Version, terms.Status, StringComparison.Ordinal);
        Assert.NotEmpty(terms.Title);
        Assert.NotEmpty(terms.Sections);
        Assert.All(terms.Sections, section => Assert.NotEmpty(section.Paragraphs));
        Assert.DoesNotContain(terms.Sections.SelectMany(section => section.Paragraphs).Append(terms.Title), line => line.StartsWith("//", StringComparison.Ordinal));
    }

    [Fact]
    public void Notes_are_left_out_and_each_heading_takes_the_paragraphs_under_it()
    {
        var terms = TermsOfUse.Parse("// a note\nTitle\nDraft version 2027-01-02\n## One\nfirst\n// another note\nsecond\n## Two\nthird\n");

        Assert.Equal(("Title", "Draft version 2027-01-02", "2027-01-02"), (terms.Title, terms.Status, terms.Version));
        Assert.Equal(["One", "Two"], terms.Sections.Select(section => section.Heading));
        Assert.Equal(["first", "second"], terms.Sections[0].Paragraphs);
        Assert.Equal(["third"], terms.Sections[1].Paragraphs);
    }

    [Theory]
    [InlineData("Title\n## One\nfirst\n")]
    [InlineData("Title\nDraft version soon\n## One\nfirst\n")]
    [InlineData("Title\nDraft version 2027-01-02\nfirst\n## One\nsecond\n")]
    [InlineData("## One\nDraft version 2027-01-02\n## Two\nfirst\n")]
    public void Terms_in_another_form_are_refused(string text) =>
        Assert.Throws<FormatException>(() => TermsOfUse.Parse(text));
}
