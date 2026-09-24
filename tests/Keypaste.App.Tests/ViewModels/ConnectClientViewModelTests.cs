using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Connecting a client from Agent Activity: what is shown, what runs, and what the check keeps.
/// </summary>
/// <remarks>
/// Client commands go to a recording runner and the check to a scripted bridge, so no test reaches
/// a real client's configuration. The real bridge and the real prompt window are
/// <c>scripts/verify-connect-client.sh</c>.
/// </remarks>
public sealed class ConnectClientViewModelTests : IDisposable
{
    private const string _secret = "released-value-the-screen-must-never-hold";

    private static readonly McpServerCommand _server = new(Path.Combine(Path.GetTempPath(), "keypaste-mcp"), []);

    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session;
    private readonly RecordingRunner _runner = new("claude");
    private readonly List<ScriptedBridge> _bridges = [];

    public ConnectClientViewModelTests()
    {
        _session = new AppVaultSession(new ManualClock(), home: _fixture.Home);

        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ConnectClientViewModel Model(McpServerCommand? server = null, params string[] listed)
    {
        var found = server ?? _server;
        return new ConnectClientViewModel(
            _session,
            new ClientConnector(
                _runner,
                () => found.Path.Length == 0 ? null : found,
                "in the test's directory and on PATH",
                _ =>
                {
                    var bridge = new ScriptedBridge(listed.Length == 0 ? ["env/ci/DEPLOY_KEY"] : listed);
                    _bridges.Add(bridge);
                    return bridge.Check();
                }));
    }

    private McpServerRegistration Expected(string label, params string[] expose)
    {
        Assert.True(McpServerRegistration.TryCreate(_server, _fixture.Path_, label, expose, out var registration, out var error), error);
        return registration;
    }

    [Fact]
    public async Task The_preview_is_the_plan_core_composed_with_the_vault_and_the_exposure()
    {
        using var model = Model();
        model.Exposure = "env/**, servers/staging/*";

        await model.PreviewConnectCommand.ExecuteAsync();

        var plan = McpClientSetup.Connect(McpClientCatalog.Find("claude-code")!, Expected("claude-code", "env/**", "servers/staging/*"));
        Assert.Equal(plan.Display, model.Preview);
        Assert.Contains("--vault", model.Preview, StringComparison.Ordinal);
        Assert.Contains("--expose servers/staging/*", model.Preview, StringComparison.Ordinal);
        Assert.Empty(_runner.Changes);
        Assert.True(model.ConfirmCommand.CanExecute(null));
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
    public async Task A_client_configured_by_file_is_shown_its_block_and_nothing_is_written_or_claimed()
    {
        using var model = Model();
        model.SelectedClient = McpClientCatalog.Find("claude-desktop")!;

        Assert.Equal("claude-desktop", model.Label);
        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal(string.Join('\n', Expected("claude-desktop").ConfigBlock()), model.Preview);
        Assert.Contains("keypaste wrote nothing", model.PreviewNote, StringComparison.Ordinal);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Removing_previews_and_then_runs_the_clients_own_removal()
    {
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
        using var model = Model(new McpServerCommand(string.Empty, []));

        await model.PreviewConnectCommand.ExecuteAsync();

        Assert.Equal("keypaste-mcp was not found in the test's directory and on PATH, so there is nothing a client could start.", model.Message);
        Assert.Empty(_runner.Calls);
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
        var model = Model(null, listed);
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

        _session.Dispose();
        _fixture.Dispose();
    }

    /// <summary>Records every client command except the installed probe.</summary>
    private sealed class RecordingRunner(params string[] installed) : IProcessRunner
    {
        internal List<string> Calls { get; } = [];

        internal List<string> Changes => [.. Calls.Where(call => !call.EndsWith(" --version", StringComparison.Ordinal))];

        internal string? Refusal { get; set; }

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            Calls.Add(new McpClientCommand(fileName, arguments, MayFail: false).Display);

            if (!installed.Contains(fileName))
            {
                return new ProcessResult(ToolFound: false, ExitCode: -1, string.Empty, string.Empty);
            }

            return Refusal is { } refusal && arguments is not ["--version"] && arguments[1] == "add"
                ? new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, refusal)
                : new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
        }
    }

    /// <summary>A bridge that lists what it is given and approves every request, over anonymous pipes.</summary>
    private sealed class ScriptedBridge : IDisposable
    {
        private readonly AnonymousPipeServerStream _toBridge = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromBridge = new(PipeDirection.In);
        private readonly string[] _listed;

        internal ScriptedBridge(string[] listed)
        {
            _listed = listed;
            Serving = Task.Run(ServeAsync);
        }

        internal Task Serving { get; }

        internal string? AskedFor { get; private set; }

        internal bool Ended { get; private set; }

        internal McpConnectionCheck Check() => new(_toBridge, _fromBridge);

        private async Task ServeAsync()
        {
            using var reads = new AnonymousPipeClientStream(PipeDirection.In, _toBridge.ClientSafePipeHandle);
            using var writes = new AnonymousPipeClientStream(PipeDirection.Out, _fromBridge.ClientSafePipeHandle);
            using var reader = new StreamReader(reads, new UTF8Encoding(false));
            using var writer = new StreamWriter(writes, new UTF8Encoding(false)) { AutoFlush = true };

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
            _toBridge.Dispose();
            Serving.Wait(TimeSpan.FromSeconds(10));
            _fromBridge.Dispose();
        }
    }
}
