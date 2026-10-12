using System.Text;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A tool's command is found only in <c>PATH</c>'s absolute entries and, for the macOS app, its install folders, and is
/// started without a shell: an npm launcher on Windows as the program it names, and a Node script from an install folder
/// with a <c>node</c> found the same way (D-0436).
/// </summary>
public sealed class McpClientProgramTests : IDisposable
{
    private const string _command = "keypaste-probe-tool";

    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode _readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private static readonly string[] _awkward = ["a & b", "say \"hi\"", "%PATH%", "^caret", "!x!", "two words", @"trailing\", string.Empty];

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-client-program-").FullName;

    public static TheoryData<string, string> OtherLaunchers => new()
    {
        { _command + ".cmd", NpmCmdShimTests.NotNpms },
        { _command + ".cmd", NpmCmdShimTests.Pnpm },
        { _command + ".cmd", NpmCmdShimTests.Script(@"node_modules\probe\bin\probe.js").Replace("\"%_prog%\"  ", "\"%_prog%\" --no-warnings ", StringComparison.Ordinal) },
        { _command + ".cmd", NpmCmdShimTests.Script(@"node_modules\absent\bin\absent.js") },
        { _command + ".bat", NpmCmdShimTests.Script(@"node_modules\probe\bin\probe.js") },
    };

    private string Home => Path.Combine(_root, "home");

    private string LocalBin => Path.Combine(Home, ".local", "bin");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_tool_in_no_folder_searched_is_not_installed()
    {
        Assert.False(McpClientProgram.TryLocate(Tool(), Machine(Folder("empty")), out var program, out var problem));

        Assert.Null(program);
        Assert.Null(problem);
    }

    [Fact]
    public void A_tool_only_in_the_working_directory_or_a_relative_path_entry_is_not_installed()
    {
        var command = "keypaste-probe-" + Guid.NewGuid().ToString("N");
        var planted = Plant(Environment.CurrentDirectory, Exe(command));

        try
        {
            Assert.False(McpClientProgram.TryLocate(Tool(command), Machine("." + Path.PathSeparator + "bin"), out var program, out var problem));

            Assert.Null(program);
            Assert.Null(problem);
        }
        finally
        {
            File.Delete(planted);
        }
    }

    [Fact]
    public void The_first_absolute_path_entry_holding_the_tool_wins()
    {
        var first = Plant(Folder("first"), Exe(_command));
        Plant(Folder("second"), OperatingSystem.IsWindows() ? _command + ".cmd" : _command, NpmCmdShimTests.NotNpms);
        var path = string.Join(Path.PathSeparator, "relative", Path.GetDirectoryName(first), Path.Combine(_root, "second"));

        Assert.True(McpClientProgram.TryLocate(Tool(), Machine(path), out var program, out var problem));

        Assert.Equal(first, program.FileName);
        Assert.Empty(program.Prefix);
        Assert.Null(problem);
    }

    [Fact]
    public void A_file_without_an_execute_bit_is_not_installed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no execute bit");

        Plant(Folder("bin"), _command, executable: false);

        Assert.False(McpClientProgram.TryLocate(Tool(), Machine(Path.Combine(_root, "bin")), out _, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void The_runner_gets_the_resolved_program_and_every_argument_untouched_after_the_prefix()
    {
        var planted = Plant(Folder("bin"), Exe(_command));
        Assert.True(McpClientProgram.TryLocate(Tool(), Machine(Path.Combine(_root, "bin")), out var located, out _));
        var script = Path.Combine(_root, "probe.js");
        var throughNode = new McpClientProgram(Path.Combine(_root, "node"), [script]);
        var runner = new RecordingRunner();

        located.Run(runner, _awkward, TimeSpan.FromSeconds(1));
        throughNode.Run(runner, _awkward, TimeSpan.FromSeconds(1));

        Assert.Equal(planted, runner.Calls[0].FileName);
        Assert.Equal(_awkward, runner.Calls[0].Arguments);
        Assert.Equal(throughNode.FileName, runner.Calls[1].FileName);
        Assert.Equal([script, .. _awkward], runner.Calls[1].Arguments);
    }

    [Theory]
    [InlineData("npm", @"node_modules\probe\bin\probe.js")]
    [InlineData(@"project\node_modules\.bin", @"..\probe\bin\probe.js")]
    public void An_npm_script_launcher_starts_the_node_beside_it_with_the_script(string folder, string relative)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var bin = Folder(folder);
        Plant(bin, _command + ".cmd", NpmCmdShimTests.Script(relative));
        var script = PlantAt(Path.GetFullPath(Path.Combine(bin, relative)));
        var node = Plant(bin, "node.exe");

        Assert.True(McpClientProgram.TryLocate(Tool(), Machine(bin), out var program, out var problem), problem);

        Assert.Equal(node, program.FileName);
        Assert.Equal([script], program.Prefix);
    }

    [Fact]
    public void Without_node_beside_it_an_npm_script_launcher_starts_the_node_on_path()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var bin = Folder("npm");
        Plant(bin, _command + ".cmd", NpmCmdShimTests.Script(@"node_modules\probe\bin\probe.js"));
        var script = PlantAt(Path.Combine(bin, "node_modules", "probe", "bin", "probe.js"));
        var node = Plant(Folder("nodejs"), "node.exe");

        Assert.True(McpClientProgram.TryLocate(Tool(), Machine(bin + Path.PathSeparator + Path.GetDirectoryName(node)), out var program, out var problem), problem);

        Assert.Equal(node, program.FileName);
        Assert.Equal([script], program.Prefix);
    }

    [Fact]
    public void An_npm_script_launcher_with_no_node_is_found_but_not_started()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var bin = Folder("npm");
        var launcher = Plant(bin, _command + ".cmd", NpmCmdShimTests.Script(@"node_modules\probe\bin\probe.js"));
        PlantAt(Path.Combine(bin, "node_modules", "probe", "bin", "probe.js"));

        Assert.False(McpClientProgram.TryLocate(Tool(), Machine(bin), out var program, out var problem));

        Assert.Null(program);
        Assert.Equal($"found {launcher}, which keypaste cannot start without cmd.exe", problem, ignoreCase: true);
    }

