using System.Text;
using Keypaste.App.ViewModels;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// A computer of a test's own for Agents to look for AI tools on: a home and a PATH under a folder, the
/// tools planted there, and a runner that answers their commands, so no real tool is started and no real
/// configuration is read or written.
/// </summary>
internal sealed class FakeTools
{
    internal const string Holding = """{"mcpServers": {"keypaste": {"command": "k"}}}""";

    private const string _empty = """{"mcpServers": {}}""";

    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly Dictionary<string, string> _variables;

    internal FakeTools(string root)
    {
        Home = Directory.CreateDirectory(Path.Combine(root, "user")).FullName;
        Bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
        _variables = new(StringComparer.Ordinal)
        {
            ["HOME"] = Home,
            ["USERPROFILE"] = Home,
            ["APPDATA"] = Path.Combine(Home, "AppData", "Roaming"),
            ["PATH"] = Bin,
        };
    }

    internal string Home { get; }

    internal string Bin { get; }

    internal ToolRunner Runner { get; } = new();

    internal McpClientMachine Machine => McpClientMachine.From(name => _variables.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);

    /// <summary>Claude Code installed, holding keypaste or not.</summary>
    internal FakeTools ClaudeCode(bool connected)
    {
        Plant("claude");
        Seed(".claude.json", connected ? Holding : _empty);
        return this;
    }

    /// <summary>Cursor's folder, its file holding keypaste or not.</summary>
    internal FakeTools Cursor(bool connected)
    {
        Seed(Path.Combine(".cursor", "mcp.json"), connected ? Holding : _empty);
        return this;
    }

    /// <summary>Gemini CLI installed, with a settings file keypaste cannot read.</summary>
    internal FakeTools GeminiUnreadable()
    {
        Plant("gemini");
        Seed(Path.Combine(".gemini", "settings.json"), "{ not json");
        return this;
    }

    /// <summary>VS Code's folder, with its configuration moved where keypaste does not look, so it cannot be connected.</summary>
    internal FakeTools VsCodeMoved()
    {
        foreach (var folder in new[] { Path.Combine("AppData", "Roaming", "Code", "User"), Path.Combine("Library", "Application Support", "Code", "User"), Path.Combine(".config", "Code", "User") })
        {
            Directory.CreateDirectory(Path.Combine(Home, folder));
        }

        _variables["VSCODE_APPDATA"] = Path.Combine(Home, "elsewhere");
        return this;
    }

    /// <summary>What Agents finds and connects tools through on this computer, with the bridge under its folder.</summary>
    /// <param name="check">Starts a registration's bridge for Check the connection; none starts nothing.</param>
    internal ClientConnector Connector(Func<McpServerRegistration, McpConnectionCheck?>? check = null) => new(
        Runner,
        Machine,
        ApproverOverride: null,
        () => new McpBridgeTargetResult(new McpBridgeTarget(new McpServerCommand(Path.Combine(Bin, "keypaste"), [McpServerLocator.BridgeArgument]), Link: null, Note: null), Error: null),
        check ?? (_ => null));

    internal string Plant(string command)
    {
        var path = Path.Combine(Bin, OperatingSystem.IsWindows() ? command + ".exe" : command);
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, _executable);
        }

        return path;
    }

    internal string Seed(string relative, string text)
    {
        var path = Path.Combine(Home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Answers every command as its tool would, and records each by its bare command as a plan shows it.</summary>
    internal sealed class ToolRunner : IProcessRunner
    {
        private readonly List<string> _calls = [];

        /// <summary>Holds each <c>--version</c> until it is set, so a test sees a look still running.</summary>
        internal ManualResetEventSlim? Hold { get; set; }

        /// <summary>Holds every other command until it is set, so a test sees a plan still running.</summary>
        internal ManualResetEventSlim? HoldChanges { get; set; }

        internal IReadOnlyList<string> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        internal IReadOnlyList<string> Changes => [.. Calls.Where(call => !call.EndsWith(" --version", StringComparison.Ordinal))];

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            lock (_calls)
            {
                _calls.Add(new McpClientCommand(new McpClientProgram(fileName, []), Path.GetFileNameWithoutExtension(fileName), arguments, MayFail: false).Display);
            }

            (arguments is ["--version"] ? Hold : HoldChanges)?.Wait(timeout);

            return new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
        }
    }
}
