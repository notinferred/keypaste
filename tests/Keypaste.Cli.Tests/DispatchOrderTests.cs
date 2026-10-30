using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>Program.cs</c> hands <c>keypaste mcp</c> to the bridge and a browser's start to the relay before any other CLI code runs,
/// so neither opens a vault, reads a password or writes an audit line first (D-0418, D-0548).
/// </summary>
/// <remarks>
/// The bridge's and the relay's <c>BannedSymbols</c> lists hold what their own code names. Which statement runs first is an
/// order, not a name, so it is read from the source.
/// </remarks>
public sealed class DispatchOrderTests
{
    private static readonly string[] _dispatches = ["if (StartsBridge(args))", "if (BrowserEntry.StartsRelay(args))"];

    [Fact]
    public void Main_dispatches_the_bridge_and_the_relay_before_anything_else() =>
        Assert.Equal(_dispatches, LeadingDispatches(MainBody()));

    [Fact]
    public void A_statement_before_a_dispatch_is_found()
    {
        var main = MainBody();

        Assert.NotEqual(_dispatches, LeadingDispatches("VaultLocator.TryResolve(args, out var vault);\n" + main));
        Assert.NotEqual(_dispatches, LeadingDispatches(main.Replace(_dispatches[1], "AuditLog.TryOpen(path, clock, out var log, out _);\n" + _dispatches[1], StringComparison.Ordinal)));
    }

    /// <summary>The dispatches <paramref name="body"/> starts with, in order, each block skipped to reach the next.</summary>
    private static List<string> LeadingDispatches(string body)
    {
        List<string> found = [];
        var rest = body.TrimStart();

        foreach (var dispatch in _dispatches)
        {
            if (!rest.StartsWith(dispatch, StringComparison.Ordinal))
            {
                break;
            }

            found.Add(dispatch);
            rest = rest[(rest.IndexOf('}', rest.IndexOf('{', StringComparison.Ordinal)) + 1)..].TrimStart();
        }

        return found;
    }

    private static string MainBody()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Cli", "Program.cs"));
        var main = program.IndexOf("Main(string[] args)", StringComparison.Ordinal);

        Assert.True(main > 0, "Program.cs no longer declares Main(string[] args)");
        return program[(program.IndexOf('{', main) + 1)..];
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
