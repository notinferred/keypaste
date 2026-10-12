using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A <c>#!/usr/bin/env node</c> tool in an install folder the macOS app searches off <c>PATH</c> is started with the
/// runner's real <c>node</c> beside it and receives every argument byte for byte (D-0436).
/// </summary>
public sealed class NodeScriptLaunchTests : IDisposable
{
    private const string _command = "keypaste-probe-tool";

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-node-launch-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_node_script_in_an_install_folder_gets_every_argument_as_it_was_given()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("npm writes a .cmd launcher on Windows, which NpmShimLaunchTests starts");
            return;
        }

        var node = RunnerNode.Find();
        var home = Path.Combine(_root, "home");
        var bin = Path.Combine(home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var tool = Path.Combine(bin, _command);
        File.WriteAllText(tool, RunnerNode.Printer);
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var linked = Path.Combine(bin, "node");
        File.CreateSymbolicLink(linked, node);

        Assert.True(McpClientProgram.TryLocate(Tool(), Machine(home), out var program, out var problem), problem);
        Assert.Equal(linked, program.FileName);
        Assert.Equal([tool], program.Prefix);

        Assert.Equal(RunnerNode.Awkward, RunnerNode.Printed(program.Run(new SystemProcessRunner(), RunnerNode.Awkward, RunnerNode.Timeout)));
    }

    private static McpClient Tool() => new("probe", "Probe", McpWiring.OwnCli, _command) { InstallFolders = ["~/.local/bin"] };

    private static McpClientMachine Machine(string home)
    {
        string? Variable(string name) => name switch { "HOME" => home, "PATH" => "/usr/bin:/bin", _ => null };

        return McpClientMachine.From(Variable, McpClientMachine.CurrentPlatform) with { LooksInInstallFolders = true };
    }
}
