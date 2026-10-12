using System.Text;
using System.Text.Json.Nodes;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Clients;
using Keypaste.Core.Linking;
using Keypaste.Core.Processes;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Connecting a client from Agent Activity: what is shown, what runs, and what the check keeps.
/// </summary>
/// <remarks>
/// Client commands go to a recording runner, the client's command and files are under the fixture's directory, the
/// AppImage's link is a fake, and the check goes to a scripted bridge, so no test reaches a real client's configuration
/// or links anything. The session's home is the machine home's <c>.keypaste</c> unless a test says otherwise. The real
/// bridge and the real prompt window are <c>scripts/verify-connect-client.sh</c>.
/// </remarks>
public sealed class ConnectClientViewModelTests : IDisposable
{
    private const string _secret = "released-value-the-screen-must-never-hold";

    private const string _image = "/home/person/Applications/keypaste.AppImage";

    private const string _holdingKeypaste = """{"mcpServers": {"keypaste": {"command": "k"}}}""";

    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly McpServerCommand _server = new(Path.Combine(Path.GetTempPath(), "keypaste"), [McpServerLocator.BridgeArgument]);

    private readonly TempVault _fixture = new();
    private readonly List<AppVaultSession> _sessions = [];
    private readonly RecordingRunner _runner;
    private readonly List<ScriptedBridge> _bridges = [];

    public ConnectClientViewModelTests()
    {
        _runner = new RecordingRunner(Plant("claude"), Plant("gemini"));
    }

    private string UserHome => Path.Combine(_fixture.Home, "user");

    private string DefaultHome => Path.Combine(UserHome, ".keypaste");

    private string Bin => Path.Combine(_fixture.Home, "bin");

    private McpClientMachine Machine
    {
        get
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOME"] = UserHome,
                ["USERPROFILE"] = UserHome,
                ["APPDATA"] = Path.Combine(UserHome, "AppData", "Roaming"),
                ["PATH"] = Bin,
            };