    [Fact]
    public void Npms_launcher_for_a_program_starts_that_exe_with_no_prefix()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var bin = Folder("npm");
        Plant(bin, _command + ".cmd", NpmCmdShimTests.Direct(@"node_modules\probe\bin\probe.exe"));
        var exe = PlantAt(Path.Combine(bin, "node_modules", "probe", "bin", "probe.exe"));

        Assert.True(McpClientProgram.TryLocate(Tool(), Machine(bin), out var program, out var problem), problem);

        Assert.Equal(exe, program.FileName);
        Assert.Empty(program.Prefix);
    }

    [Theory]
    [MemberData(nameof(OtherLaunchers))]
    public void Any_other_batch_launcher_is_found_but_not_started_and_named(string name, string text)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "batch launchers are Windows'");

        var bin = Folder("bin");
        var launcher = Plant(bin, name, text);
        Plant(bin, "node.exe");
        PlantAt(Path.Combine(bin, "node_modules", "probe", "bin", "probe.js"));

        Assert.False(McpClientProgram.TryLocate(Tool(), Machine(bin), out var program, out var problem));

        Assert.Null(program);
        Assert.Equal($"found {launcher}, which keypaste cannot start without cmd.exe", problem, ignoreCase: true);
    }

    [Fact]
    public void The_search_is_paths_absolute_entries_then_each_tools_install_folders_once()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the install folders are macOS paths");

        var path = string.Join(Path.PathSeparator, "/usr/bin", "relative", "/opt/homebrew/bin/");
        var tools = McpClientCatalog.All.Where(client => client.InstallFolders.Count > 0).ToList();

        Assert.NotEmpty(tools);

        foreach (var client in tools)
        {
            string[] expected =
            [
                "/usr/bin",
                "/opt/homebrew/bin",
                .. client.InstallFolders
                    .Select(folder => folder.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Home, folder[2..]) : folder)
                    .Where(folder => folder != "/opt/homebrew/bin"),
            ];

            Assert.Equal(expected, McpClientProgram.SearchFolders(client, Machine(path, Home, installFolders: true)));
            Assert.Equal(["/usr/bin", "/opt/homebrew/bin"], McpClientProgram.SearchFolders(client, Machine(path, Home)));
        }
    }

    [Fact]
    public void A_tool_in_its_install_folder_is_found_after_path_and_a_copy_on_path_wins()
    {
        var claude = McpClientCatalog.Find("claude-code")!;
        var installed = Plant(LocalBin, Exe("claude"));

        Assert.False(McpClientProgram.TryLocate(claude, Machine(Folder("empty"), Home), out _, out _));
        Assert.True(McpClientProgram.TryLocate(claude, Machine(Path.Combine(_root, "empty"), Home, installFolders: true), out var program, out _));
        Assert.Equal(installed, program.FileName);
        Assert.Empty(program.Prefix);

        var onPath = Plant(Folder("bin"), Exe("claude"));

        Assert.True(McpClientProgram.TryLocate(claude, Machine(Path.Combine(_root, "bin"), Home, installFolders: true), out program, out _));
        Assert.Equal(onPath, program.FileName);
    }

    [Fact]
    public void A_node_script_in_an_install_folder_off_path_starts_with_the_node_beside_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var script = Plant(LocalBin, _command, "#!/usr/bin/env node\nconsole.log(1)\n");
        var beside = Plant(LocalBin, "node");
        Plant(Folder("path"), "node");

        Assert.True(McpClientProgram.TryLocate(InstalledTool(), Machine(Path.Combine(_root, "path"), Home, installFolders: true), out var program, out var problem), problem);

        Assert.Equal(beside, program.FileName);
        Assert.Equal([script], program.Prefix);
    }

    [Fact]
    public void Without_node_beside_it_a_node_script_starts_with_the_node_the_same_search_finds()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var script = Plant(LocalBin, _command, "#!/usr/bin/env node\n");
        var node = Plant(Folder("path"), "node");

        Assert.True(McpClientProgram.TryLocate(InstalledTool(), Machine(Path.Combine(_root, "path"), Home, installFolders: true), out var program, out var problem), problem);

        Assert.Equal(node, program.FileName);
        Assert.Equal([script], program.Prefix);
    }

    [Fact]
    public void A_node_script_with_no_node_to_start_it_is_found_but_not_started_and_named()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var script = Plant(LocalBin, _command, "#!/usr/bin/env node\n");

        Assert.False(McpClientProgram.TryLocate(InstalledTool(), Machine(Folder("path"), Home, installFolders: true), out var program, out var problem));

        Assert.Null(program);
        Assert.Equal($"found {script}, which needs Node, and keypaste found no node beside it or on PATH", problem);
    }

    [Fact]
    public void A_node_script_on_path_runs_as_a_terminal_would()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var script = Plant(Folder("path"), _command, "#!/usr/bin/env node\n");
        Plant(LocalBin, "node");

        Assert.True(McpClientProgram.TryLocate(InstalledTool(), Machine(Path.Combine(_root, "path"), Home, installFolders: true), out var program, out _));

        Assert.Equal(script, program.FileName);
        Assert.Empty(program.Prefix);
    }

    [Theory]
    [InlineData("#!/bin/sh\n")]
    [InlineData("#!/usr/bin/env node --no-warnings\n")]
    [InlineData("#!/usr/bin/env nodejs\n")]
    [InlineData("#!/usr/bin/env node\r\n")]
    [InlineData("#!/usr/bin/env")]
    public void Any_other_file_in_an_install_folder_runs_directly(string text)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "npm writes a .cmd launcher on Windows");

        var file = Plant(LocalBin, _command, text);
        Plant(LocalBin, "node");

        Assert.True(McpClientProgram.TryLocate(InstalledTool(), Machine(Folder("path"), Home, installFolders: true), out var program, out _));

        Assert.Equal(file, program.FileName);
        Assert.Empty(program.Prefix);
    }

    private static McpClient Tool(string command = _command) => new("probe", "Probe", McpWiring.OwnCli, command);

    private static McpClient InstalledTool() => Tool() with { InstallFolders = ["~/.local/bin"] };

    private static McpClientMachine Machine(string? path, string? home = null, bool installFolders = false)
    {
        string? Variable(string name) => name switch { "PATH" => path, "HOME" or "USERPROFILE" => home, _ => null };

        return McpClientMachine.From(Variable, McpClientMachine.CurrentPlatform) with { LooksInInstallFolders = installFolders };
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static string Plant(string folder, string name, string text = "#!/bin/sh\n", bool executable = true)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, executable ? _executable : _readable);
        }

        return path;
    }

    private static string PlantAt(string path) => Plant(Path.GetDirectoryName(path)!, Path.GetFileName(path));

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            Calls.Add((fileName, [.. arguments]));
            return new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
        }
    }
}
