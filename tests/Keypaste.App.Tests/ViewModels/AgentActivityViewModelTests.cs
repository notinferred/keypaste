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
        Assert.Equal("45s left", waiting.LeftText);
        Assert.True(model.HasWaiting);
        Assert.Empty(model.Grants);
        Assert.True(model.IsAvailable);
        var serving = Assert.IsType<AuthorityStatus.Serving>(app.Authority.Status);
        Assert.Equal($"process {serving.Owner.ProcessId} · unlock {serving.Session}", model.Serving);

        app.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("answered for you in 40 s", Assert.Single(model.Waiting).Left);

        app.Clock.Advance(TimeSpan.FromSeconds(40));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.Empty(model.Waiting);
        Assert.True(model.NothingWaiting);
    }

    /// <summary>
    /// The status names each agent the vault releases to without asking and where, beneath this
    /// computer's ceilings, rather than a sentence that could be true of any agent.
    /// </summary>
    [Fact]
    public async Task The_status_names_each_agent_on_Automatic_and_for_what()
    {
        await using var app = await App.StartAsync(vault =>
        {
            Assert.True(ProjectScope.TryCreate("billing", "dev", out var dev, out var error), error);
            var store = new AgentAccessStore(vault);
            Assert.Equal(AgentAccessWrite.Written, store.Write("claude-code", AgentAccess.Defaults with { Projects = [new ProjectAccess(dev, AgentMode.Automatic)] }));
            Assert.Equal(AgentAccessWrite.Written, store.Write("held", AgentAccess.Defaults with { Mode = AgentMode.Automatic }));
        });
        File.WriteAllText(KeypasteHome.ClientsPath(app.Fixture.Home), "[[client]]\nlabel = \"held\"\nmode = \"ask\"\n");

        using var model = app.Model();

        Assert.EndsWith(
            "nothing is released unless you allow it, except what these agents are Automatic for: claude-code (billing:dev).",
            model.Status,
            StringComparison.Ordinal);

        File.Delete(KeypasteHome.ClientsPath(app.Fixture.Home));
        model.Refresh();

        Assert.EndsWith("except what these agents are Automatic for: claude-code (billing:dev); held (by default).", model.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_approved_request_is_listed_as_a_grant_with_its_remaining_lifetime_and_no_value()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;

        var reply = await app.RequestAsync();
        Assert.Equal(AuditMethod.Prompt, reply!.Method);

        using var model = app.Model();

        var grant = Assert.Single(model.Grants);
        Assert.Equal("example · password", grant.What);
        Assert.Equal("claude-code · label ci-probe", grant.Who);
        Assert.Equal("ends in 3600 s", grant.Left);
        Assert.NotNull(grant.Grant);
        Assert.True(model.EndAllCommand.CanExecute(null));

        app.Clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal("ends in 3590 s", Assert.Single(model.Grants).Left);
        Assert.DoesNotContain(reply.Value!, Everything(model), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task End_ends_the_allowance_in_the_authority_so_the_same_request_is_asked_again(bool all)
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;

        await app.RequestAsync();
        Assert.Equal(AuditMethod.GrantCache, (await app.RequestAsync())!.Method);

        using var model = app.Model();

        if (all)
        {
            model.EndAllCommand.Execute(null);
        }
        else
        {
            model.EndCommand.Execute(Assert.Single(model.Grants));
        }

        Assert.Empty(model.Grants);
        Assert.Empty(app.Authority.Activity.Grants);
        Assert.False(model.EndAllCommand.CanExecute(null));

        var again = await app.RequestAsync();

        Assert.Equal(AuditMethod.Prompt, again!.Method);
        Assert.Equal(2, app.Person.Asked);
    }

    [Fact]
    public async Task EndById_EndsThatAllowance()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;

        await using var second = await app.ConnectAsync();
        await app.RequestAsync();
        await app.RequestAsync(second);

        using var model = app.Model();

        Assert.Equal(2, model.Grants.Count);
        var listed = app.Authority.Grants();
        Assert.Equal(model.Grants.Select(row => row.Id).Order(StringComparer.Ordinal), listed.Select(grant => grant.Id).Order(StringComparer.Ordinal));
        Assert.All(model.Grants, row => Assert.Equal(GrantId.Of(row.Grant!.Value), row.Id));
        Assert.All(listed, grant => Assert.Equal(new GrantSummary(grant.Id, "credential", "claude-code", "example", "password", 3600), grant));

        var ended = model.Grants[0];
        model.EndCommand.Execute(ended);

        var kept = Assert.Single(model.Grants);
        Assert.NotEqual(ended.Id, kept.Id);
        Assert.Equal(kept.Id, Assert.Single(app.Authority.Grants()).Id);
        Assert.False(app.Authority.Revoke(ended.Id!));
        Assert.True(app.Authority.Revoke(kept.Id!.ToUpperInvariant()));
        Assert.Empty(app.Authority.Grants());
    }

    [Fact]
    public async Task History_is_the_audit_records_naming_this_session()
    {
        await using var app = await App.StartAsync();
        app.Audit("env/other/FROM_BEFORE", "an-earlier-session");
        app.Audit("example", app.Session);

        using var model = app.Model();

        Assert.False(model.HasHistoryMessage);
        Assert.Equal($"1 record of 2 in {AuditHistory.ShortPath(model.AuditPath)}, since you unlocked", model.HistoryHeading);
        Assert.DoesNotContain(app.Session, model.HistoryHeading + model.History, StringComparison.Ordinal);
        Assert.Contains("example", model.History, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM_BEFORE", model.History, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_record_written_while_the_screen_is_open_appears_at_the_next_tick()
    {
        await using var app = await App.StartAsync();
        app.Audit("example", app.Session);

        using var model = app.Model();
        app.Audit("example", app.Session);

        app.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Contains("2 records of 2", model.HistoryHeading, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_with_no_records_says_so_instead_of_an_empty_table()
    {
        await using var app = await App.StartAsync();
        app.Audit("env/other/FROM_BEFORE", "an-earlier-session");

        using var model = app.Model();

        Assert.False(model.HasHistory);
        Assert.Equal("Nothing has been recorded since you unlocked.", model.HistoryMessage);
        Assert.Empty(model.HistoryHeading);
    }

    [Fact]
    public async Task A_missing_log_reads_as_a_session_with_no_records_yet()
    {
        await using var app = await App.StartAsync();

        using var model = app.Model();

        Assert.False(model.HasHistory);
        Assert.Equal("Nothing has been recorded since you unlocked.", model.HistoryMessage);
        Assert.DoesNotContain(model.AuditPath, model.HistoryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_log_is_unavailable()
    {
        await using var app = await App.StartAsync();
        Directory.CreateDirectory(KeypasteHome.AuditPath(app.Fixture.Home));

        using var model = app.Model();

        Assert.False(model.HasHistory);
        Assert.StartsWith("History unavailable: the audit log couldn't be read", model.HistoryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lock_empties_both_lists_because_the_authority_holds_nothing()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        await app.RequestAsync();

        using var model = app.Model();
        Assert.Single(model.Grants);

        app.Authority.Session.Lock(VaultLockReason.Manual);
        model.Refresh();

        Assert.Same(ApproverActivity.None, app.Authority.Activity);
        Assert.Empty(model.Grants);
        Assert.Empty(model.Waiting);
        Assert.True(model.IsUnavailable);
        Assert.False(model.NoGrants);
        Assert.Equal("History can't be shown, because this app is not answering agents for this vault.", model.HistoryMessage);
    }

    [Fact]
    public void Without_an_authority_nothing_is_read_and_the_screen_says_so()
    {
        using var home = new TempAuditHome();
        using var model = new AgentActivityViewModel(null, home.Home, new ManualClock(AppClock.Start));

        Assert.True(model.IsUnavailable);
        Assert.Empty(model.Waiting);
        Assert.Empty(model.Grants);
        Assert.False(model.NothingWaiting);
    }

    [Fact]
    public async Task A_disposed_screen_stops_reading()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        await app.RequestAsync();

        var model = app.Model();
        var before = Assert.Single(model.Grants);
        model.Dispose();

        app.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Same(before, Assert.Single(model.Grants));
    }

    [Fact]
    public void A_runs_timed_grant_is_listed_by_the_id_keypaste_grants_revokes_it_by()
    {
        var row = ActivityRow.EnvGranted(3, new EnvGrantInForce("key", "acme-api", "staging", "npm start", TimeSpan.FromSeconds(899.5)));

        Assert.Equal(3, row.Number);
        Assert.Equal(GrantId.OfEnv("key"), row.Id);
        Assert.Null(row.Grant);
        Assert.Equal("keypaste run · label none configured", row.Who);
        Assert.Equal("acme-api · staging · npm start · set", row.What);
        Assert.Equal("ends in 900 s", row.Left);
    }

    [Fact]
    public async Task An_allowance_row_says_its_time_left_as_the_table_shows_it_and_End_says_so()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        await app.RequestAsync();

        List<string> said = [];
        using var model = new AgentActivityViewModel(app.Authority, app.Fixture.Home, app.Clock, toast: said.Add);

        var grant = Assert.Single(model.Grants);
        Assert.Equal(("claude-code", "example · password", "1h left", 1d), (grant.Client, grant.Detail, grant.LeftText, grant.Fraction));

        app.Clock.Advance(TimeSpan.FromSeconds(181));

        grant = Assert.Single(model.Grants);
        Assert.Equal("57m left", grant.LeftText);
        Assert.Equal(3419d / 3600, grant.Fraction, 3);

        model.EndCommand.Execute(grant);

        Assert.Empty(model.Grants);
        Assert.Equal(["Ended claude-code's allowance."], said);
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
        using var model = new AgentActivityViewModel(app.Authority, app.Fixture.Home, app.Clock, toast: said.Add);

        Assert.True(model.ShowPromptCommand.CanExecute(null));
        model.ShowPromptCommand.Execute(null);

        Assert.Equal(["The prompt window is no longer open."], said);
        Assert.Single(model.Waiting);
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, app.Person.Asked);

        app.Clock.Advance(TimeSpan.FromSeconds(45));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.False(model.ShowPromptCommand.CanExecute(null));
    }

    /// <summary>A confirmation of a change that gives an agent more waits like a request, named by where it says it came from, with no reason shown.</summary>
    [Fact]
    public async Task A_waiting_change_is_listed_by_where_it_came_from_with_Show_the_prompt()
    {
        await using var app = await App.StartAsync();
        app.Person.Hold = true;
        Assert.True(AccessEdit.TryCreate(null, "automatic", null, null, null, null, null, reset: false, out var edit, out var error), error);
        using var givingUp = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var changing = app.Authority.ChangeAccessAsync("claude-code", edit, givingUp.Token).AsTask();
        await app.Person.Waiting.WaitAsync(_connect, Token);

        using var model = app.Model();

        var waiting = Assert.Single(model.Waiting);
        Assert.Equal(("claude-code", "Your change to claude-code's settings"), (waiting.Client, waiting.Asked));
        Assert.EndsWith("s left", waiting.LeftText, StringComparison.Ordinal);
        Assert.True(model.ShowPromptCommand.CanExecute(null));

        await givingUp.CancelAsync();

        try
        {
            await changing.WaitAsync(_connect, Token);
        }
        catch (OperationCanceledException)
        {
            // Withdrawn, as a screen that stops waiting withdraws it.
        }
    }

    /// <summary>
    /// A row's summary follows a change made outside its panel, such as one an agent asked for and the person
    /// confirmed in the prompt window, which rewrites the agent's record and leaves the number of records as it was.
    /// </summary>
    [Fact]
    public async Task A_rows_summary_follows_a_change_made_outside_its_panel()
    {
        await using var app = await App.StartAsync(vault =>
            Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(vault).Write("claude-code", AgentAccess.Defaults with { Mode = AgentMode.Ask })));
        app.Person.Answer = ApprovalAnswer.Approved;

        using var model = app.Model();
        var claude = model.Rows.Single(row => row.Key == "claude-code");
        Assert.StartsWith("Ask every time · ", claude.Summary, StringComparison.Ordinal);

        Assert.True(AccessEdit.TryCreate(null, "automatic", null, null, null, null, null, reset: false, out var edit, out var error), error);
        Assert.Equal(AuditDecision.Granted, (await app.Authority.ChangeAccessAsync("claude-code", edit, Token)).Decision);
        app.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.StartsWith("Automatic · ", claude.Summary, StringComparison.Ordinal);
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
        Assert.False(model.HasNoTools);
        var claude = model.Rows.Single(row => row.Key == "claude-code");
        Assert.Equal(("Claude Code", "Connected", true), (claude.Title, claude.Status, claude.IsLive));

        var probes = Probes(tools);
        app.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(probes, Probes(tools));
    }

    [Fact]
    public async Task With_no_tool_found_the_screen_names_those_it_looks_for()
    {
        await using var app = await App.StartAsync();
        var tools = app.Tools();

        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);

        Assert.True(model.HasNoTools);
        Assert.StartsWith("None of the AI tools keypaste connects was found on this computer: Claude Code, Codex, ", model.NoTools, StringComparison.Ordinal);
    }

    /// <summary>
    /// The agents that asked come first, then the tools found, connected before not and in catalogue order,
    /// then Every other agent; each tool says whether it holds keypaste and offers what fits.
    /// </summary>
    [Fact]
    public async Task Rows_hold_the_agents_that_asked_then_the_tools_found_then_every_other_agent()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        await app.RequestAsync();
        var tools = app.Tools().ClaudeCode(connected: true).Cursor(connected: false).GeminiUnreadable().VsCodeMoved();

        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);

        string[] ours = ["ci-probe", "claude-code", "gemini-cli", "vscode", "cursor", AgentNames.Any];
        Assert.Equal(ours, model.Rows.Select(row => row.Key).Where(ours.Contains));
        Assert.Equal(AgentNames.Any, model.Rows[^1].Key);

        var claude = Row(model, "claude-code");
        Assert.Equal(("Connected", false, true, true), (claude.Status, claude.OffersConnect, claude.OffersConnectAgain, claude.OffersDisconnect));
        var gemini = Row(model, "gemini-cli");
        Assert.StartsWith("keypaste can't tell whether Gemini CLI has it: ", gemini.Status, StringComparison.Ordinal);
        Assert.Equal((true, false, true), (gemini.OffersConnect, gemini.OffersConnectAgain, gemini.OffersDisconnect));
        var vscode = Row(model, "vscode");
        Assert.Contains("VSCODE_APPDATA is set", vscode.Status, StringComparison.Ordinal);
        Assert.Equal((false, false, false), (vscode.OffersConnect, vscode.OffersConnectAgain, vscode.OffersDisconnect));
        var cursor = Row(model, "cursor");
        Assert.Equal(("Not connected", true, false, false), (cursor.Status, cursor.OffersConnect, cursor.OffersConnectAgain, cursor.OffersDisconnect));
        Assert.True(model.ConnectCommand.CanExecute(cursor));
        Assert.False(model.ConnectCommand.CanExecute(vscode));
        Assert.False(model.DisconnectCommand.CanExecute(cursor));
        Assert.Null(Row(model, "ci-probe").Tool);
    }

    /// <summary>
    /// One pane is open at a time; while its plan runs no other Connect, Connect again or Disconnect can start and it
    /// cannot be closed, and once it has run the tools are looked for again.
    /// </summary>
    [Fact]
    public async Task While_a_panes_plan_runs_no_other_Connect_or_Disconnect_can_start()
    {
        await using var app = await App.StartAsync();
        var tools = app.Tools().ClaudeCode(connected: true).Cursor(connected: false).GeminiUnreadable();
        using var model = app.Model(tools.Connector());
        await model.Looking.WaitAsync(_connect, Token);
        var claude = Row(model, "claude-code");
        var cursor = Row(model, "cursor");
        var gemini = Row(model, "gemini-cli");

        model.ConnectCommand.Execute(cursor);
        await model.Opening.WaitAsync(_connect, Token);
        var first = model.Connection!;
        Assert.Same(first, cursor.Connection);

        model.ConnectAgainCommand.Execute(claude);
        await model.Opening.WaitAsync(_connect, Token);

        Assert.Null(cursor.Connection);
        Assert.Same(model.Connection, claude.Connection);
        Assert.True(model.Connection!.HasPreview);

        using var held = new ManualResetEventSlim();
        tools.Runner.HoldChanges = held;
        var running = model.Connection.ConfirmCommand.ExecuteAsync();

        Assert.True(model.Connection.IsBusy);
        Assert.False(model.ConnectCommand.CanExecute(cursor));
        Assert.False(model.DisconnectCommand.CanExecute(gemini));
        Assert.False(model.ConnectAgainCommand.CanExecute(claude));
        Assert.False(model.CloseConnectionCommand.CanExecute(null));

        held.Set();
        await running.WaitAsync(_connect, Token);
        await model.Looking.WaitAsync(_connect, Token);

        Assert.StartsWith("Connected.", model.Connection.Message, StringComparison.Ordinal);
        Assert.True(model.ConnectCommand.CanExecute(cursor));
        Assert.True(model.CloseConnectionCommand.CanExecute(null));
        Assert.True(Probes(tools) > 3, "the tools were not looked for again after the plan ran");

        model.CloseConnectionCommand.Execute(null);

        Assert.Null(model.Connection);
        Assert.Null(claude.Connection);
    }

    private static AgentRow Row(AgentActivityViewModel model, string key) => model.Rows.Single(row => row.Key == key);

    private static int Probes(FakeTools tools) => tools.Runner.Calls.Count(call => call.EndsWith(" --version", StringComparison.Ordinal));

    private static string Everything(AgentActivityViewModel model) =>
        string.Join(
            '\n',
            [
                model.Status, model.Unavailable, model.History, model.HistoryMessage,
                .. model.Waiting.Concat(model.Grants).Select(row => $"{row.Who} {row.What} {row.Left}"),
            ]);

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
                    TtlSeconds = 60,
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
                    Args = AuditArgs.ForCredentialRequest(entry, "password", 60, "deploy"),
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

        public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken) => HoldAsync(cancellationToken);

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
