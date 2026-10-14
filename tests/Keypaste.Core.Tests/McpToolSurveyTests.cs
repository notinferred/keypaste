using System.Text;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Agents looking for every AI tool the catalogue connects on this computer: one finding per installed tool, its own
/// configuration read only when it can be, every tool looked for at once, and nothing written.
/// </summary>
/// <remarks>Every tool is planted under a temporary home and every command answered by a fake runner, so no test reaches a real tool.</remarks>
public sealed class McpToolSurveyTests : IDisposable
{
    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly TimeSpan _guard = TimeSpan.FromSeconds(60);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-survey-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Home => Path.Combine(_directory, "home");

    private string Bin => Path.Combine(_directory, "bin");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Each_installed_tool_has_one_finding_and_one_not_installed_has_none()
    {
        Plant("claude");
        Directory.CreateDirectory(Path.Combine(Home, ".cursor"));

        var findings = await McpToolSurvey.LookAsync(Machine(), new VersionRunner(), Token);

        Assert.Equal(["claude-code", "cursor"], findings.Select(finding => finding.Tool.Id));
        Assert.All(findings, finding => Assert.True(finding.Detection.Found));
    }

    [Fact]
    public async Task A_found_tool_says_whether_it_holds_keypaste_and_one_with_a_problem_is_not_read()
    {
        Plant("claude");
        Seed("claude-code", """{"mcpServers": {"keypaste": {"command": "/opt/keypaste/keypaste"}}}""");
        var cursor = Seed("cursor", """{"mcpServers": {"keypaste": {}""");
        Seed("vscode", "{}");
        Plant("codex");

        var findings = await McpToolSurvey.LookAsync(Machine(("VSCODE_PORTABLE", Path.Combine(_directory, "portable"))), new VersionRunner(), Token);
        var found = findings.ToDictionary(finding => finding.Tool.Id);

        Assert.Equal(new McpToolPresence(McpToolState.Connected, Reason: null), found["claude-code"].Presence);
        Assert.Equal(new McpToolPresence(McpToolState.NotConnected, Reason: null), found["codex"].Presence);

        Assert.Equal(McpToolState.CannotTell, found["cursor"].Presence.State);
        Assert.StartsWith(cursor, found["cursor"].Presence.Reason, StringComparison.Ordinal);

        Assert.Null(found["vscode"].Reading);
        Assert.Equal(
            new McpToolPresence(McpToolState.Problem, "VSCODE_PORTABLE is set, so VS Code may keep its configuration where keypaste does not look"),
            found["vscode"].Presence);
    }

    /// <summary>Each probe waits until every probe has started, so a survey that looked one tool at a time would find none.</summary>
    [Fact]
    public async Task Every_tool_is_looked_for_at_once()
    {
        var commands = McpClientCatalog.For(McpClientMachine.CurrentPlatform).Select(tool => tool.Executable).OfType<string>().ToList();

        foreach (var command in commands)
        {
            Plant(command);
        }

        using var started = new CountdownEvent(commands.Count);

        var findings = await McpToolSurvey.LookAsync(
            Machine(),
            new VersionRunner(() =>
            {
                started.Signal();
                return started.Wait(_guard, Token);
            }),
            Token);

        Assert.Equal(commands.Count, findings.Count);
        Assert.All(findings, finding => Assert.NotNull(finding.Detection.Program));
    }

    [Fact]
    public async Task Cancelling_returns_while_a_probe_is_still_running()
    {
        Plant("claude");
        using var release = new ManualResetEventSlim();
        var probing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var looking = McpToolSurvey.LookAsync(
            Machine(),
            new VersionRunner(() =>
            {
                probing.TrySetResult();
                var released = release.Wait(_guard, CancellationToken.None);
                probed.TrySetResult();
                return released;
            }),
            cancel.Token);

        await probing.Task.WaitAsync(_guard, Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => looking);
        Assert.False(probed.Task.IsCompleted);

        release.Set();
        await probed.Task.WaitAsync(_guard, Token);
    }

    [Fact]
    public void A_probe_that_ends_after_the_cancel_reads_no_configuration()
    {
        Plant("claude");
        var reads = 0;
        using var cancel = new CancellationTokenSource();

        var finding = McpToolSurvey.Look(
            McpClientCatalog.Find("claude-code")!,
            Machine(),
            new VersionRunner(() =>
            {
                cancel.Cancel();
                return true;
            }),
            (_, _) =>
            {
                reads++;
                return new McpEntryReading(McpEntryPresence.Absent, Path: null, Reason: null);
            },
            cancel.Token);

        Assert.Null(finding);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task Looking_writes_nothing()
    {
        Plant("claude");
        Plant("codex");
        Seed("claude-code", """{"oauthAccount": {"token": "kept"}, "mcpServers": {"keypaste": {"command": "k"}}}""");
        Seed("codex", "[mcp_servers.keypaste]\ncommand = \"k\"\n");
        Seed("cursor", """{"mcpServers": {}}""");
        var before = Snapshot();

        var findings = await McpToolSurvey.LookAsync(Machine(), new VersionRunner(), Token);

        Assert.Equal(["claude-code", "codex", "cursor"], findings.Select(finding => finding.Tool.Id));
        Assert.Equal(before, Snapshot());
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private string Plant(string command)
    {
        Directory.CreateDirectory(Bin);
        var path = Path.Combine(Bin, Exe(command));
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, _executable);
        }

        return path;
    }

    private string Seed(string id, string text)
    {
        var path = McpClientCatalog.Find(id)!.Configuration!.PathOn(Machine())!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private List<string> Snapshot() =>
    [
        .. Directory.EnumerateFileSystemEntries(_directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => File.Exists(path)
                ? $"{path} {File.GetLastWriteTimeUtc(path).Ticks} {Convert.ToHexString(File.ReadAllBytes(path))}"
                : path),
    ];

    private McpClientMachine Machine(params (string Name, string Value)[] set)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = Home,
            ["USERPROFILE"] = Home,
            ["APPDATA"] = Path.Combine(Home, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(Home, "AppData", "Local"),
            ["PATH"] = Bin,
        };

        foreach (var (name, value) in set)
        {
            variables[name] = value;
        }

        return McpClientMachine.From(name => variables.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);
    }

    /// <summary>Answers each tool's <c>--version</c>, found when <paramref name="starts"/> says so, and refuses any other command.</summary>
    private sealed class VersionRunner(Func<bool>? starts = null) : IProcessRunner
    {
        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout) =>
            arguments is ["--version"]
                ? new ProcessResult(ToolFound: starts?.Invoke() ?? true, ExitCode: 0, "1.0.0", string.Empty)
                : throw new InvalidOperationException($"looking for tools started {fileName} {string.Join(' ', arguments)}");
    }
}
