using Xunit;

namespace Keypaste.Core.Tests.Browser;

/// <summary>8.3a1's rule that matching a page to an item uses no network: the list is embedded, never fetched (D-0615).</summary>
/// <remarks>
/// A text scan of <c>Core/Browser/</c>, in which a name in a comment counts too. The change it must fail is the list downloaded
/// at run time, which would make matching depend on a server (PRODUCT §4.1).
/// </remarks>
public sealed class BrowserSourceRulesTests
{
    private static readonly string[] _network =
    [
        "System.Net", "HttpClient", "HttpMessage", "Socket", "WebRequest", "WebClient", "Dns.", "Keypaste.Core.Sharing", "ShareClient",
    ];

    [Fact]
    public void Matching_reaches_no_network()
    {
        var offenders = Sources().SelectMany(source => Offenders(source.Name, source.Text)).ToList();

        Assert.True(offenders.Count == 0, $"browser matching reaches the network: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_reads_the_matcher_and_the_list_and_finds_each_network_name_added()
    {
        var sources = Sources().ToList();
        Assert.Contains(sources, source => source.Name == "LoginLookup.cs");
        Assert.Contains(sources, source => source.Name == "PublicSuffixList.cs");

        var list = sources.Single(source => source.Name == "PublicSuffixList.cs").Text;

        foreach (var name in _network)
        {
            Assert.NotEmpty(Offenders("PublicSuffixList.cs", list + Environment.NewLine + name));
        }
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        _network.Where(marker => text.Contains(marker, StringComparison.Ordinal)).Select(marker => $"{name}: {marker}");

    private static IEnumerable<(string Name, string Text)> Sources() =>
        Directory.GetFiles(Path.Combine(VendoredWordList.RepoRoot(), "src", "Keypaste.Core", "Browser"), "*.cs")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
}