            return McpClientMachine.From(name => variables.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ConnectClientViewModel Model(
        Func<McpBridgeTargetResult>? findServer = null,
        string? home = null,
        string? approver = null,
        params string[] listed) =>
        new(
            Unlocked(home ?? DefaultHome),
            new ClientConnector(
                _runner,
                Machine,
                approver,
                findServer ?? Serving(_server),
                _ =>
                {
                    var bridge = new ScriptedBridge(listed.Length == 0 ? ["env/ci/DEPLOY_KEY"] : listed);
                    _bridges.Add(bridge);
                    return bridge.Check();
                }));

    private static Func<McpBridgeTargetResult> Serving(McpServerCommand server) =>
        () => new McpBridgeTargetResult(new McpBridgeTarget(server, Link: null, Note: null), Error: null);

    private static Func<McpBridgeTargetResult> Through(ICliLink link) =>
        () => McpBridgeTargetResult.ForDesktop("/tmp/.mount_keypaste/usr/bin", new McpAppImage(_image, link), pathVariable: null);

    private AppVaultSession Unlocked(string home)
    {
        var session = new AppVaultSession(new ManualClock(AppClock.Start), home: home);
        _sessions.Add(session);

        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_fixture.Path_, master.Value));
        return session;
    }

    private McpServerRegistration Expected(string label, params string[] expose) =>
        Registration(_server, new McpServerPins(_fixture.Path_, null, null), label, expose);

    private static McpServerRegistration Registration(McpServerCommand server, McpServerPins pins, string label, params string[] expose)
    {
        Assert.True(McpServerRegistration.TryCreate(server, pins, label, expose, out var registration, out var error), error);
        return registration;
    }

    private string Plan(string id, McpServerRegistration registration)
    {
        var client = McpClientCatalog.Find(id)!;
        Assert.True(McpClientSetup.TryConnect(client, McpClientSetup.Detect(client, Machine, _runner), registration, link: null, out var plan, out var error), error);
        return plan.Display;
    }

    private string Plant(string command)
    {
        Directory.CreateDirectory(Bin);
        var path = Path.Combine(Bin, OperatingSystem.IsWindows() ? command + ".exe" : command);
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, _executable);
        }

        return path;
    }

    private string Seed(string relative, string text)
    {
        var path = Path.Combine(UserHome, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task Connecting_the_chosen_vault_names_no_vault_so_the_client_follows_the_choice()
    {
        Assert.Equal(Core.Settings.ChooseOutcome.Chosen, Core.Settings.ChosenVault.Choose(DefaultHome, _fixture.Path_, onlyIfNone: false));
        using var model = Model();

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(Plan("claude-code", Registration(_server, McpServerPins.None, "claude-code")), model.Preview);
        Assert.DoesNotContain("--vault", model.Preview, StringComparison.Ordinal);

        await model.ConfirmCommand.ExecuteAsync();
        Assert.DoesNotContain(_runner.Changes, change => change.Contains("--vault", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_preview_is_the_plan_core_composed_with_the_vault_and_the_exposure()
    {
        using var model = Model();
        model.Exposure = "env/**, servers/staging/*";

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(Plan("claude-code", Expected("claude-code", "env/**", "servers/staging/*")), model.Preview);
        Assert.Contains("--vault", model.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("--audit-log", model.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("--approver", model.Preview, StringComparison.Ordinal);
        Assert.Contains("--expose servers/staging/*", model.Preview, StringComparison.Ordinal);
        Assert.Empty(_runner.Changes);
        Assert.True(model.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Under_another_home_the_vault_the_log_and_the_apps_own_pipe_are_pinned()
    {
        using var model = Model(home: _fixture.Home);
        var session = _sessions[^1];

        await model.PreviewConnectCommand.ExecuteAsync();

        var pins = new McpServerPins(
            _fixture.Path_,
            Core.Audit.KeypasteHome.AuditPath(_fixture.Home),
            Core.Ipc.ApproverEndpoint.Resolve(null, null, session.Identity));
        Assert.Equal(Plan("claude-code", Registration(_server, pins, "claude-code")), model.Preview);
        Assert.Contains($"--audit-log {pins.AuditLogPath}", model.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_set_approver_alone_pins_only_the_approver()
    {
        Assert.Equal(Core.Settings.ChooseOutcome.Chosen, Core.Settings.ChosenVault.Choose(DefaultHome, _fixture.Path_, onlyIfNone: false));
        using var model = Model(approver: "keypaste-connect-test");

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(Plan("claude-code", Registration(_server, McpServerPins.None with { ApproverName = "keypaste-connect-test" }, "claude-code")), model.Preview);
        Assert.DoesNotContain("--vault", model.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("--audit-log", model.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_the_preview_runs_nothing()
    {
        using var model = Model();

        await model.PreviewConnectCommand.ExecuteAsync();
        model.CancelCommand.Execute(null);

        Assert.Empty(_runner.Changes);
        Assert.False(model.HasPreview);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.False(model.CheckCommand.CanExecute(null));
        Assert.Equal("Cancelled. Nothing was changed.", model.Message);
    }

    [Fact]
    public async Task Running_it_runs_exactly_the_commands_shown()
    {
        using var model = Model();
        model.Label = "my-laptop";

        await model.PreviewConnectCommand.ExecuteAsync();
        var shown = model.Preview;
        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal(shown, string.Join('\n', _runner.Changes));
        Assert.Contains("--client-label my-laptop", shown, StringComparison.Ordinal);
        Assert.StartsWith("Connected.", model.Message, StringComparison.Ordinal);
        Assert.Equal("my-laptop", model.Registered!.ClientLabel);
        Assert.True(model.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_edit_after_the_preview_drops_it_so_an_old_plan_can_never_run()
    {
        using var model = Model();

        await model.PreviewConnectCommand.ExecuteAsync();
        model.Exposure = "servers/**";

        Assert.False(model.HasPreview);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        await model.ConfirmCommand.ExecuteAsync();
        Assert.Empty(_runner.Changes);
    }

    [Fact]
    public async Task A_client_that_is_not_installed_is_said_to_be_and_nothing_is_offered()
    {
        using var model = Model();
        model.SelectedClient = McpClientCatalog.Find("codex")!;

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal("Codex is not installed here: `codex` could not be started.", model.Message);
        Assert.False(model.HasPreview);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Gemini_cli_is_said_to_start_keypaste_only_in_folders_the_person_trusted()
    {
        const string trusted = "Gemini CLI starts keypaste only in folders you have trusted.";
        using var model = Model();
        model.SelectedClient = McpClientCatalog.Find("gemini-cli")!;

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(Plan("gemini-cli", Expected("gemini-cli")), model.Preview);
        Assert.EndsWith(trusted, model.PreviewNote, StringComparison.Ordinal);

        await model.ConfirmCommand.ExecuteAsync();

        Assert.EndsWith(trusted, model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_configured_by_file_is_shown_the_edit_of_its_own_entry_which_runs_only_on_run_it()
    {
        const string seed = """{"mcpServers": {"other": {"command": "other"}}, "theme": "dark"}""";
        var file = Seed(Path.Combine(".cursor", "mcp.json"), seed);
        var backup = McpConfigFile.BackupPath(file);
        using var model = Model();
        model.SelectedClient = McpClientCatalog.Find("cursor")!;

        Assert.Equal("cursor", model.Label);
        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(Plan("cursor", Expected("cursor")), model.Preview);
        Assert.EndsWith($"; keeps the original as {backup}", model.Preview, StringComparison.Ordinal);
        Assert.Equal(
            $"Connect changes only keypaste's entry in {file}, and keeps the first original as {backup}. No password, keyfile or session goes into it.",
            model.PreviewNote);
        Assert.Equal(seed, File.ReadAllText(file));

        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal(
            $"Connected. Cursor starts keypaste mcp for this vault; Check asks through it once. The original is kept as {backup}. Restart Cursor to load it.",
            model.Message);
        Assert.Equal(Expected("cursor").FileEntry(typed: false).ToJsonString(), JsonNode.Parse(File.ReadAllText(file))!["mcpServers"]!["keypaste"]!.ToJsonString());
        Assert.Equal("other", (string?)JsonNode.Parse(File.ReadAllText(file))!["mcpServers"]!["other"]!["command"]);
        Assert.Equal(seed, File.ReadAllText(backup));
        Assert.Equal("cursor", model.Registered!.ClientLabel);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Removing_previews_and_then_runs_the_clients_own_removal()
    {
        Seed(".claude.json", _holdingKeypaste);
        using var model = Model();

        await model.PreviewRemoveCommand.ExecuteAsync();
        Assert.Equal("claude mcp remove --scope user keypaste", model.Preview);
        Assert.Empty(_runner.Changes);

        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal(["claude mcp remove --scope user keypaste"], _runner.Changes);
        Assert.StartsWith("Removed.", model.Message, StringComparison.Ordinal);
        Assert.Null(model.Registered);
    }

    [Fact]
    public async Task A_client_holding_no_keypaste_entry_is_not_connected_and_nothing_is_offered()
    {
        var cursor = Seed(Path.Combine(".cursor", "mcp.json"), """{"mcpServers": {"other": {"command": "other"}}}""");
        using var model = Model();

        await model.PreviewRemoveCommand.ExecuteAsync();

        Assert.Equal("Claude Code is not connected; there is nothing to remove.", model.Message);
        Assert.False(model.HasPreview);
        Assert.False(model.ConfirmCommand.CanExecute(null));

        model.SelectedClient = McpClientCatalog.Find("cursor")!;
        await model.PreviewRemoveCommand.ExecuteAsync();

        Assert.Equal("Cursor is not connected; there is nothing to remove.", model.Message);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.Empty(_runner.Changes);
        Assert.False(File.Exists(McpConfigFile.BackupPath(cursor)));
    }

    [Fact]
    public async Task Removing_a_client_configured_by_file_edits_only_its_entry()
    {
        const string seed = """{"mcpServers": {"keypaste": {"command": "k"}, "other": {"command": "other"}}}""";
        var file = Seed(Path.Combine(".cursor", "mcp.json"), seed);
        var backup = McpConfigFile.BackupPath(file);
        using var model = Model();
        model.SelectedClient = McpClientCatalog.Find("cursor")!;

        await model.PreviewRemoveCommand.ExecuteAsync();

        Assert.Equal($"edit {file}: remove mcpServers.keypaste; keeps the original as {backup}", model.Preview);
        Assert.Equal($"Remove changes only keypaste's entry in {file}, and keeps the first original as {backup}.", model.PreviewNote);

        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal("""{"mcpServers": {"other": {"command": "other"}}}""", File.ReadAllText(file));
        Assert.Equal(seed, File.ReadAllText(backup));
        Assert.Equal(
            $"Removed. Cursor can no longer start keypaste mcp for this vault. The original is kept as {backup}. Restart Cursor to load it.",
            model.Message);
    }

    [Fact]
    public async Task A_refusal_is_reported_in_the_clients_words_and_leaves_nothing_to_check()
    {
        _runner.Refusal = "that scope is not writable here";
        using var model = Model();

        await model.PreviewConnectCommand.ExecuteAsync();
        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal("Claude Code refused: that scope is not writable here", model.Message);
        Assert.False(model.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_removal_is_reported_in_the_clients_words()
    {
        Seed(".claude.json", _holdingKeypaste);
        _runner.Refusal = "No MCP server found with name: keypaste";
        _runner.Refuses = "remove";
        using var model = Model();

        await model.PreviewRemoveCommand.ExecuteAsync();
        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal("Claude Code refused: No MCP server found with name: keypaste", model.Message);
    }

    [Fact]
    public async Task An_exposure_the_bridge_would_refuse_is_refused_before_anything_is_shown()
    {
        using var model = Model();
        model.Exposure = new string('x', 200);

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.StartsWith("Exposure: ", model.Message, StringComparison.Ordinal);
        Assert.False(model.HasPreview);
    }

    [Fact]
    public async Task Without_a_bridge_to_start_there_is_nothing_to_connect()
    {
        using var model = Model(() => new McpBridgeTargetResult(Target: null, "keypaste was not found in the test's directory and on PATH, so there is nothing a client could start"));

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal("keypaste was not found in the test's directory and on PATH, so there is nothing a client could start.", model.Message);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task A_link_path_holding_another_program_beside_an_existing_backup_is_refused_before_anything_is_shown()
    {
        var link = FakeCliLink.At(CliLinkState.Foreign, backupExists: true);
        using var model = Model(Through(link));

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(
            $"{link.LinkPath} is another program, and {link.BackupPath} already exists, which keypaste never overwrites. "
                + $"Move or delete it to replace {link.LinkPath}. Nothing was changed.",
            model.Message);
        Assert.False(model.HasPreview);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Inside_an_appimage_the_link_is_created_first_as_a_step_shown_with_the_rest()
    {
        var link = FakeCliLink.At(CliLinkState.Absent);
        using var model = Model(Through(link));

        await model.PreviewConnectCommand.ExecuteAsync();

        var server = new McpServerCommand(link.LinkPath, [McpServerLocator.BridgeArgument]);
        Assert.Equal(
            $"create {link.LinkPath}, a script that starts {_image} cli\n" + Plan("claude-code", Registration(server, new McpServerPins(_fixture.Path_, null, null), "claude-code")),
            model.Preview);
        Assert.Contains($"It first creates {link.LinkPath}, which starts this AppImage.", model.PreviewNote, StringComparison.Ordinal);
        Assert.Equal(0, link.Links);

        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal(1, link.Links);
        Assert.Equal(2, _runner.Changes.Count);
        Assert.StartsWith("Connected.", model.Message, StringComparison.Ordinal);
        Assert.Equal(link.LinkPath, model.Registered!.Server.Path);
    }

    [Fact]
    public async Task Inside_an_appimage_a_file_keypaste_did_not_write_is_replaced_only_on_run_it()
    {
        var link = FakeCliLink.At(CliLinkState.Foreign);
        using var model = Model(Through(link));

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.StartsWith(
            $"replace {link.LinkPath}, which is another program, with a script that starts {_image} cli; the old file is kept as {link.BackupPath}\n",
            model.Preview,
            StringComparison.Ordinal);
        Assert.Contains($"It first replaces {link.LinkPath}, which is another program, keeping it as {link.BackupPath}.", model.PreviewNote, StringComparison.Ordinal);
        Assert.Equal(0, link.Replaces);

        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal((0, 1), (link.Links, link.Replaces));
        Assert.StartsWith("Connected.", model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_that_is_not_made_registers_nothing()
    {
        var link = FakeCliLink.At(CliLinkState.Absent, answer: new CliLinkResult(CliLinkOutcome.Failed, Reason: "Permission denied"));
        using var model = Model(Through(link));

        await model.PreviewConnectCommand.ExecuteAsync();
        await model.ConfirmCommand.ExecuteAsync();

        Assert.Equal($"keypaste could not create {link.LinkPath}: Permission denied. Nothing else was changed.", model.Message);
        Assert.Empty(_runner.Changes);
        Assert.Null(model.Registered);
        Assert.False(model.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task With_one_listed_name_the_check_asks_for_it_and_keeps_no_value()
    {
        using var model = await ConnectedAsync();

        await model.CheckCommand.ExecuteAsync();

        var bridge = Assert.Single(_bridges);
        Assert.Equal("k1_0", bridge.AskedFor);
        Assert.StartsWith("Connected: you approved env/ci/DEPLOY_KEY", model.CheckMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(_secret, model.CheckMessage, StringComparison.Ordinal);
        Assert.False(model.IsChecking);
        await bridge.Serving.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(bridge.Ended);
    }

    [Fact]
    public async Task With_several_listed_names_the_person_picks_and_the_first_is_offered()
    {
        using var model = await ConnectedAsync("env/ci/DEPLOY_KEY", "env/ci/OTHER");

        await model.CheckCommand.ExecuteAsync();

        Assert.True(model.IsPicking);
        Assert.Equal(["env/ci/DEPLOY_KEY", "env/ci/OTHER"], model.CheckEntries.Select(entry => entry.Name));
        Assert.Equal("env/ci/DEPLOY_KEY", model.SelectedCheckEntry!.Name);
        Assert.Null(_bridges[0].AskedFor);

        model.SelectedCheckEntry = model.CheckEntries[1];
        await model.AskCommand.ExecuteAsync();

        Assert.Equal("k1_1", _bridges[0].AskedFor);
        Assert.False(model.IsPicking);
        Assert.StartsWith("Connected: you approved env/ci/OTHER", model.CheckMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scripted_bridge_answers_within_the_checks_own_call()
    {
        using var bridge = new ScriptedBridge(["env/ci/DEPLOY_KEY"]);
        await using var check = bridge.Check();

        var listing = check.ListAsync(Token);

        Assert.True(listing.IsCompleted, "the scripted listing did not finish inside ListAsync");
        var listed = await listing;
        Assert.Null(listed.Problem);
        Assert.Equal("env/ci/DEPLOY_KEY", Assert.Single(listed.Entries).Name);
    }

    [Fact]
    public async Task Closing_the_screen_while_the_check_waits_ends_its_bridge()
    {
        var model = await ConnectedAsync("env/ci/DEPLOY_KEY", "env/ci/OTHER");
        await model.CheckCommand.ExecuteAsync();
        Assert.True(model.IsChecking);

        model.Dispose();

        await _bridges[0].Serving.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(_bridges[0].Ended);
    }

    private async Task<ConnectClientViewModel> ConnectedAsync(params string[] listed)
    {
        var model = Model(listed: listed);
        await model.PreviewConnectCommand.ExecuteAsync();
        await model.ConfirmCommand.ExecuteAsync();
        Assert.NotNull(model.Registered);
        return model;
    }

    public void Dispose()
    {
        foreach (var bridge in _bridges)
        {
            bridge.Dispose();
        }

        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        _fixture.Dispose();
    }

    /// <summary>Records every client command except the installed probe, each by its bare command as the preview shows it.</summary>
    private sealed class RecordingRunner(params string[] installed) : IProcessRunner
    {
        internal List<string> Calls { get; } = [];

        internal List<string> Changes => [.. Calls.Where(call => !call.EndsWith(" --version", StringComparison.Ordinal))];

        internal string? Refusal { get; set; }

        /// <summary>The <c>mcp</c> verb that <see cref="Refusal"/> answers.</summary>
        internal string Refuses { get; set; } = "add";

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            Calls.Add(new McpClientCommand(new McpClientProgram(fileName, []), Path.GetFileNameWithoutExtension(fileName), arguments, MayFail: false).Display);

            if (!installed.Contains(fileName))
            {
                return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
            }

            return Refusal is { } refusal && arguments is not ["--version"] && arguments[1] == Refuses
                ? new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, refusal)
                : new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
        }
    }

    /// <summary>A bridge that lists what it is given and approves every request, inside the write that asks.</summary>
    private sealed class ScriptedBridge : IDisposable
    {
        private readonly ScriptedBridgeChannels _channels = new();
        private readonly string[] _listed;

        internal ScriptedBridge(string[] listed)
        {
            _listed = listed;
            Serving = ServeAsync();
        }

        internal Task Serving { get; }

        internal string? AskedFor { get; private set; }

        internal bool Ended { get; private set; }

        internal McpConnectionCheck Check() => _channels.Check();

        private async Task ServeAsync()
        {
            using var reader = new StreamReader(_channels.BridgeReads, new UTF8Encoding(false));
            using var writer = new StreamWriter(_channels.BridgeWrites, new UTF8Encoding(false)) { AutoFlush = true };

            while (await reader.ReadLineAsync() is { } line)
            {
                var message = JsonNode.Parse(line)!;
                if (message["id"] is not { } id)
                {
                    continue;
                }

                var tool = (string?)message["params"]?["name"];
                if (tool == "request_credential")
                {
                    AskedFor = (string?)message["params"]!["arguments"]!["entry"];
                }

                JsonObject result = tool switch
                {
                    "list_entry_names" => new JsonObject
                    {
                        ["structuredContent"] = new JsonObject
                        {
                            ["entries"] = new JsonArray([.. _listed.Select((name, i) => (JsonNode)new JsonObject
                            {
                                ["handle"] = $"k1_{i}",
                                ["group"] = name[..name.LastIndexOf('/')],
                                ["name"] = name[(name.LastIndexOf('/') + 1)..],
                                ["fields"] = new JsonArray("password"),
                                ["tags"] = new JsonArray(),
                            })]),
                        },
                    },
                    "request_credential" => new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = _secret }),
                        ["structuredContent"] = new JsonObject { ["value"] = _secret },
                    },
                    _ => new JsonObject(),
                };

                await writer.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString());
            }

            Ended = true;
        }

        public void Dispose()
        {
            _channels.ToBridge.Dispose();
            Serving.Wait(TimeSpan.FromSeconds(10));
            _channels.Dispose();
        }
    }
}
