using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Core writes, copies, renames and removes vault files and their backups only through <c>Internal/VaultFiles.cs</c>, and replaces a vault only through KeePassLib's file transaction in <c>KeePassInterop.cs</c>.</summary>
/// <remarks>
/// The front ends carry <c>BannedSymbols.NoFileWrites.txt</c>. Core also writes settings, audit lines and other files that are no
/// vault, and a ban list covers a whole project and cannot tell one path from another, so this rule is read from the source of
/// the files that handle vault paths: every <c>Vault*.cs</c> at Core's top level, <c>KeePassInterop.cs</c> and the import folder. Comments are left out.
/// </remarks>
public sealed partial class VaultFilesSourceRulesTests
{
    private const string _owner = "VaultFiles.cs";
    private const string _transaction = "FileTransactionEx";

    [Fact]
    public void The_files_that_handle_vault_paths_write_nothing_themselves() =>
        Assert.Empty(VaultHandling().SelectMany(Writes));

    [Fact]
    public void Only_the_save_opens_a_KeePassLib_file_transaction() =>
        Assert.Equal(["KeePassInterop.cs"], [.. Core().Where(source => Code(source.Text).Contains(_transaction, StringComparison.Ordinal)).Select(source => source.Name)]);

    [Fact]
    public void Each_rule_finds_its_mutation()
    {
        var handling = VaultHandling();
        Assert.Contains(handling, source => source.Name == "VaultBackups.cs");
        Assert.Contains(handling, source => source.Name == "KeePassInterop.cs");
        Assert.DoesNotContain(handling, source => source.Name == _owner);
        Assert.NotEmpty(Writes(Core().Single(source => source.Name == _owner)));

        var backups = handling.Single(source => source.Name == "VaultBackups.cs");
        string[] mutations =
        [
            "File.Copy(a, b);", "File.Move(a, b);", "File.Delete(a);", "File.Replace(a, b, null);", "File.WriteAllBytes(a, b);",
            "File.SetUnixFileMode(a, m);", "Directory.CreateDirectory(a);", "new FileStream(a, FileMode.Create);",
            "new FileStream(a, FileMode.Open, FileAccess.ReadWrite);", "FileOptions.DeleteOnClose",
        ];
        Assert.All(mutations, mutation => Assert.NotEmpty(Writes(backups with { Text = backups.Text + "\n" + mutation })));
        Assert.Empty(Writes(backups with { Text = backups.Text + "\n// File.Delete(a);" }));

        var vault = handling.Single(source => source.Name == "Vault.cs");
        Assert.Equal(["KeePassInterop.cs", "Vault.cs"], [.. new[] { vault with { Text = vault.Text + "\n" + _transaction } }.Concat(Core()).Where(source => Code(source.Text).Contains(_transaction, StringComparison.Ordinal)).Select(source => source.Name).Order(StringComparer.Ordinal)]);
    }

    [GeneratedRegex(@"\bFile\.(Copy|Move|Delete|Replace|WriteAll\w*|AppendAll\w*|Create|OpenWrite|SetUnixFileMode)\(|\bDirectory\.(CreateDirectory|Delete|Move)\(|\bFileMode\.(Create|CreateNew|OpenOrCreate|Truncate|Append)\b|\bFileAccess\.(Write|ReadWrite)\b|\bFileOptions\.DeleteOnClose\b")]
    private static partial Regex RawWrite();

    private static IEnumerable<string> Writes(Source source) =>
        RawWrite().Matches(Code(source.Text)).Select(match => $"{source.Name}: {match.Value}");

    private static string Code(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static List<Source> VaultHandling() =>
        [.. Core().Where(source => source.Name != _owner && ((source.Folder == "Keypaste.Core" && source.Name.StartsWith("Vault", StringComparison.Ordinal)) || source.Name == "KeePassInterop.cs" || source.Folder == "Import"))];

    private static List<Source> Core()
    {
        var root = Path.Combine(VendoredWordList.RepoRoot(), "src", "Keypaste.Core");
        return [.. Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => new Source(Path.GetFileName(path), Path.GetFileName(Path.GetDirectoryName(path)!), File.ReadAllText(path)))];
    }

    private sealed record Source(string Name, string Folder, string Text);
}
