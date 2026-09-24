using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What connecting a client composes and runs, shared by <c>keypaste setup</c> and the desktop.
/// </summary>
/// <remarks>
/// Every case runs against a recording runner, so no test reaches a real client's configuration.
/// </remarks>
public sealed class McpClientSetupTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-setup-").FullName;

    private static McpClient Claude => McpClientCatalog.Find("claude-code")!;

    private static McpClient Cursor => McpClientCatalog.Find("cursor")!;

    private McpServerRegistration Registration(
        string label = "claude-code",
        IReadOnlyList<string>? expose = null,
        McpServerCommand? server = null)
    {
        Assert.True(McpServerRegistration.TryCreate(
            server ?? new McpServerCommand(Path.Combine(_directory, "keypaste-mcp"), []),
            Path.Combine(_directory, "vault.kdbx"),
            label,
            expose ?? [],
            out var registration,
            out var error), error);

        return registration;
    }

    [Fact]
    public void Running_a_plan_runs_exactly_the_commands_it_shows()
    {
        var runner = new RecordingRunner("claude");
        var plan = McpClientSetup.Connect(Claude, Registration(expose: ["env/**", "servers/staging/*"]));

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, runner).Status);

        Assert.Equal(plan.Commands.Select(c => c.Display), runner.Calls.Select(c => c.Display));
        Assert.Equal(plan.Display, string.Join('\n', runner.Calls.Select(c => c.Display)));
    }

    [Fact]
    public void An_earlier_entry_is_cleared_before_the_add_and_its_absence_is_not_a_refusal()
    {
        var runner = new RecordingRunner("claude") { RefuseFirst = "No MCP server found with name: keypaste" };
        var plan = McpClientSetup.Connect(Claude, Registration());

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, runner).Status);

        Assert.Equal(["mcp", "remove", "--scope", "user", "keypaste"], runner.Calls[0].Arguments);
        Assert.Equal(["mcp", "add", "--scope", "user", "--transport", "stdio", "keypaste", "--"], runner.Calls[1].Arguments.Take(8));
    }

    [Fact]
    public void A_refused_add_reports_the_clients_own_first_line()
    {
        var runner = new RecordingRunner("claude") { RefuseAll = "that scope is not writable here\nmore detail" };

        var result = McpClientSetup.Apply(McpClientSetup.Connect(Claude, Registration()), runner);

        Assert.Equal(McpSetupStatus.Refused, result.Status);
        Assert.Equal("that scope is not writable here", result.ClientSaid);
    }

    [Fact]
    public void A_client_that_cannot_be_started_is_not_installed_and_nothing_is_claimed()
    {
        var runner = new RecordingRunner();

        Assert.False(McpClientSetup.IsInstalled(Claude, runner));
        Assert.Equal(McpSetupStatus.NotInstalled, McpClientSetup.Apply(McpClientSetup.Connect(Claude, Registration()), runner).Status);
    }

    [Fact]
    public void A_client_configured_by_file_gets_a_block_to_paste_and_nothing_runs()
    {
        var runner = new RecordingRunner("cursor");
        var plan = McpClientSetup.Connect(Cursor, Registration(label: "cursor"));

        Assert.False(plan.RunsCommands);
        Assert.Equal(McpSetupStatus.ByHand, McpClientSetup.Apply(plan, runner).Status);
        Assert.Empty(runner.Calls);
        Assert.False(McpClientSetup.IsInstalled(Cursor, runner));

        Assert.Equal("\"keypaste\": {", plan.PasteBlock![0]);
        Assert.Contains("\"--client-label\", \"cursor\"", plan.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_runs_only_the_clients_own_removal()
    {
        var runner = new RecordingRunner("claude");
        var plan = McpClientSetup.Remove(Claude);

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, runner).Status);
        Assert.Equal("claude mcp remove --scope user keypaste", Assert.Single(runner.Calls).Display);
        Assert.False(McpClientSetup.Remove(Cursor).RunsCommands);
    }

    [Fact]
    public void A_refused_removal_is_a_refusal_rather_than_success()
    {
        var runner = new RecordingRunner("claude") { RefuseAll = "No MCP server found with name: keypaste" };

        Assert.Equal(McpSetupStatus.Refused, McpClientSetup.Apply(McpClientSetup.Remove(Claude), runner).Status);
    }

    /// <summary>
    /// An AppImage is started as the image file with <c>mcp</c>, which must come before any of the
    /// bridge's flags or <c>AppRun</c> could not see it.
    /// </summary>
    [Fact]
    public void An_appimage_is_told_to_start_the_bridge_before_any_of_its_flags()
    {
        var image = Path.Combine(_directory, "keypaste.AppImage");
        var line = Registration(server: new McpServerCommand(image, [McpServerLocator.AppImageArgument])).CommandLine();

        Assert.Equal([image, "mcp", "--vault"], line.Take(3));
    }

    [Fact]
    public void The_server_argv_follows_a_double_dash_so_a_vault_path_can_never_be_read_as_a_flag()
    {
        var add = McpClientSetup.Connect(Claude, Registration()).Commands[1].Arguments;

        Assert.Equal(Path.Combine(_directory, "keypaste-mcp"), add[add.ToList().IndexOf("--") + 1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded ")]
    [InlineData("two\nlines")]
    [InlineData("zero\u200Bwidth")]
    public void A_label_the_audit_log_would_rewrite_is_refused(string label)
    {
        Assert.False(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste-mcp", []), "vault.kdbx", label, [], out _, out var error));
        Assert.Contains("label", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exposure_the_bridge_would_refuse_is_refused_before_anything_is_written()
    {
        Assert.False(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste-mcp", []), "vault.kdbx", "claude-code", ["env/**", " "], out _, out var error));
        Assert.StartsWith("exposure: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_vault_path_is_made_absolute()
    {
        Assert.True(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste-mcp", []), "vault.kdbx", "codex", [], out var registration, out _));
        Assert.Equal(Path.GetFullPath("vault.kdbx"), registration.VaultPath);
    }

    /// <summary>
    /// The only things a client is ever told: where the bridge is, which vault, what to call it
    /// and what it may name. A keyfile, a password or a session has nowhere to go.
    /// </summary>
    [Fact]
    public void A_plan_holds_only_the_bridge_the_vault_the_label_and_the_exposure()
    {
        var registration = Registration(expose: ["env/**"]);
        var add = McpClientSetup.Connect(Claude, registration).Commands[1].Arguments;

        Assert.Equal(
            ["mcp", "add", "--scope", "user", "--transport", "stdio", "keypaste", "--",
             registration.Server.Path, "--vault", registration.VaultPath, "--client-label", "claude-code", "--expose", "env/**"],
            add);
    }

    [Fact]
    public void A_displayed_argument_with_a_space_is_quoted()
    {
        var command = new McpClientCommand("claude", ["mcp", "add", "C:\\Program Files\\keypaste-mcp.exe"], MayFail: false);

        Assert.Equal("claude mcp add \"C:\\Program Files\\keypaste-mcp.exe\"", command.Display);
    }

    [Fact]
    public void The_bridge_is_found_beside_the_program_before_path()
    {
        var beside = Directory.CreateDirectory(Path.Combine(_directory, "beside")).FullName;
        var onPath = Directory.CreateDirectory(Path.Combine(_directory, "path")).FullName;
        File.WriteAllText(Path.Combine(onPath, McpServerLocator.ExecutableName), "");

        Assert.Equal(Path.Combine(onPath, McpServerLocator.ExecutableName), McpServerLocator.Find(beside, onPath)!.Path);

        File.WriteAllText(Path.Combine(beside, McpServerLocator.ExecutableName), "");
        Assert.Equal(Path.Combine(beside, McpServerLocator.ExecutableName), McpServerLocator.Find(beside, onPath)!.Path);
        Assert.Null(McpServerLocator.Find(Path.Combine(_directory, "nowhere"), null));
    }

    [Fact]
    public void Inside_its_appimage_the_desktop_registers_the_image_file_rather_than_its_mount()
    {
        var mount = Directory.CreateDirectory(Path.Combine(_directory, ".mount_keypaste")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(mount, "usr", "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, McpServerLocator.ExecutableName), "");
        var image = Path.Combine(_directory, "keypaste-app.AppImage");
        File.WriteAllText(image, "");

        var server = McpServerLocator.FindForDesktop(bin, image, mount, null)!;

        Assert.Equal(image, server.Path);
        Assert.Equal(["mcp"], server.Arguments);
    }

    /// <summary>
    /// <c>APPIMAGE</c> is inherited by everything an AppImage starts, so it proves nothing about a
    /// desktop that was not itself started from the image.
    /// </summary>
    [Fact]
    public void An_inherited_appimage_variable_does_not_redirect_a_desktop_running_elsewhere()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_directory, "installed")).FullName;
        File.WriteAllText(Path.Combine(bin, McpServerLocator.ExecutableName), "");
        var image = Path.Combine(_directory, "keypaste-app.AppImage");
        File.WriteAllText(image, "");

        var server = McpServerLocator.FindForDesktop(bin, image, Path.Combine(_directory, ".mount_other"), null)!;

        Assert.Equal(Path.Combine(bin, McpServerLocator.ExecutableName), server.Path);
        Assert.Empty(server.Arguments);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RecordingRunner(params string[] installed) : IProcessRunner
    {
        internal List<McpClientCommand> Calls { get; } = [];

        internal string? RefuseFirst { get; init; }

        internal string? RefuseAll { get; init; }

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, System.Text.Encoding stdinEncoding, TimeSpan timeout)
        {
            if (!installed.Contains(fileName))
            {
                return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
            }

            if (arguments is ["--version"])
            {
                return new ProcessResult(ToolFound: true, ExitCode: 0, "1.0.0", string.Empty);
            }

            Calls.Add(new McpClientCommand(fileName, [.. arguments], MayFail: false));
            var refusal = RefuseAll ?? (Calls.Count == 1 ? RefuseFirst : null);

            return refusal is null
                ? new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty)
                : new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, refusal);
        }
    }
}
