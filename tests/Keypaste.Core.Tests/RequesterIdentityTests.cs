using System.IO.Pipes;
using System.Security.Cryptography;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A prompt says Verified only when the program on the owner's pipe is keypaste's own and the program that started it is
/// the client its label names, as the system says; anything else, the label included, is Not verified (THREATS.md T-3).
/// </summary>
public sealed class RequesterIdentityTests : IDisposable
{
    private const string _bridge = "/Applications/keypaste.app/Contents/MacOS/keypaste";
    private const string _claude = "/Users/maya/.local/share/claude/versions/2.0.14";
    private const string _terminal = "/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal";

    private readonly string _folder = Directory.CreateTempSubdirectory("keypaste-binaries-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static PeerLineage Lineage(bool keypaste, params string[] ancestors) => new(_bridge, keypaste, ancestors);

    [Fact]
    public void Keypaste_started_by_the_client_its_label_names_is_verified_and_names_its_app()
    {
        var identity = RequesterIdentity.Of(Lineage(true, _claude, "/bin/zsh", "/usr/bin/login", _terminal), "claude-code", "claude-code");

        Assert.Equal(("claude-code", "Terminal", true, "Verified"), (identity.Agent, identity.App, identity.Verified, identity.Verification));
        Assert.Equal("Checked by keypaste: the program asking is claude-code, run from Terminal.", identity.Explanation);
        Assert.Equal($"{_bridge}, started by {_claude}", identity.Process);
    }

    [Theory]
    [InlineData("another program on the pipe")]
    [InlineData("keypaste started by another program")]
    [InlineData("keypaste started by another client than the label names")]
    [InlineData("a label no client has")]
    [InlineData("no label")]
    [InlineData("no parent the system could read")]
    [InlineData("outside a connection")]
    public void Anything_else_is_not_verified_and_names_no_app(string why)
    {
        var (lineage, label) = why switch
        {
            "another program on the pipe" => (Lineage(false, _claude, _terminal), "claude-code"),
            "keypaste started by another program" => (Lineage(true, "/opt/homebrew/bin/node", _terminal), "claude-code"),
            "keypaste started by another client than the label names" => (Lineage(true, _claude, _terminal), "cursor"),
            "a label no client has" => (Lineage(true, _claude, _terminal), "work laptop"),
            "no label" => (Lineage(true, _claude, _terminal), (string?)null),
            "no parent the system could read" => (Lineage(true), "claude-code"),
            _ => ((PeerLineage?)null, "claude-code"),
        };

        var identity = RequesterIdentity.Of(lineage, label ?? "claude-code", label);

        Assert.False(identity.Verified, why);
        Assert.Null(identity.App);
        Assert.Equal("Not verified", identity.Verification);
        Assert.StartsWith("keypaste could not check that ", identity.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_bundle_a_client_ships_verifies_it_and_a_Windows_terminal_is_named_in_words()
    {
        var cursor = RequesterIdentity.Of(Lineage(true, "/Applications/Cursor.app/Contents/Frameworks/Cursor Helper (Plugin).app/Contents/MacOS/Cursor Helper (Plugin)", "/Applications/Cursor.app/Contents/MacOS/Cursor"), "cursor", "cursor");
        var windows = RequesterIdentity.Of(Lineage(true, @"C:\Users\maya\.local\bin\claude.exe", @"C:\Program Files\PowerShell\7\pwsh.exe", @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe", @"C:\Windows\explorer.exe"), "claude-code", "claude-code");

        Assert.Equal(("Cursor", true), (cursor.App, cursor.Verified));
        Assert.Equal(("Windows Terminal", true), (windows.App, windows.Verified));
    }

    /// <summary>
    /// npm's Claude Code and pnpm's Gemini CLI run under Node, linked from a bin folder: the script Node was given, its links
    /// followed, is inside the tool's own package, so the tool is verified; the same script name pointing elsewhere is not.
    /// </summary>
    [Theory]
    [InlineData("claude-code", "lib/node_modules/@anthropic-ai/claude-code/cli.js", "node", true)]
    [InlineData("gemini-cli", "pnpm/global/5/.pnpm/@google+gemini-cli@0.9.0/node_modules/@google/gemini-cli/dist/index.js", "node", true)]
    [InlineData("claude-code", ".bun/install/global/node_modules/@anthropic-ai/claude-code/cli.js", "bun", true)]
    [InlineData("claude-code", "lib/node_modules/@google/gemini-cli/dist/index.js", "node", false)]
    [InlineData("claude-code", "elsewhere/claude-code/cli.js", "node", false)]
    [InlineData("claude-code", "lib/node_modules/@anthropic-ai/claude-code/cli.js", "python3", false)]
    public void Node_running_the_tools_installed_package_is_verified_through_its_links(string label, string installed, string host, bool verified)
    {
        var script = Write(installed, [1]);
        var link = Path.Combine(_folder, "bin", "agent");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, script);
        var tree = new FakeTree(
            new ProcessFacts(40, _bridge, 30, Started: 100),
            new ProcessFacts(30, "/opt/homebrew/bin/" + host, 20, Started: 90, Arguments: [host, "--no-warnings", link, "mcp"]),
            new ProcessFacts(20, _terminal, 1, Started: 80));

        var identity = RequesterIdentity.Of(PeerLineage.Read(40, _bridge, tree, path => path == _bridge), label, label);

        Assert.Equal(verified, identity.Verified);
        Assert.Equal(verified ? "Terminal" : null, identity.App);
    }

    [Fact]
    public void Node_given_a_relative_script_is_not_verified()
    {
        var tree = new FakeTree(
            new ProcessFacts(40, _bridge, 30, Started: 100),
            new ProcessFacts(30, "/usr/bin/node", 20, Started: 90, Arguments: ["node", "node_modules/@anthropic-ai/claude-code/cli.js"]));

        var lineage = PeerLineage.Read(40, _bridge, tree, _ => true);

        Assert.Null(lineage.ParentScript);
        Assert.False(RequesterIdentity.Of(lineage, "claude-code", "claude-code").Verified);
    }

    [Fact]
    public void A_parent_that_started_after_its_child_ends_the_walk_as_a_reused_id()
    {
        var tree = new FakeTree(
            new ProcessFacts(40, _bridge, 30, Started: 100),
            new ProcessFacts(30, _claude, 20, Started: 90),
            new ProcessFacts(20, _terminal, 10, Started: 95),
            new ProcessFacts(10, "/sbin/launchd", 1, Started: 1));

        var lineage = PeerLineage.Read(40, _bridge, tree, path => path == _bridge);

        Assert.True(lineage.IsKeypaste);
        Assert.Equal([_claude], lineage.Ancestors);
        Assert.Same(PeerLineage.Unknown, PeerLineage.Read(null, _bridge, tree, _ => true));
    }

    [Fact]
    public void Keypaste_is_its_own_file_or_one_of_the_same_bytes_and_nothing_else()
    {
        var shipped = Write("shipped/keypaste", [1, 2, 3, 4]);
        var mounted = Write("mount/keypaste", [1, 2, 3, 4]);
        var other = Write("other/keypaste", [1, 2, 3, 5]);
        var binaries = new KeypasteBinaries([shipped, Path.Combine(_folder, "missing")]);

        Assert.True(binaries.Holds(shipped));
        Assert.True(binaries.Holds(mounted));
        Assert.False(binaries.Holds(other));
        Assert.False(binaries.Holds(Path.Combine(_folder, "nowhere")));
        Assert.False(new KeypasteBinaries([]).Holds(shipped));
    }

    /// <summary>The system's own reading of this test's process: its program, and a parent that started before it.</summary>
    [Fact]
    public void The_system_names_this_process_and_its_parent()
    {
        var self = SystemProcessTree.Instance.Of(Environment.ProcessId);

        Assert.NotNull(self);
        Assert.Equal(PathIdentity.Canonical(Environment.ProcessPath!), PathIdentity.Canonical(self.Path!), StringComparer.FromComparison(PathIdentity.Comparison));
        Assert.True(self.Parent > 0);
        Assert.NotEmpty(self.Arguments!);
        Assert.True(SystemProcessTree.Instance.Of(self.Parent!.Value) is { Started: { } parentStarted } && parentStarted <= self.Started);
        Assert.Null(SystemProcessTree.Instance.Of(-1));
    }

    /// <summary>On a real pipe the caller here is the test host, not keypaste, so its prompts say Not verified.</summary>
    [Fact]
    public async Task A_caller_on_the_pipe_that_is_not_keypaste_is_read_and_not_verified()
    {
        var name = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await Task.WhenAll(server.WaitForConnectionAsync(Token), client.ConnectAsync(Token));

        var (caller, lineage) = PipeCaller.Identify(server);

        Assert.Equal(Environment.ProcessId, caller.Pid);
        Assert.False(lineage.IsKeypaste);
        Assert.NotEmpty(lineage.Ancestors);
        Assert.False(RequesterIdentity.Of(lineage, "claude-code", "claude-code").Verified);
    }

    private string Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class FakeTree(params ProcessFacts[] processes) : IProcessTree
    {
        public ProcessFacts? Of(int pid) => processes.FirstOrDefault(process => process.Pid == pid);
    }
}
