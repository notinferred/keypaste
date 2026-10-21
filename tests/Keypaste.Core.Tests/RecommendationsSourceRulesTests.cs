using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V.9's local rule: the recommendation checks, the one file that reads passwords for them and the app's
/// list make no network request.
/// </summary>
/// <remarks>
/// A text scan, in which a name in a comment counts too. The change it must fail is a breach lookup or any
/// other request added beside the local checks, which needs its own privacy design first (BACKLOG P.8).
/// </remarks>
public sealed class RecommendationsSourceRulesTests
{
    private static readonly string[] _network =
    [
        "System.Net", "HttpClient", "HttpMessage", "Socket", "WebRequest", "WebClient", "Dns.", "Keypaste.Core.Sharing", "ShareClient",
    ];

    [Fact]
    public void The_checks_and_the_list_reach_no_network()
    {
        var offenders = Sources().SelectMany(source => Offenders(source.Name, source.Text)).ToList();

        Assert.True(offenders.Count == 0, $"recommendations reach the network: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_reads_every_check_and_each_network_name_added_is_found()
    {
        var sources = Sources().ToList();
        Assert.Contains(sources, source => source.Name == "PasswordHealth.cs");
        Assert.Contains(sources, source => source.Name == "KeePassInterop.cs");
        Assert.Contains(sources, source => source.Name == "RecommendationsViewModel.cs");
        Assert.True(sources.Count >= 9, $"only {sources.Count} files were scanned");

        var health = sources.Single(source => source.Name == "PasswordHealth.cs").Text;

        foreach (var name in _network)
        {
            Assert.NotEmpty(Offenders("PasswordHealth.cs", health + Environment.NewLine + name));
        }
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        _network.Where(marker => text.Contains(marker, StringComparison.Ordinal)).Select(marker => $"{name}: {marker}");

    private static IEnumerable<(string Name, string Text)> Sources()
    {
        var root = VendoredWordList.RepoRoot();
        var viewModels = Path.Combine(root, "src", "Keypaste.App", "ViewModels");

        return Directory.GetFiles(Path.Combine(root, "src", "Keypaste.Core", "Recommendations"), "*.cs")
            .Append(Path.Combine(root, "src", "Keypaste.Core", "Internal", "KeePassInterop.cs"))
            .Concat(new[] { "RecommendationsViewModel.cs", "RecommendationRow.cs", "LostTagRow.cs", "PasswordRow.cs" }.Select(file => Path.Combine(viewModels, file)))
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
    }
}
