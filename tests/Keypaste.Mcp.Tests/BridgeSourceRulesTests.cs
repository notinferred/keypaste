using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The bridge holds no vault: it cannot open, create or unlock one, and has nowhere to take a master
/// password or keyfile from (PRODUCT §2, THREATS.md T-8). Its path starts in the CLI's <c>Program.cs</c>,
/// which hands <c>keypaste mcp</c> to it before any other CLI code runs (D-0418).
/// </summary>
/// <remarks>
/// The bridge references the core and <c>Program.cs</c> the whole CLI, which can do all of those, so the
/// compiler does not hold this. A text scan of the bridge's sources and <c>Program.cs</c>, in which a name
/// in a comment counts too.
/// </remarks>
public sealed class BridgeSourceRulesTests
{
    private static readonly string _dispatch = Path.Combine("src", "Keypaste.Cli", "Program.cs");

    private static readonly string[] _vaultAccess =
    [
        "Vault.Open",
        "Vault.Create",
        "VaultCreation",
        "VaultKeyfile",
        "VaultCredentialSource",
        "VaultEntryNameLister",
        "VaultLocator",
        "VaultSession",
        "SecretInput",
        "KeePassInterop",
    ];

    [Fact]
    public void The_bridge_never_opens_a_vault_or_reads_a_password()
    {
        var offenders = Sources().SelectMany(source => Offenders(source.Name, source.Text)).ToList();

        Assert.True(offenders.Count == 0, $"the bridge's path names vault access: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_reads_the_dispatch_and_the_whole_bridge()
    {
        var names = Sources().Select(source => source.Name).ToList();

        Assert.Contains(_dispatch, names);
        Assert.Contains(Path.Combine("src", "Keypaste.Mcp", "BridgeEntry.cs"), names);
        Assert.True(names.Count >= 10, $"only {names.Count} files were scanned");
    }

    [Fact]
    public void A_vault_opened_before_the_dispatch_is_found()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), _dispatch));
        var dispatch = program.IndexOf("if (StartsBridge(args))", StringComparison.Ordinal);

        Assert.True(dispatch > 0, "Program.cs no longer dispatches where this control inserts its mutation");
        Assert.Empty(Offenders(_dispatch, program));
        Assert.NotEmpty(Offenders(_dispatch, program[..dispatch] + "VaultLocator.TryResolve(args, out var vault);\n        " + program[dispatch..]));
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        _vaultAccess.Where(access => text.Contains(access, StringComparison.Ordinal)).Select(access => $"{name}: {access}");

    private static IEnumerable<(string Name, string Text)> Sources()
    {
        var root = RepoRoot();

        return Directory.GetFiles(Path.Combine(root, "src", "Keypaste.Mcp"), "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(root, _dispatch))
            .Select(path => (Path.GetRelativePath(root, path), File.ReadAllText(path)));
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
