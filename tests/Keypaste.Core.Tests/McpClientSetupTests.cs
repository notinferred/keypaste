using System.Text;
using System.Text.Json;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What connecting a client composes and runs, shared by <c>keypaste setup</c> and the desktop.
/// </summary>
/// <remarks>
/// Every command runs against a recording runner and every file under a temporary home, so no test reaches a real
/// client's configuration.
/// </remarks>
public sealed class McpClientSetupTests : IDisposable
{
    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-setup-").FullName;

    public static TheoryData<string, string[], string[]> Commands => new()
    {
        { "claude-code", ["mcp", "add", "--scope", "user", "--transport", "stdio", "keypaste", "--", "/k/keypaste", "mcp"], ["mcp", "remove", "--scope", "user", "keypaste"] },
        { "codex", ["mcp", "add", "keypaste", "--", "/k/keypaste", "mcp"], ["mcp", "remove", "keypaste"] },
        { "gemini-cli", ["mcp", "add", "--scope", "user", "keypaste", "/k/keypaste", "--", "mcp"], ["mcp", "remove", "--scope", "user", "keypaste"] },
        { "devin", ["mcp", "add", "-s", "user", "keypaste", "--", "/k/keypaste", "mcp"], ["mcp", "remove", "-s", "user", "keypaste"] },
    };

    public static TheoryData<string, McpClientPlatform, string> Rewritten => new()
    {
        { "gemini-cli", McpClientPlatform.Linux, "$" },
        { "gemini-cli", McpClientPlatform.MacOS, "$" },
        { "gemini-cli", McpClientPlatform.Windows, "$" },
        { "gemini-cli", McpClientPlatform.Windows, "%" },
        { "claude-code", McpClientPlatform.Linux, "${" },
        { "devin", McpClientPlatform.MacOS, "${" },
        { "vscode", McpClientPlatform.Windows, "${" },
        { "cursor", McpClientPlatform.Linux, "${" },
    };

    private static McpClient Claude => McpClientCatalog.Find("claude-code")!;

    private static McpClient Cursor => McpClientCatalog.Find("cursor")!;

    private string Home => Path.Combine(_directory, "home");

    private string Bin => Path.Combine(_directory, "bin");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [MemberData(nameof(Commands))]
    public void Each_client_with_a_command_is_told_its_own_scope_and_where_the_program_goes(string id, string[] add, string[] remove)
    {
        var client = McpClientCatalog.Find(id)!;
        var registration = Registration(id, server: new McpServerCommand("/k/keypaste", [McpServerLocator.BridgeArgument]), vault: null);

        Assert.Equal([.. add, "--client-label", id, "--allow-run", "--expose", "**"], client.AddArguments(registration));
        Assert.Equal(remove, client.RemoveArguments());
    }

    [Fact]
    public void Each_platform_offers_the_clients_that_run_on_it_with_commands_first()
    {
        string[] everywhere = ["claude-code", "codex", "gemini-cli", "devin", "vscode", "cursor"];

        Assert.Equal(everywhere, McpClientCatalog.For(McpClientPlatform.Linux).Select(client => client.Id));
        Assert.Equal([.. everywhere, "claude-desktop"], McpClientCatalog.For(McpClientPlatform.MacOS).Select(client => client.Id));
        Assert.Equal([.. everywhere, "claude-desktop"], McpClientCatalog.For(McpClientPlatform.Windows).Select(client => client.Id));
        Assert.Equal(McpClientCatalog.All.Select(client => client.Id), [.. everywhere, "claude-desktop"]);
    }

