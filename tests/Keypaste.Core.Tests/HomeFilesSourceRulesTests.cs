using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Core writes, moves and removes what it keeps under its home only through <c>Audit/HomeFiles.cs</c>, so no other file adds to what the home holds in the clear.</summary>
/// <remarks>
/// A write names its path at run time, so this rule is read from the source: every Core file that writes is the owner or one of
/// the writers of places outside the home, each named here with its place, and none of those names the home. Comments are left out.
/// </remarks>
public sealed partial class HomeFilesSourceRulesTests
{
    private const string _owner = "HomeFiles.cs";

    private static readonly Dictionary<string, string> _elsewhere = new(StringComparer.Ordinal)
    {
        ["VaultFiles.cs"] = "vaults and their backups",
        ["Vault.cs"] = "an attachment saved where the person chose",
        ["VaultKeyfile.cs"] = "a keyfile where the person chose",
        ["DotEnvFile.cs"] = "an exported .env",
        ["EnvReferenceFile.cs"] = "a project's reference file",
        ["SourceSnapshot.cs"] = "an imported export, removed once imported",
        ["ProcessTemporaryDirectory.cs"] = "the system's temporary folder",
        ["TerminalRequests.cs"] = "the system's temporary folder",
        ["McpConfigFile.cs"] = "an AI tool's configuration",
        ["BrowserHosts.cs"] = "a browser's native host manifest",
        ["LoginItem.cs"] = "the system's open-at-login entry",
        ["LinuxCliLink.cs"] = "the terminal's keypaste link",
    };

    [Fact]
    public void Only_the_owner_and_the_writers_of_other_places_write() =>
        Assert.Empty(Core().Where(source => source.Name != _owner && !_elsewhere.ContainsKey(source.Name)).SelectMany(Writes));

    [Fact]
    public void The_writers_of_other_places_name_no_home_path() =>
        Assert.Empty(Core().Where(source => _elsewhere.ContainsKey(source.Name) && Regex.IsMatch(Code(source.Text), @"\b(KeypasteHome|HomeFiles)\b")).Select(source => source.Name));

    [Fact]
    public void Each_rule_finds_its_mutation()
    {
        var core = Core();
        Assert.All(_elsewhere.Keys, name => Assert.NotEmpty(Writes(core.Single(source => source.Name == name))));
        Assert.NotEmpty(Writes(core.Single(source => source.Name == _owner)));

        var rules = core.Single(source => source.Name == "AgentRulesFile.cs");
        Assert.Empty(Writes(rules));

        string[] mutations =
        [
            "AtomicFile.Write(a, b);", "AtomicFile.Stage(a, b);", "File.Move(a, b);", "File.Delete(a);", "File.WriteAllText(a, b);",
            "File.AppendAllText(a, b);", "Directory.CreateDirectory(a);", "new FileStream(a, FileMode.OpenOrCreate);",
            "new FileStream(a, FileMode.Open, FileAccess.Write);",
        ];
        Assert.All(mutations, mutation => Assert.NotEmpty(Writes(rules with { Text = rules.Text + "\n" + mutation })));
        Assert.Empty(Writes(rules with { Text = rules.Text + "\n// File.Delete(a);" }));
    }

    [GeneratedRegex(@"\bAtomicFile\.(Write|Stage)\(|\bFile\.(Copy|Move|Delete|Replace|WriteAll\w*|AppendAll\w*|Create|OpenWrite|SetUnixFileMode)\(|\bDirectory\.(CreateDirectory|Delete|Move)\(|\bFileMode\.(Create|CreateNew|OpenOrCreate|Truncate|Append)\b|\bFileAccess\.(Write|ReadWrite)\b|\bFileOptions\.DeleteOnClose\b")]
    private static partial Regex RawWrite();

    private static IEnumerable<string> Writes(Source source) =>
        RawWrite().Matches(Code(source.Text)).Select(match => $"{source.Name}: {match.Value}");

    private static string Code(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static List<Source> Core()
    {
        var root = Path.Combine(VendoredWordList.RepoRoot(), "src", "Keypaste.Core");
        return [.. Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => new Source(Path.GetFileName(path), File.ReadAllText(path)))];
    }

    private sealed record Source(string Name, string Text);
}
