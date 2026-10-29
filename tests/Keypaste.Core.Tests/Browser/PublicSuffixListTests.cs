using System.Text.RegularExpressions;
using Keypaste.Core.Browser;
using Xunit;

namespace Keypaste.Core.Tests.Browser;

/// <summary>The embedded Public Suffix List gives every case of upstream's <c>test_psl.txt</c>, over both its sections (D-0615).</summary>
/// <remarks>
/// The cases are read from the vendored file, whose cases upstream wrote in Unicode were converted to A-labels as the list's
/// rules were, so each is then the same as one of upstream's own Punycode cases and is run once.
/// </remarks>
public sealed partial class PublicSuffixListTests
{
    private static string TestsPath =>
        Path.Combine(VendoredWordList.RepoRoot(), "third_party", "publicsuffix", "test_psl.txt");

    public static TheoryData<string?, string?> UpstreamCases()
    {
        var cases = new TheoryData<string?, string?>();

        foreach (var match in Cases().Distinct(StringComparer.Ordinal).Select(line => Case().Match(line)))
        {
            cases.Add(Argument(match.Groups[1].Value), Argument(match.Groups[2].Value));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(UpstreamCases))]
    public void Each_upstream_case_gives_its_registrable_domain(string? domain, string? registrable) =>
        Assert.Equal(registrable, PublicSuffixList.RegistrableDomain(domain));

    [Fact]
    public void Every_upstream_case_is_read_and_none_is_left_in_unicode()
    {
        var cases = Cases().ToList();

        Assert.True(cases.Count >= 78, $"only {cases.Count} cases were found");
        Assert.All(cases, line => Assert.Matches(Case(), line));
        Assert.All(cases, line => Assert.True(line.All(char.IsAscii), line));
        Assert.Equal(2, cases.Count(line => line == "checkPublicSuffix('xn--85x722f.xn--fiqs8s', 'xn--85x722f.xn--fiqs8s');"));
    }

    [Theory]
    [InlineData("www.example.co.uk", "example.co.uk")]
    [InlineData("co.uk", null)]
    [InlineData("nas.home.arpa", "nas.home.arpa")]
    [InlineData("alice.github.io", "alice.github.io")]
    [InlineData("gist.alice.github.io", "alice.github.io")]
    [InlineData("github.io", null)]
    [InlineData("bucket.s3.amazonaws.com", "bucket.s3.amazonaws.com")]
    [InlineData("amazonaws.com", "amazonaws.com")]
    public void Both_sections_are_read(string domain, string? registrable) =>
        Assert.Equal(registrable, PublicSuffixList.RegistrableDomain(domain));

    [Theory]
    [InlineData("bücher.de")]
    [InlineData("example.com.")]
    [InlineData("example..com")]
    [InlineData("")]
    public void A_name_that_is_not_ascii_or_has_an_empty_label_has_none(string domain) =>
        Assert.Null(PublicSuffixList.RegistrableDomain(domain));

    private static IEnumerable<string> Cases() =>
        File.ReadLines(TestsPath).Where(line => line.StartsWith("checkPublicSuffix(", StringComparison.Ordinal));

    private static string? Argument(string text) => text == "null" ? null : text[1..^1];

    [GeneratedRegex(@"^checkPublicSuffix\((null|'[^']*'), (null|'[^']*')\);$")]
    private static partial Regex Case();
}