    [Fact]
    public void Running_a_plan_runs_exactly_the_commands_it_shows_with_the_program_found()
    {
        var runner = new RecordingRunner();
        var (detection, claude) = Planted("claude");
        Assert.True(McpClientSetup.TryConnect(Claude, detection, Registration(expose: ["env/**", "servers/staging/*"]), link: null, out var plan, out var error), error);

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, runner).Status);

        Assert.All(runner.Calls, call => Assert.Equal(claude, call.FileName));
        Assert.Equal(plan.Display, string.Join('\n', runner.Calls.Select(call => "claude " + string.Join(' ', call.Arguments.Select(Quoted)))));
        Assert.Equal(plan.Steps.OfType<McpClientCommand>().Select(command => command.Started), runner.Calls.Select(call => (IReadOnlyList<string>)[call.FileName, .. call.Arguments]));
    }

    [Fact]
    public void An_earlier_entry_is_cleared_before_the_add_and_its_absence_is_not_a_refusal()
    {
        var runner = new RecordingRunner { RefuseFirst = "No MCP server found with name: keypaste" };

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(Connected(Claude, Planted("claude").Detection), runner).Status);

        Assert.Equal(["mcp", "remove", "--scope", "user", "keypaste"], runner.Calls[0].Arguments);
        Assert.Equal(["mcp", "add", "--scope", "user", "--transport", "stdio", "keypaste", "--"], runner.Calls[1].Arguments.Take(8));
    }

    [Fact]
    public void A_refused_add_reports_the_clients_own_first_line()
    {
        var runner = new RecordingRunner { RefuseAll = "that scope is not writable here\nmore detail" };

        var result = McpClientSetup.Apply(Connected(Claude, Planted("claude").Detection), runner);

        Assert.Equal(McpSetupStatus.Refused, result.Status);
        Assert.Equal("that scope is not writable here", result.Said);
    }

    [Fact]
    public void A_client_whose_command_is_not_found_is_not_installed_and_never_started()
    {
        var runner = new RecordingRunner();
        Directory.CreateDirectory(Bin);

        var detection = McpClientSetup.Detect(Claude, Machine(), runner);

        Assert.False(detection.Found);
        Assert.Empty(runner.Calls);
        Assert.False(McpClientSetup.TryConnect(Claude, detection, Registration(), link: null, out _, out var error));
        Assert.Equal("Claude Code is not installed on this machine", error);
    }

    [Fact]
    public void A_client_whose_version_cannot_be_started_is_not_installed()
    {
        Plant("claude");

        Assert.False(McpClientSetup.Detect(Claude, Machine(), new RecordingRunner { Starts = false }).Found);
    }

    [Theory]
    [MemberData(nameof(Rewritten))]
    public void A_registration_the_client_would_rewrite_is_refused_before_anything_runs(string id, McpClientPlatform platform, string marker)
    {
        var client = McpClientCatalog.Find(id)!;
        var detection = Reachable(client, platform);

        Assert.True(McpClientSetup.TryConnect(client, detection, Registration(id), link: null, out _, out var error), error);

        var argument = Registration(id, vault: "a" + marker + "b.kdbx");
        Assert.False(McpClientSetup.TryConnect(client, detection, argument, link: null, out var plan, out error));
        Assert.Null(plan);
        Assert.Contains("a" + marker + "b.kdbx", error, StringComparison.Ordinal);

        var path = Registration(id, server: new McpServerCommand(Path.Combine(_directory, "k" + marker + "p", "keypaste"), [McpServerLocator.BridgeArgument]));
        Assert.False(McpClientSetup.TryConnect(client, detection, path, link: null, out _, out error));
        Assert.StartsWith(client.DisplayName + " would rewrite " + marker, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Gemini_cli_keeps_a_percent_sign_outside_windows_and_codex_keeps_both()
    {
        var gemini = McpClientCatalog.Find("gemini-cli")!;
        var codex = McpClientCatalog.Find("codex")!;

        Assert.True(McpClientSetup.TryConnect(gemini, Reachable(gemini, McpClientPlatform.Linux), Registration(vault: "a%b.kdbx"), link: null, out _, out var error), error);
        Assert.True(McpClientSetup.TryConnect(codex, Reachable(codex, McpClientPlatform.Windows), Registration(vault: "a${b}%c$.kdbx"), link: null, out _, out error), error);
    }

    [Fact]
    public void A_client_configured_by_file_gets_one_edit_of_its_own_entry_that_keeps_the_first_original()
    {
        var path = Seed(Cursor, """{"mcpServers": {"other": {"command": "other"}}}""");
        var detection = McpClientSetup.Detect(Cursor, Machine(), new RecordingRunner());
        var registration = Registration("cursor");

        Assert.True(detection.Found);
        Assert.Equal(path, detection.ConfigPath);
        Assert.True(McpClientSetup.TryConnect(Cursor, detection, registration, link: null, out var plan, out var error), error);

        var edit = Assert.IsType<McpConfigEdit>(Assert.Single(plan.Steps));
        Assert.Equal(McpConfigFile.BackupPath(path), edit.Backup);
        Assert.Equal(
            $"edit {path}: set mcpServers.keypaste {McpConfigFile.OneLine(registration.FileEntry(typed: false))}; keeps the original as {edit.Backup}",
            plan.Display);

        var result = McpClientSetup.Apply(plan, new RecordingRunner());

        Assert.Equal(new McpSetupResult(McpSetupStatus.Done, Said: null, edit.Backup), result);
        Assert.Equal("""{"mcpServers": {"other": {"command": "other"}}}""", File.ReadAllText(edit.Backup!));
        using var written = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("other", written.RootElement.GetProperty("mcpServers").GetProperty("other").GetProperty("command").GetString());
        Assert.Equal(registration.Server.Path, written.RootElement.GetProperty("mcpServers").GetProperty("keypaste").GetProperty("command").GetString());

        Assert.True(McpClientSetup.TryConnect(Cursor, detection, registration, link: null, out var again, out _));
        Assert.Null(((McpConfigEdit)again.Steps[0]).Backup);
        Assert.DoesNotContain("keeps the original", again.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void Vs_codes_entry_names_its_transport()
    {
        var vscode = McpClientCatalog.Find("vscode")!;
        Seed(vscode, "{}");

        Assert.True(McpClientSetup.TryConnect(vscode, McpClientSetup.Detect(vscode, Machine(), new RecordingRunner()), Registration("vscode"), link: null, out var plan, out var error), error);

        var entry = Assert.IsType<McpConfigEdit>(Assert.Single(plan.Steps)).Entry!;
        Assert.Equal("servers", ((McpConfigEdit)plan.Steps[0]).ServersKey);
        Assert.Equal(["type", "command", "args"], entry.Select(member => member.Key));
        Assert.Equal("stdio", (string?)entry["type"]);
    }

    /// <summary>The entry is written into a JSON file, so a path holding a control character, a quote or a backslash must still name it exactly.</summary>
    [Fact]
    public void A_file_entry_names_every_path_exactly()
    {
        var path = Seed(Cursor, "{}");
        var server = new McpServerCommand(Path.Combine(_directory, "ke\u0001y\"pa\\ste\tbin"), [McpServerLocator.BridgeArgument]);
        var registration = Registration("cursor", server: server);

        Assert.True(McpClientSetup.TryConnect(Cursor, McpClientSetup.Detect(Cursor, Machine(), new RecordingRunner()), registration, link: null, out var plan, out var error), error);
        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, new RecordingRunner()).Status);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var entry = json.RootElement.GetProperty("mcpServers").GetProperty(McpClientCatalog.ServerName);

        Assert.Equal(server.Path, entry.GetProperty("command").GetString());
        Assert.Equal(registration.CommandLine().Skip(1), entry.GetProperty("args").EnumerateArray().Select(argument => argument.GetString() ?? string.Empty));
    }

    [Fact]
    public void A_client_configured_by_file_is_found_by_its_folder_or_its_launcher_and_nothing_of_it_starts()
    {
        var runner = new RecordingRunner();

        Assert.False(McpClientSetup.Detect(Cursor, Machine(), runner).Found);

        Plant("cursor");
        var byLauncher = McpClientSetup.Detect(Cursor, Machine(), runner);
        Assert.True(byLauncher.Found);
        Assert.Equal(Path.Combine(Home, ".cursor", "mcp.json"), byLauncher.ConfigPath);

        File.Delete(Path.Combine(Bin, Exe("cursor")));
        Directory.CreateDirectory(Path.Combine(Home, ".cursor"));
        Assert.True(McpClientSetup.Detect(Cursor, Machine(), runner).Found);

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void The_packaged_claude_desktop_is_found_by_its_package_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the packaged Claude Desktop is Windows'");

        var desktop = McpClientCatalog.Find("claude-desktop")!;
        Assert.False(McpClientSetup.Detect(desktop, Machine(), new RecordingRunner()).Found);

        Directory.CreateDirectory(Path.Combine(Home, "AppData", "Local", "Packages", McpConfigLocation.ClaudeDesktopPackage));

        var detection = McpClientSetup.Detect(desktop, Machine(), new RecordingRunner());
        Assert.True(detection.Found);
        Assert.Equal(Path.Combine(Home, "AppData", "Roaming", "Claude", "claude_desktop_config.json"), detection.ConfigPath);
    }

    [Fact]
    public void A_file_client_whose_file_a_variable_moved_is_found_but_not_edited()
    {
        var vscode = McpClientCatalog.Find("vscode")!;
        Seed(vscode, "{}");

        var detection = McpClientSetup.Detect(vscode, Machine(("VSCODE_PORTABLE", Path.Combine(_directory, "portable"))), new RecordingRunner());

        Assert.True(detection.Found);
        Assert.Equal("VSCODE_PORTABLE is set, so VS Code may keep its configuration where keypaste does not look", detection.Problem);
        Assert.False(McpClientSetup.TryConnect(vscode, detection, Registration("vscode"), link: null, out _, out var error));
        Assert.Equal(detection.Problem, error);
    }

    /// <summary>VS Code reads a relative <c>XDG_CONFIG_HOME</c> against the folder it starts in, so its usual file is not the one it reads.</summary>
    [Fact]
    public void Vs_code_under_a_relative_xdg_config_home_is_found_but_not_edited()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "VS Code reads XDG_CONFIG_HOME only on Linux");

        var vscode = McpClientCatalog.Find("vscode")!;
        Seed(vscode, "{}");
        Plant("code");

        var detection = McpClientSetup.Detect(vscode, Machine(("XDG_CONFIG_HOME", "cfg")), new RecordingRunner());

        Assert.True(detection.Found);
        Assert.Null(detection.ConfigPath);
        Assert.Equal("keypaste cannot tell where VS Code keeps its configuration", detection.Problem);
        Assert.False(McpClientSetup.TryConnect(vscode, detection, Registration("vscode"), link: null, out _, out var error));
        Assert.Equal(detection.Problem, error);
    }

    [Fact]
    public void A_removal_from_a_client_holding_no_entry_is_not_connected_and_runs_nothing()
    {
        var runner = new RecordingRunner();
        var plan = McpClientSetup.Remove(Claude, Planted("claude").Detection, new McpEntryReading(McpEntryPresence.Absent, "/h/.claude.json", Reason: null));

        Assert.True(plan.IsNotConnected);
        Assert.Equal("not connected; nothing to remove", plan.Display);
        Assert.Equal(new McpSetupResult(McpSetupStatus.NotConnected, Said: null, Backup: null), McpClientSetup.Apply(plan, runner));
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(McpEntryPresence.Present)]
    [InlineData(McpEntryPresence.Unknown)]
    public void A_removal_from_a_client_that_may_hold_an_entry_runs_its_own_removal(McpEntryPresence presence)
    {
        var runner = new RecordingRunner();
        var plan = McpClientSetup.Remove(Claude, Planted("claude").Detection, new McpEntryReading(presence, Path: null, Reason: null));

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, runner).Status);
        Assert.Equal("claude mcp remove --scope user keypaste", plan.Display);
        Assert.Equal(["mcp", "remove", "--scope", "user", "keypaste"], Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public void A_refused_removal_is_a_refusal_in_the_clients_words()
    {
        var runner = new RecordingRunner { RefuseAll = "No MCP server found with name: keypaste" };
        var plan = McpClientSetup.Remove(Claude, Planted("claude").Detection, new McpEntryReading(McpEntryPresence.Unknown, Path: null, Reason: null));

        Assert.Equal(new McpSetupResult(McpSetupStatus.Refused, "No MCP server found with name: keypaste", Backup: null), McpClientSetup.Apply(plan, runner));
    }

    [Fact]
    public void Removing_a_file_clients_entry_edits_only_it_and_one_without_an_entry_is_left_unwritten()
    {
        const string Holding = """
            {
              "mcpServers": {
                "other": {},
                "keypaste": {
                  "command": "k"
                }
              }
            }
            """;
        const string Kept = """
            {
              "mcpServers": {
                "other": {}
              }
            }
            """;
        var path = Seed(Cursor, Holding);
        var machine = Machine();
        var detection = McpClientSetup.Detect(Cursor, machine, new RecordingRunner());

        var plan = McpClientSetup.Remove(Cursor, detection, McpClientConfig.Read(Cursor, machine));
        Assert.Equal($"edit {path}: remove mcpServers.keypaste; keeps the original as {McpConfigFile.BackupPath(path)}", plan.Display);
        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(plan, new RecordingRunner()).Status);
        Assert.Equal(Kept, File.ReadAllText(path));

        var written = File.GetLastWriteTimeUtc(path);
        Assert.True(McpClientSetup.Remove(Cursor, detection, McpClientConfig.Read(Cursor, machine)).IsNotConnected);

        var unread = McpClientSetup.Remove(Cursor, detection, new McpEntryReading(McpEntryPresence.Unknown, Path: null, Reason: null));
        Assert.Equal(McpSetupStatus.NotConnected, McpClientSetup.Apply(unread, new RecordingRunner()).Status);
        Assert.Equal(Kept, File.ReadAllText(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_refused_edit_is_reported_in_keypastes_words_and_writes_nothing()
    {
        var path = Seed(Cursor, """{"mcpServers": {"keypaste": {}, "keypaste": {}}}""");
        Assert.True(McpClientSetup.TryConnect(Cursor, McpClientSetup.Detect(Cursor, Machine(), new RecordingRunner()), Registration("cursor"), link: null, out var plan, out var error), error);

        var result = McpClientSetup.Apply(plan, new RecordingRunner());

        Assert.Equal(McpSetupStatus.Refused, result.Status);
        Assert.StartsWith(path + " ", result.Said, StringComparison.Ordinal);
        Assert.EndsWith("nothing was written", result.Said, StringComparison.Ordinal);
        Assert.Equal("""{"mcpServers": {"keypaste": {}, "keypaste": {}}}""", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded ")]
    [InlineData("two\nlines")]
    [InlineData("zero\u200Bwidth")]
    public void A_label_the_audit_log_would_rewrite_is_refused(string label)
    {
        Assert.False(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste", [McpServerLocator.BridgeArgument]), Pins("vault.kdbx"), label, [], out _, out var error));
        Assert.Contains("label", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exposure_the_bridge_would_refuse_is_refused_before_anything_is_written()
    {
        Assert.False(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste", [McpServerLocator.BridgeArgument]), Pins("vault.kdbx"), "claude-code", ["env/**", " "], out _, out var error));
        Assert.StartsWith("what agents can see: ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pipe/name")]
    [InlineData("pipe:name")]
    [InlineData("pipe\u0001")]
    [InlineData("keypaste-locked-0123")]
    [InlineData("KEYPASTE-LOCKED-0123")]
    [InlineData("Keypaste-Locked-0123")]
    public void An_approver_the_bridge_would_refuse_is_refused_before_anything_is_written(string approver)
    {
        Assert.False(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste", [McpServerLocator.BridgeArgument]), new McpServerPins(null, null, approver), "codex", [], out _, out var error));
        Assert.StartsWith("approver: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_vault_the_audit_log_and_the_ceilings_are_made_absolute()
    {
        Assert.True(McpServerRegistration.TryCreate(
            new McpServerCommand("keypaste", [McpServerLocator.BridgeArgument]), new McpServerPins("vault.kdbx", "audit.jsonl", null, "clients.toml"), "codex", [], out var registration, out _));
        Assert.Equal(Path.GetFullPath("vault.kdbx"), registration.VaultPath);
        Assert.Equal(Path.GetFullPath("audit.jsonl"), registration.AuditLogPath);
        Assert.Equal(Path.GetFullPath("clients.toml"), registration.ClientsPath);
    }

    /// <summary>
    /// The only things a client is ever told: where the bridge is, which vault, log and owner, what to call it, that it
    /// offers run and what it may name. A keyfile, a password or a session has nowhere to go.
    /// </summary>
    [Fact]
    public void A_registration_holds_only_the_bridge_its_pins_the_label_run_and_the_exposure_in_that_order()
    {
        var server = new McpServerCommand(Path.Combine(_directory, "keypaste"), [McpServerLocator.BridgeArgument]);
        Assert.True(McpServerRegistration.TryCreate(
            server,
            new McpServerPins(Path.Combine(_directory, "v.kdbx"), Path.Combine(_directory, "a.jsonl"), "keypaste-vault-0123", Path.Combine(_directory, "c.toml")),
            "claude-code",
            ["env/**"],
            out var registration,
            out var error), error);

        Assert.Equal(
            [server.Path, "mcp", "--vault", Path.Combine(_directory, "v.kdbx"), "--audit-log", Path.Combine(_directory, "a.jsonl"),
             "--clients", Path.Combine(_directory, "c.toml"), "--approver", "keypaste-vault-0123", "--client-label", "claude-code", "--allow-run", "--expose", "env/**"],
            registration.CommandLine());
    }

    /// <summary>
    /// The owner holds each agent to its settings, so with no globs given a bridge reaches every item and offers run,
    /// and the globs a person gives take the place of every item rather than adding to it.
    /// </summary>
    [Fact]
    public void A_registration_reaches_every_item_unless_the_person_narrowed_it()
    {
        Assert.Equal([McpServerRegistration.WideExposure], Registration().Expose);
        Assert.Equal(["tag:env:billing", "api/**"], Registration(expose: ["tag:env:billing", "api/**"]).Expose);
        Assert.Equal(["--allow-run", "--expose", "tag:env:billing", "--expose", "api/**"], Registration(expose: ["tag:env:billing", "api/**"]).CommandLine().TakeLast(5));
        Assert.True(EntryExposure.TryCreate(Registration().Expose, out var wide, out var error), error);
        Assert.True(wide.Covers(EntryExposure.Default));
    }

    [Fact]
    public void A_registration_for_the_chosen_vault_names_no_vault()
    {
        var registration = Registration(vault: null);

        Assert.Null(registration.VaultPath);
        Assert.Equal([registration.Server.Path, "mcp", "--client-label", "claude-code", "--allow-run", "--expose", "**"], registration.CommandLine());
        Assert.DoesNotContain("--vault", McpConfigFile.OneLine(registration.FileEntry(typed: false)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_displayed_argument_with_a_space_is_quoted()
    {
        var command = new McpClientCommand(new McpClientProgram("/usr/bin/node", ["/opt/claude/cli.js"]), "claude", ["mcp", "add", "C:\\Program Files\\keypaste\\keypaste.exe"], MayFail: false);

        Assert.Equal("claude mcp add \"C:\\Program Files\\keypaste\\keypaste.exe\"", command.Display);
        Assert.Equal(["/usr/bin/node", "/opt/claude/cli.js", "mcp", "add", "C:\\Program Files\\keypaste\\keypaste.exe"], command.Started);
    }

    private static McpServerPins Pins(string? vault) => new(vault, AuditLogPath: null, ApproverName: null);

    private static string Quoted(string value) =>
        value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'') ? value : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static McpClientDetection Reachable(McpClient client, McpClientPlatform platform) =>
        client.Wiring == McpWiring.OwnCli
            ? new McpClientDetection(platform, Found: true, new McpClientProgram("/opt/tool", []), ConfigPath: null, Problem: null)
            : new McpClientDetection(platform, Found: true, Program: null, "/home/person/tool.json", Problem: null);

    private McpServerRegistration Registration(
        string label = "claude-code",
        IReadOnlyList<string>? expose = null,
        McpServerCommand? server = null,
        string? vault = "vault.kdbx")
    {
        Assert.True(McpServerRegistration.TryCreate(
            server ?? new McpServerCommand(Path.Combine(_directory, "keypaste"), [McpServerLocator.BridgeArgument]),
            Pins(vault is null ? null : Path.Combine(_directory, vault)),
            label,
            expose ?? [],
            out var registration,
            out var error), error);

        return registration;
    }

    private McpSetupPlan Connected(McpClient client, McpClientDetection detection)
    {
        Assert.True(McpClientSetup.TryConnect(client, detection, Registration(client.Id), link: null, out var plan, out var error), error);
        return plan;
    }

    private (McpClientDetection Detection, string Program) Planted(string command)
    {
        var program = Plant(command);
        var detection = McpClientSetup.Detect(McpClientCatalog.All.Single(client => client.Executable == command), Machine(), new RecordingRunner());

        Assert.True(detection.Found);
        Assert.Equal(program, detection.Program!.FileName);
        return (detection, program);
    }

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

    private string Seed(McpClient client, string text)
    {
        var path = client.Configuration!.PathOn(Machine())!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

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

    private sealed class RecordingRunner : IProcessRunner
    {
        internal List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        internal bool Starts { get; init; } = true;

        internal string? RefuseFirst { get; init; }

        internal string? RefuseAll { get; init; }

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            if (!Starts)
            {
                return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
            }

            if (arguments is ["--version"])
            {
                return new ProcessResult(ToolFound: true, ExitCode: 0, "1.0.0", string.Empty);
            }

            Calls.Add((fileName, [.. arguments]));
            var refusal = RefuseAll ?? (Calls.Count == 1 ? RefuseFirst : null);

            return refusal is null
                ? new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty)
                : new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, refusal);
        }
    }
}
