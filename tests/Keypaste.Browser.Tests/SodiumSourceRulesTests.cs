using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>
/// Only <c>Keypaste.Browser</c> calls libsodium and only the NativeAOT <c>keypaste</c> links it, so Keypaste.Core, the bridge
/// and the app carry none of it (D-0611).
/// </summary>
/// <remarks>A text scan of the repository's C# sources and projects, in which a name in a comment counts too.</remarks>
public sealed class SodiumSourceRulesTests
{
    private const string _import = "\"libsodium\"";

    private const string _link = "<DirectPInvoke Include=\"libsodium\"";

    private const string _reference = "Keypaste.Browser/Keypaste.Browser.csproj\"";

    [Fact]
    public void Only_Sodium_cs_imports_libsodium() =>
        Assert.Equal(["src/Keypaste.Browser/Sodium.cs"], Naming(Files("src", "*.cs"), _import));

    [Fact]
    public void Only_the_CLI_links_libsodium() =>
        Assert.Equal(["src/Keypaste.Cli/Keypaste.Cli.csproj"], Naming(Files("src", "*.csproj"), _link));

    [Fact]
    public void Only_the_CLI_these_tests_and_the_fake_browser_reference_Keypaste_Browser() =>
        Assert.Equal(
            ["src/Keypaste.Cli/Keypaste.Cli.csproj", "tests/Keypaste.Browser.Tests/Keypaste.Browser.Tests.csproj", "tests/Keypaste.FakeBrowser/Keypaste.FakeBrowser.csproj"],
            Naming([.. Files("src", "*.csproj"), .. Files("tests", "*.csproj")], _reference));

    [Fact]
    public void Another_import_link_or_reference_is_found()
    {
        Assert.Single(Naming([("src/Keypaste.Core/Internal/Sodium.cs", "[DllImport(\"libsodium\")]")], _import));
        Assert.Single(Naming([("src/Keypaste.App/Keypaste.App.csproj", "<DirectPInvoke Include=\"libsodium\" />")], _link));
        Assert.Single(Naming([("src/Keypaste.Mcp/Keypaste.Mcp.csproj", "<ProjectReference Include=\"../Keypaste.Browser/Keypaste.Browser.csproj\" />")], _reference));
    }

    private static List<string> Naming(IEnumerable<(string Path, string Text)> files, string marker) =>
        [.. files.Where(file => file.Text.Contains(marker, StringComparison.Ordinal)).Select(file => file.Path).Order(StringComparer.Ordinal)];

    private static List<(string Path, string Text)> Files(string folder, string pattern)
    {
        var root = RepoRoot();

        return [.. Directory.GetFiles(Path.Combine(root, folder), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => (Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path)))];
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new Xunit.Sdk.XunitException("keypaste.slnx not found above the test's base directory");
        }

        return directory;
    }
}
