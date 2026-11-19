using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Agents lists what the app's session authority holds, the AI tools found on this computer with the agents
/// that asked, and what the audit file says was answered since the unlock, over the app's real endpoint, and
/// says so when any of it cannot be read. Nothing on it answers a request (D-0456).
/// </summary>
public sealed class AgentActivityViewModelTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_in_front_of_a_person_is_listed_and_counts_down()
    {
        await using var app = await App.StartAsync();
        app.Person.Hold = true;

        var pending = app.RequestAsync();
        await app.Person.Waiting.WaitAsync(_connect, Token);

        using var model = app.Model();

        var waiting = Assert.Single(model.Waiting);
        Assert.Equal("example · password", waiting.What);
        Assert.Equal("claude-code · label ci-probe", waiting.Who);
        Assert.Equal("answered for you in 45 s", waiting.Left);
        Assert.Equal("Denies in 0:45", waiting.LeftText);
        Assert.True(model.HasWaiting);
        Assert.True(model.IsAvailable);

        app.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("answered for you in 40 s", Assert.Single(model.Waiting).Left);

        app.Clock.Advance(TimeSpan.FromSeconds(40));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.Empty(model.Waiting);
        Assert.True(model.NothingWaiting);
    }

    [Fact]
    public async Task A_waiting_linked_set_is_listed_with_each_field()
    {
        await using var app = await App.StartAsync();
        app.Person.Hold = true;

        var pending = app.RequestSetAsync();
        await app.Person.Waiting.WaitAsync(_connect, Token);

        using var model = app.Model();

        var waiting = Assert.Single(model.Waiting);
        Assert.Equal("example · password, example · url", waiting.Asked);
        Assert.Equal("claude-code · label ci-probe", waiting.Who);
        Assert.Equal("Denies in 0:45", waiting.LeftText);

        app.Clock.Advance(TimeSpan.FromSeconds(45));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.Empty(model.Waiting);
    }

    [Fact]
    public async Task A_waiting_save_is_listed_with_its_place()
    {
        await using var app = await App.StartAsync();
        app.Person.Hold = true;

        var pending = app.SaveAsync();
        await app.Person.Waiting.WaitAsync(_connect, Token);

        using var model = app.Model();

        var waiting = Assert.Single(model.Waiting);
        Assert.Equal("Save API_TOKEN in example", waiting.Asked);
        Assert.Equal("claude-code · label ci-probe", waiting.Who);
        Assert.Equal("Denies in 0:45", waiting.LeftText);

        app.Clock.Advance(TimeSpan.FromSeconds(45));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.Empty(model.Waiting);
    }

    [Fact]
    public void Without_an_authority_nothing_is_read_and_the_screen_says_so()
    {
        using var home = new TempAuditHome();
        using var model = new AgentActivityViewModel(null, home.Home, new ManualClock(AppClock.Start));

        Assert.True(model.IsUnavailable);
        Assert.Empty(model.Waiting);
        Assert.False(model.NothingWaiting);
    }

    /// <summary>
    /// Show the prompt asks the authority to bring the prompt forward and answers nothing: the request still waits.
    /// With no window on screen, as here, the person is told the prompt is no longer open.
    /// </summary>
    [Fact]
    public async Task Show_the_prompt_asks_the_authority_and_leaves_the_request_waiting()
    {
        await using var app = await App.StartAsync();
        app.Person.Hold = true;
        var pending = app.RequestAsync();
        await app.Person.Waiting.WaitAsync(_connect, Token);
        List<string> said = [];
        using var model = new AgentActivityViewModel(app.Authority, app.Fixture.Home, app.Clock, toast: (message, _) => said.Add(message));

        Assert.True(model.ShowPromptCommand.CanExecute(null));
        model.ShowPromptCommand.Press();

        Assert.Equal(["The prompt window is no longer open."], said);
        Assert.Single(model.Waiting);
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, app.Person.Asked);

        app.Clock.Advance(TimeSpan.FromSeconds(45));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.False(model.ShowPromptCommand.CanExecute(null));
    }

    /// <summary>A row listed only while its settings are open goes on saying what is true of it: once its bridge leaves, it is no longer in use.</summary>
    [Fact]
    public async Task A_row_kept_for_its_open_settings_stays_current_after_its_bridge_leaves()
    {
        await using var app = await App.StartAsync();
        using var model = app.Model();
        await using var bridge = await app.ConnectAsync(new AttachClient("helper", "1.0", "helper"));
        app.Clock.Advance(TimeSpan.FromSeconds(1));

        var helper = model.Rows.Single(row => row.Key == "helper");
        Assert.Equal(("in use now", true), (helper.Status, helper.IsLive));
        helper.IsOpen = true;

        await bridge.DisposeAsync();
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            deadline.CancelAfter(_connect);

            while (app.Authority.Clients.Any(client => client.Label == "helper"))
            {
                await Task.Delay(20, deadline.Token);
            }
        }

        app.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Same(helper, model.Rows.Single(row => row.Key == "helper"));
        Assert.Equal(("hasn't asked in the last 30 days", false), (helper.Status, helper.IsLive));
    }

    /// <summary>Agents looks for the tools once when it opens, never on its ticks, and a look still running ends with the screen.</summary>
    [Fact]
    public async Task The_tools_are_looked_for_once_when_the_screen_opens_and_the_look_ends_with_it()
    {
        await using var app = await App.StartAsync();
        var tools = app.Tools().ClaudeCode(connected: true);
        using var held = new ManualResetEventSlim();
        tools.Runner.Hold = held;

        var closed = app.Model(tools.Connector());
        Assert.True(closed.IsLooking);
        closed.Dispose();
        await closed.Looking.WaitAsync(_connect, Token);
        held.Set();

        Assert.DoesNotContain(closed.Rows, row => row.Presence is not null);

        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);

        Assert.False(model.IsLooking);
        Assert.False(model.IsEmpty);
        var claude = model.Rows.Single(row => row.Key == "claude-code");
        Assert.Equal(("Claude Code", "Connected", true), (claude.Title, claude.Status, claude.IsLive));

        var probes = Probes(tools);
        app.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(probes, Probes(tools));
    }

    [Fact]
    public async Task With_no_tool_found_the_Connect_sheet_offers_the_prompt_and_an_entry_without_a_name()
    {
        await using var app = await App.StartAsync();
        var tools = app.Tools();

        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);

        Assert.Equal(model.Rows.Count == 0, model.IsEmpty);
        Assert.DoesNotContain(AgentListHeading.Found, model.Listed);

        model.StartConnectCommand.Press();

        Assert.True(model.HasSheet);
        Assert.Empty(model.Sheet!.Found);
        Assert.Contains("\"args\": [\"mcp\"]", model.Sheet.Config, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pressing_the_chosen_row_opens_its_detail_again_after_Back()
    {
        await using var app = await App.StartAsync();
        using var model = app.Model(app.Tools().Cursor(connected: false).Connector());
        await model.Looking.WaitAsync(_connect, Token);

        var chosen = model.Selected!;
        Assert.False(model.ShowsDetail);

        model.Open(chosen);
        Assert.True(model.ShowsDetail);

        model.CloseDetailCommand.Execute(null);
        Assert.False(model.ShowsDetail);
        Assert.Same(chosen, model.Selected);

        model.Open(chosen);
        Assert.True(model.ShowsDetail);
    }

    /// <summary>
    /// The agents connected come first under Connected, then the tools merely found under Found on this computer; each
    /// tool says whether it holds keypaste and offers what fits.
    /// </summary>
    [Fact]
    public async Task Rows_hold_the_agents_connected_then_the_tools_found()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.ApprovedOnce;
        await app.RequestAsync();
        var tools = app.Tools().ClaudeCode(connected: true).Cursor(connected: false).GeminiUnreadable().VsCodeMoved();

        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);

        string[] ours = ["ci-probe", "claude-code", "gemini-cli", "vscode", "cursor"];
        Assert.Equal(ours, model.Rows.Select(row => row.Key).Where(ours.Contains));
        Assert.DoesNotContain(model.Rows, row => row.Key == AgentNames.Any);
        Assert.Equal(
            ["Connected", "ci-probe", "claude-code", "Found on this computer", "gemini-cli", "vscode", "cursor"],
            model.Listed.Select(listed => listed is AgentRow row ? row.Key : listed.ToString()!).Where(key => ours.Contains(key) || key is "Connected" or "Found on this computer"));

        var claude = Row(model, "claude-code");
        Assert.Equal(("Connected", false, true), (claude.Status, claude.OffersConnect, claude.OffersDisconnect));
        var gemini = Row(model, "gemini-cli");
        Assert.Equal("Can't read its settings", gemini.Status);
        Assert.False(string.IsNullOrEmpty(gemini.StatusDetail));
        Assert.Equal((true, false, true), (gemini.OffersConnect, gemini.OffersRepair, gemini.OffersDisconnect));
        var vscode = Row(model, "vscode");
        Assert.Contains("VSCODE_APPDATA is set", vscode.StatusDetail, StringComparison.Ordinal);
        Assert.Equal((false, false, false), (vscode.OffersConnect, vscode.OffersRepair, vscode.OffersDisconnect));
        var cursor = Row(model, "cursor");
        Assert.Equal(("Not connected", true, false, false), (cursor.Status, cursor.OffersConnect, cursor.OffersRepair, cursor.OffersDisconnect));
        Assert.True(model.ConnectCommand.CanExecute(cursor));
        Assert.False(model.ConnectCommand.CanExecute(vscode));
        Assert.False(model.DisconnectCommand.CanExecute(cursor));
        Assert.Null(Row(model, "ci-probe").Tool);
    }

    /// <summary>
    /// One tool's part of the sheet is open at a time; while its plan runs no other Connect or Disconnect can start and the
    /// sheet cannot be closed, and once it has run the tools are looked for again.
    /// </summary>
    [Fact]
    public async Task While_a_sheets_plan_runs_no_other_Connect_or_Disconnect_can_start()
    {
        await using var app = await App.StartAsync();
        var tools = app.Tools().ClaudeCode(connected: true).Cursor(connected: false).GeminiUnreadable();
        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);
        var claude = Row(model, "claude-code");
        var cursor = Row(model, "cursor");
        var gemini = Row(model, "gemini-cli");

        model.ConnectCommand.Press(cursor);
        await model.Opening.WaitAsync(_connect, Token);
        Assert.Equal("cursor", model.Connection!.Client.Id);

        model.ConnectCommand.Press(gemini);
        await model.Opening.WaitAsync(_connect, Token);

        Assert.Equal("gemini-cli", model.Connection!.Client.Id);
        Assert.True(model.Connection.HasPlan);

        using var held = new ManualResetEventSlim();
        tools.Runner.HoldChanges = held;
        var running = model.Connection.ConfirmCommand.PressAsync();

        Assert.True(model.Connection.IsBusy);
        Assert.False(model.ConnectCommand.CanExecute(cursor));
        Assert.False(model.DisconnectCommand.CanExecute(claude));
        Assert.False(model.CloseSheetCommand.CanExecute(null));

        held.Set();
        await running.WaitAsync(_connect, Token);
        await model.Looking.WaitAsync(_connect, Token);

        Assert.StartsWith("Connected Gemini CLI", model.Connection.Message, StringComparison.Ordinal);
        Assert.True(model.ConnectCommand.CanExecute(cursor));
        Assert.True(model.CloseSheetCommand.CanExecute(null));
        Assert.True(Probes(tools) > 3, "the tools were not looked for again after the plan ran");

        model.CloseSheetCommand.Press();

        Assert.Null(model.Connection);
        Assert.False(model.HasSheet);
    }

    private static AgentRow Row(AgentActivityViewModel model, string key) => model.Rows.Single(row => row.Key == key);

    private static int Probes(FakeTools tools) => tools.Runner.Calls.Count(call => call.EndsWith(" --version", StringComparison.Ordinal));

    private static string Everything(AgentActivityViewModel model) =>
        string.Join(
            '\n',
            [
                model.Status, model.Unavailable,
                .. model.Waiting.Select(row => $"{row.Who} {row.What} {row.Left}"),
            ]);

    [Fact]
    public async Task A_lock_empties_the_list_because_the_authority_holds_nothing()
    {
        await using var app = await App.StartAsync();

        using var model = app.Model();

        app.Authority.Session.Lock(VaultLockReason.Manual);
        model.Refresh();

        Assert.Same(ApproverActivity.None, app.Authority.Activity);
        Assert.Empty(model.Waiting);
        Assert.True(model.IsUnavailable);
    }

    /// <summary>The app's authority on a manual clock with a scripted person, attached to over its real endpoint.</summary>
    private sealed class App : IAsyncDisposable
    {
        private readonly ApproverClient _client;

        private App(TempVault fixture, ManualClock clock, ScriptedPerson person, AppAuthority authority, ApproverClient client, string session)
        {
            Fixture = fixture;
            Clock = clock;
            Person = person;
            Authority = authority;
            _client = client;
            Session = session;
        }

        internal TempVault Fixture { get; }

        internal ManualClock Clock { get; }

        internal ScriptedPerson Person { get; }

        internal AppAuthority Authority { get; }

        internal string Session { get; }

        /// <param name="arrange">Writes into the vault before the app unlocks it, which then saves it.</param>
        internal static async Task<App> StartAsync(Action<Vault>? arrange = null)
        {
            var fixture = new TempVault();
            fixture.Sees();

            if (arrange is not null)
            {
                using var vault = Vault.Open(fixture.Path_, TempVault.Password);
                arrange(vault);
                vault.Save();
            }

            var clock = new ManualClock(AppClock.Start);
            var person = new ScriptedPerson();
#pragma warning disable CA2000
            var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => person);
#pragma warning restore CA2000

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
            }

            var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
            Assert.Equal(serving.Session, attached!.Session);

            return new App(fixture, clock, person, authority, client, serving.Session);
        }

        internal AgentActivityViewModel Model(ClientConnector? connector = null) => new(Authority, Fixture.Home, Clock, connector: connector);

        /// <summary>A computer of the test's own to look for AI tools on.</summary>
        internal FakeTools Tools() => new(Path.Combine(Fixture.Home, "computer"));

        /// <summary>A second connection attached to the same session, as a second agent would be.</summary>
        /// <param name="identity">What it says it is, or null for nothing.</param>
        internal async Task<ApproverClient> ConnectAsync(AttachClient? identity = null)
        {
            var serving = Assert.IsType<AuthorityStatus.Serving>(Authority.Status);
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(Fixture.Path_) { Client = identity }, Token))!.Attached);
            return client;
        }

        internal Task<CredentialReply?> RequestAsync(ApproverClient? on = null) =>
            (on ?? _client).RequestAsync(
                new CredentialRequest
                {
                    Entry = "example",
                    Field = "password",
                    Reason = "deploy",
                    Exposure = ["**"],
                    ClientName = "claude-code",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        internal Task<SetReply?> RequestSetAsync() =>
            _client.RequestSetAsync(
                new SetRequest
                {
                    Items = [new("example", "password"), new("example", "url")],
                    Reason = "deploy",
                    Exposure = ["**"],
                    ClientName = "claude-code",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        internal Task<SaveReply?> SaveAsync() =>
            _client.SaveAsync(
                new SaveRequest
                {
                    Item = "example",
                    Field = "API_TOKEN",
                    Reason = "keep the new token",
                    Exposure = ["**"],
                    ClientName = "claude-code",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        /// <summary>Appends a prompted grant naming <paramref name="session"/>, as the bridge writes one.</summary>
        internal void Audit(string entry, string session)
        {
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(Fixture.Home), Clock, out var log, out var error), error);

            using (log)
            {
                var record = new AuditRecord
                {
                    Tool = "request_credential",
                    Client = new AuditClient("claude-code", null, "ci-probe"),
                    Args = AuditArgs.ForCredentialRequest(entry, "password", "deploy"),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = "a person approved this request",
                    Exposure = ["**"],
                    Session = session,
                };

                Assert.True(log.TryAppend(record, out var failure), failure);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            Authority.Dispose();
            Fixture.Dispose();
        }
    }

    /// <summary>A person who answers as told, or who has not answered yet.</summary>
    private sealed class ScriptedPerson : IApprovalChannel
    {
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal bool Hold { get; set; }

        internal int Asked { get; private set; }

        internal Task Waiting => _waiting.Task;

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) => HoldAsync(cancellationToken);

        public ValueTask<ApprovalAnswer> AskAsync(SetPrompt prompt, CancellationToken cancellationToken) => HoldAsync(cancellationToken);

        public async ValueTask<SaveAnswer> AskToSaveAsync(SavePrompt prompt, CancellationToken cancellationToken) => new(await HoldAsync(cancellationToken), null);


        private async ValueTask<ApprovalAnswer> HoldAsync(CancellationToken cancellationToken)
        {
            Asked++;

            if (!Hold)
            {
                return Answer;
            }

            _waiting.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Withdrawn, which is the only way a held prompt ends.
            }

            return ApprovalAnswer.Denied;
        }
    }
}
