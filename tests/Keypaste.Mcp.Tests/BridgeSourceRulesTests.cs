using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The bridge holds no vault: it cannot open, create or unlock one, and has nowhere to take a master
/// password or keyfile from (PRODUCT §2, THREATS.md T-8).
/// </summary>
/// <remarks>
/// The bridge references the core, which can do all of those, so the compiler does not hold this.
/// The mutation that must fail it: any of these calls written into the bridge, for a "fallback when no
/// owner is running" or a diagnostic.
/// </remarks>
public sealed class BridgeSourceRulesTests
{
    private static readonly string[] _vaultAccess =
    [
        "Vault.Open",
        "Vault.Create",
        "VaultCreation",
        "VaultKeyfile",
        "VaultCredentialSource",
        "VaultEntryNameLister",
        "SecretInput",
        "KeePassInterop",
    ];

    [Fact]
    public void The_bridge_never_opens_a_vault_or_reads_a_password()
    {
        var offenders = new List<string>();
        var bridge = Path.Combine(RepoRoot(), "src", "Keypaste.Mcp");

        foreach (var file in Directory.EnumerateFiles(bridge, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (var access in _vaultAccess)
            {
                if (text.Contains(access, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {access}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));

            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Could not locate keypaste.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
