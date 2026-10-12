using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// On Windows a tool behind npm's launcher is started with the runner's real <c>node.exe</c> and receives every argument
/// byte for byte, because none passes through <c>cmd.exe</c> (D-0436).
/// </summary>
public sealed class NpmShimLaunchTests : IDisposable
{
    private const string _command = "keypaste-probe-tool";

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-npm-launch-").FullName;

    private static McpClient Tool => new("probe", "Probe", McpWiring.OwnCli, _command);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_script_behind_npms_launcher_gets_every_argument_as_it_was_given()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var node = RunnerNode.Find();
        var bin = Path.Combine(_root, "npm");
        var script = Path.Combine(bin, "node_modules", "probe", "bin", "probe.js");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, RunnerNode.Printer);
        File.WriteAllText(Path.Combine(bin, _command + ".cmd"), NpmCmdShimTests.Script(@"node_modules\probe\bin\probe.js"));
        var machine = Machine(bin + Path.PathSeparator + Path.GetDirectoryName(node));

        Assert.True(McpClientProgram.TryLocate(Tool, machine, out var program, out var problem), problem);
        Assert.Equal(node, program.FileName, ignoreCase: true);
        Assert.Equal([script], program.Prefix);

        Assert.Equal(RunnerNode.Awkward, RunnerNode.Printed(program.Run(new SystemProcessRunner(), RunnerNode.Awkward, RunnerNode.Timeout)));
    }

    [Fact]
    public void A_program_behind_npms_launcher_is_started_directly_and_gets_every_argument_as_it_was_given()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var node = RunnerNode.Find();
        var bin = Path.Combine(_root, "npm");
        var copy = Path.Combine(bin, "node_modules", "probe", "bin", "probe.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(node, copy);
        File.WriteAllText(Path.Combine(bin, _command + ".cmd"), NpmCmdShimTests.Direct(@"node_modules\probe\bin\probe.exe"));

        Assert.True(McpClientProgram.TryLocate(Tool, Machine(bin), out var program, out var problem), problem);
        Assert.Equal(copy, program.FileName);
        Assert.Empty(program.Prefix);

        var printed = program.Run(
            new SystemProcessRunner(),
            ["-e", "process.stdout.write(JSON.stringify(process.argv.slice(1)))", .. RunnerNode.Awkward],
            RunnerNode.Timeout);

        Assert.Equal(RunnerNode.Awkward, RunnerNode.Printed(printed));
    }

    private static McpClientMachine Machine(string path)
    {
        string? Variable(string name) => name == "PATH" ? path : null;

        return McpClientMachine.From(Variable, McpClientPlatform.Windows);
    }
}
