using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>
/// The relay holds no vault and writes no audit line: it cannot open, create or unlock one, has nowhere to take a master
/// password or keyfile from, and records nothing (D-0548, D-0624). Its path starts in the CLI's <c>Program.cs</c>, which hands
/// a browser's start to it before any other CLI code runs, as it hands <c>keypaste mcp</c> to the bridge (D-0418).
/// </summary>
/// <remarks>
/// <c>Keypaste.Browser</c> references the core and <c>Program.cs</c> the whole CLI, which can do all of those, so the compiler
/// does not hold this. A text scan of the project's sources and <c>Program.cs</c>, in which a name in a comment counts too.
/// </remarks>
public sealed class RelaySourceRulesTests
{
    private const string _relayDispatch = "if (BrowserEntry.StartsRelay(args))";

    private static readonly string _dispatch = Path.Combine("src", "Keypaste.Cli", "Program.cs");

    private static readonly string[] _forbidden =
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
        "TotpSpec",
        "ReadTotp",
        "AuditLog",
        "RequestRecorder",
        "RequestRecords",
    ];

    [Fact]
    public void The_relay_never_opens_a_vault_reads_a_password_or_writes_an_audit_line()
    {
        var offenders = Sources().SelectMany(source => Offenders(source.Name, source.Text)).ToList();

        Assert.True(offenders.Count == 0, $"the relay's path names vault access or the audit log: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_reads_the_dispatch_and_the_whole_relay()
    {
        var names = Sources().Select(source => source.Name).ToList();

        Assert.Contains(_dispatch, names);
        Assert.Contains(Path.Combine("src", "Keypaste.Browser", "BrowserEntry.cs"), names);
        Assert.Contains(Path.Combine("src", "Keypaste.Browser", "Relay.cs"), names);
        Assert.True(names.Count >= 10, $"only {names.Count} files were scanned");
    }

    [Fact]
    public void A_vault_opened_before_the_dispatch_or_a_line_written_in_the_relay_is_found()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), _dispatch));
        var dispatch = program.IndexOf(_relayDispatch, StringComparison.Ordinal);
        var relay = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Browser", "Relay.cs"));

        Assert.True(dispatch > 0, "Program.cs no longer dispatches where this control inserts its mutation");
        Assert.Empty(Offenders(_dispatch, program));
        Assert.NotEmpty(Offenders(_dispatch, program[..dispatch] + "VaultLocator.TryResolve(args, out var vault);\n        " + program[dispatch..]));
        Assert.Empty(Offenders("Relay.cs", relay));
        Assert.NotEmpty(Offenders("Relay.cs", relay + "\n// AuditLog.TryOpen(path, clock, out var log, out _);"));
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        _forbidden.Where(marker => text.Contains(marker, StringComparison.Ordinal)).Select(marker => $"{name}: {marker}");

    private static IEnumerable<(string Name, string Text)> Sources()
    {
        var root = RepoRoot();

        return Directory.GetFiles(Path.Combine(root, "src", "Keypaste.Browser"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
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
