using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>Only <c>Keypaste.Browser</c>'s <c>Sodium.cs</c> calls libsodium, so Keypaste.Core, the bridge and the app carry none of it (D-0611).</summary>
/// <remarks>
/// <c>Directory.Build.targets</c> holds who links libsodium and who references <c>Keypaste.Browser</c>. A <c>DllImport</c>'s
/// library is a string the compiler does not check, so the import is read from the source, in which a name in a comment counts too.
/// </remarks>
public sealed class SodiumSourceRulesTests
{
    private const string _import = "\"libsodium\"";

    [Fact]
    public void Only_Sodium_cs_imports_libsodium() =>
        Assert.Equal(["src/Keypaste.Browser/Sodium.cs"], Naming(Files("src", "*.cs"), _import));

    [Fact]
    public void Another_import_is_found() =>
        Assert.Single(Naming([("src/Keypaste.Core/Internal/Sodium.cs", "[DllImport(\"libsodium\")]")], _import));

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
