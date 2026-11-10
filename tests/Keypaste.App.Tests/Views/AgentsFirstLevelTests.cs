using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.ViewModels;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// Agents' first level, what it shows with no Connect pane open, holds at most twelve kinds of
/// control over every state it can be in, a control repeated on every row counted once.
/// </summary>
/// <remarks>
/// A kind is a control's type and name. Only controls Agents' own markup and row templates declare are
/// counted, so a template's parts, such as a disclosure's header toggle or a scroll bar's arrows, are not.
/// </remarks>
public sealed class AgentsFirstLevelTests
{
    private const int _most = 12;

    /// <summary>The app's narrowest window less its 232 px sidebar: the width Agents is laid out in.</summary>
    private const double _narrowest = 960 - 232;

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static readonly string[] _kinds =
    [
        "Button/AddAnAgent", "ListBox/AgentRows", "Button/OpenActivity", "Button/ShowPrompt", "Button/BackToList",
        "Button/ConnectTool", "Button/ConnectToolAgain", "Button/DisconnectTool", "ChoiceSegment/RunChoice",
        "ChoiceSegment/CategoryChoice", "ChoiceSegment/RuleChoice", "Button/RemoveAnswer",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task The_list_holds_a_tool_not_connected_and_choosing_it_offers_Connect() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync();
        var model = agents.Model;
        var cursor = model.Rows.Single(row => row.Key == "cursor");

        model.Choose(model.Rows.Single(row => row.Key == "claude-code"));
        agents.Settle();
        Assert.DoesNotContain(agents.Window.GetVisualDescendants().OfType<Button>(), button => button is { Name: "ConnectTool", IsEffectivelyVisible: true });

        model.Choose(cursor);
        agents.Settle();
        Assert.Contains(agents.Window.GetVisualDescendants().OfType<Button>(), button => button is { Name: "ConnectTool", IsEffectivelyVisible: true } && button.DataContext == cursor);
    });

    [Fact]
    public Task Add_MCP_manually_shows_the_configuration_in_the_detail() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync();
        var model = agents.Model;

        model.AddMcpManuallyCommand.Execute(null);

        Assert.True(model.ShowsManualConfig);
        Assert.Null(model.Selected);
        Assert.Contains("\"keypaste\"", model.ManualConfigText, StringComparison.Ordinal);
    });

    [Fact]
    public Task Agents_first_level_holds_at_most_twelve_kinds_of_control() => HeadlessSession.On(async () =>
    {
        SortedSet<string> kinds = new(StringComparer.Ordinal);
        await EveryStateAsync(window => kinds.UnionWith(Operable(window).Select(Kind)));

        Assert.True(kinds.Count <= _most, $"Agents' first level holds {kinds.Count} kinds of control: {string.Join(", ", kinds)}");
        Assert.Superset(new HashSet<string>(_kinds, StringComparer.Ordinal), kinds);
    });

    /// <summary>A control with no name would slip past the count, and one a reader names by its type tells the person nothing.</summary>
    [Fact]
    public Task Every_control_a_person_can_operate_on_Agents_has_a_name() => HeadlessSession.On(async () =>
    {
        List<string> unnamed = [];
        await EveryStateAsync(window => unnamed.AddRange(
            Operable(window)
                .Where(control => string.IsNullOrEmpty(control.Name) || string.IsNullOrWhiteSpace(Said(control)))
                .Select(control => $"{control.GetType().Name} '{control.Name}' '{Said(control)}'")));

        Assert.True(unnamed.Count == 0, $"unnamed: {string.Join("; ", unnamed.Distinct())}");
    });

    /// <summary>
    /// The chosen agent's settings stay the same controls through the screen's refresh and an agent attaching, which lists
    /// it first, so what has the focus in them keeps it; and one Connect pane is open at a time, opening another closes the first.
    /// </summary>
    [Fact]
    public Task The_chosen_agents_settings_survive_a_refresh_and_Connect_opens_one_pane() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync();
        var model = agents.Model;
        var cursor = model.Rows.Single(row => row.Key == "cursor");
        var gemini = model.Rows.Single(row => row.Key == "gemini-cli");
        var claude = model.Rows.Single(row => row.Key == "claude-code");

        model.Choose(claude);
        agents.Settle();
        var panels = Panels(agents.Window);
        Assert.Single(panels);
        var controls = PanelControls(agents.Window);
        var focused = controls.Single(panel => panel.DataContext == claude.Settings).GetVisualDescendants().OfType<ToggleButton>().Single(button => AutomationProperties.GetName(button) == "On");
        focused.Focus();
        Assert.Same(focused, agents.Window.FocusManager?.GetFocusedElement());

        await using var another = await agents.AttachAsync(new AttachClient("zed", "1.0", "zed"));
        model.Refresh();
        agents.Clock.Advance(TimeSpan.FromSeconds(2));
        agents.Settle();

        Assert.Equal("zed", model.Rows[0].Key);
        Assert.Same(claude, model.Selected);
        Assert.Equal(panels, Panels(agents.Window));
        Assert.Equal(controls, PanelControls(agents.Window));
        Assert.Same(focused, agents.Window.FocusManager?.GetFocusedElement());

        model.ConnectCommand.Press(cursor);
        await model.Opening.WaitAsync(_wait, Token);
        model.ConnectCommand.Press(gemini);
        await model.Opening.WaitAsync(_wait, Token);
        agents.Settle();

        Assert.Null(cursor.Connection);
        Assert.NotNull(gemini.Connection);
        Assert.Same(gemini, model.Selected);
        Assert.Single(agents.Window.GetVisualDescendants().OfType<Button>(), button => button is { Name: "CloseConnection", IsEffectivelyVisible: true });
    });

    /// <summary>A waiting request's Show the prompt stays the same button as they count down, so a press or the focus on one is not lost at a tick.</summary>
    [Fact]
    public Task Show_the_prompt_stays_the_same_button_as_it_counts_down() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync();
        agents.Person.Hold = true;
        var waiting = agents.RequestAsync("notes");
        await UntilAsync(() => agents.Authority.Activity.Waiting.Count == 1);
        agents.Settle();
        var show = Shown(agents.Window, "ShowPrompt");
        var left = Assert.Single(agents.Model.Waiting).LeftText;
        show.Focus();
        Assert.Same(show, agents.Window.FocusManager?.GetFocusedElement());

        agents.Clock.Advance(TimeSpan.FromSeconds(5));
        agents.Settle();

        Assert.NotEqual(left, Assert.Single(agents.Model.Waiting).LeftText);
        Assert.Same(show, Shown(agents.Window, "ShowPrompt"));
        Assert.Same(show, agents.Window.FocusManager?.GetFocusedElement());

        agents.Clock.Advance(TimeSpan.FromMinutes(1));
        await waiting.WaitAsync(_wait, Token);
    });

    /// <summary>
    /// Drives Agents through every state it can be in, calling <paramref name="look"/> in each with every
    /// disclosure and pane closed: tools found in each state, an agent of no tool, one with no name and Every other
    /// agent; history; a request waiting; a change waiting; an unusable clients.toml; not serving.
    /// </summary>
    internal static async Task EveryStateAsync(Action<Window> look)
    {
        await using var agents = await Agents.StartAsync();
        await using var zed = await agents.AttachAsync(new AttachClient("Zed", "1.0", null));
        agents.Settle();
        look(agents.Window);

        await agents.RequestAsync("password");
        agents.Settle();
        look(agents.Window);

        agents.Person.Hold = true;
        var waiting = agents.RequestAsync("notes");
        await UntilAsync(() => agents.Authority.Activity.Waiting.Count == 1);
        agents.Settle();
        Assert.True(agents.Model.HasWaiting, "no request waiting");
        look(agents.Window);
        agents.Clock.Advance(TimeSpan.FromMinutes(1));
        await waiting.WaitAsync(_wait, Token);

        File.WriteAllText(KeypasteHome.AgentsPath(agents.Fixture.Home), "[[rules]]\npassword = \"sometimes\"\n");
        agents.Settle();
        Assert.True(agents.Model.HasClientsProblem, "agents.toml was read");
        look(agents.Window);

        agents.Authority.Session.Lock(VaultLockReason.Manual);
        agents.Settle();
        Assert.True(agents.Model.IsUnavailable, "the locked app still serves");
        look(agents.Window);
    }

    /// <summary>What a person can operate on the first level: visible, declared by Agents rather than by a control's template.</summary>
    private static IEnumerable<Control> Operable(Window window) =>
        window.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control is Button or TextBox or ComboBox or ListBox or Slider or Expander or SelectableTextBlock
                && control.TemplatedParent is null
                && control.IsEffectivelyVisible);

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(_wait);

        while (!condition())
        {
            await Task.Delay(20, deadline.Token);
        }
    }

    private static string Kind(Control control) => $"{control.GetType().Name}/{control.Name}";

    /// <summary>What a screen reader is told the control is called, or nothing when that is only a type's name.</summary>
    private static string Said(Control control)
    {
        var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();

        return name.StartsWith("Avalonia.", StringComparison.Ordinal) || name.StartsWith("Keypaste.", StringComparison.Ordinal)
            ? string.Empty
            : name;
    }

    private static List<object?> Panels(Window window) => [.. PanelControls(window).Select(panel => panel.DataContext)];

    private static List<AgentSettingsView> PanelControls(Window window) =>
        [.. window.GetVisualDescendants().OfType<AgentSettingsView>().Where(panel => panel.IsEffectivelyVisible)];

    private static Button Shown(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name && button.IsEffectivelyVisible);

    /// <summary>Agents in a window over a serving authority, its tools found on a computer of the test's own.</summary>
    internal sealed class Agents : IAsyncDisposable
    {
        private readonly List<ApproverClient> _clients = [];

        private Agents(TempVault fixture, ManualClock clock, Person person, AppAuthority authority, AgentActivityViewModel model, Window window)
        {
            Fixture = fixture;
            Clock = clock;
            Person = person;
            Authority = authority;
            Model = model;
            Window = window;
        }

        internal TempVault Fixture { get; }

        internal ManualClock Clock { get; }

        internal Person Person { get; }

        internal AppAuthority Authority { get; }

        internal AgentActivityViewModel Model { get; }

        internal Window Window { get; }

        /// <param name="width">The window's width; the default leaves every row room.</param>
        internal static async Task<Agents> StartAsync(double width = 1280)
        {
            var fixture = new TempVault();
            fixture.Sees();
            var clock = new ManualClock(AppClock.Start);
            var person = new Person();
#pragma warning disable CA2000 // The authority owns and disposes its session; the screen and window are disposed with the fixture.
            var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => person);

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
            }

            var tools = new FakeTools(Path.Combine(fixture.Home, "computer")).ClaudeCode(connected: true).Cursor(connected: false).GeminiUnreadable().VsCodeMoved();
            var model = new AgentActivityViewModel(authority, fixture.Home, clock, action => Dispatcher.UIThread.Post(action), tools.Connector(), openActivity: () => { });
#pragma warning restore CA2000
            var window = new Window { Width = width, Height = 900, Content = new AgentActivityView { DataContext = model } };
            window.Show();
            await model.Looking.WaitAsync(_wait, Token);

            var agents = new Agents(fixture, clock, person, authority, model, window);
            model.ConnectAnAppCommand.Execute(null);
            agents.Settle();
            return agents;
        }

        /// <summary>Attaches an agent to the app's session, as a bridge does.</summary>
        internal async Task<ApproverClient> AttachAsync(AttachClient identity)
        {
            var serving = Assert.IsType<AuthorityStatus.Serving>(Authority.Status);
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _wait, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(Fixture.Path_) { Client = identity }, Token))!.Attached);
            return client;
        }

        /// <summary>Asks for the example item's field as <see cref="TempVault.Agent"/>, which the person answers as told.</summary>
        internal async Task<CredentialReply?> RequestAsync(string field)
        {
            if (_clients.Count == 0)
            {
                _clients.Add(await AttachAsync(new AttachClient("claude-code", "1.0", TempVault.Agent)));
            }

            var serving = Assert.IsType<AuthorityStatus.Serving>(Authority.Status);
            return await _clients[0].RequestAsync(
                new CredentialRequest
                {
                    Entry = "example",
                    Field = field,
                    Reason = "first level",
                    Exposure = ["**"],
                    ClientName = "claude-code",
                    ClientLabel = TempVault.Agent,
                    Vault = Fixture.Path_,
                    Session = serving.Session,
                },
                Token);
        }

        /// <summary>Reads the screen again and lays the window out, as the next tick would.</summary>
        internal void Settle()
        {
            Model.Refresh();
            WindowInput.Drain();
            Window.UpdateLayout();
            WindowInput.Drain();
        }

        public async ValueTask DisposeAsync()
        {
            Window.Close();
            Model.Dispose();

            foreach (var client in _clients)
            {
                await client.DisposeAsync();
            }

            Authority.Dispose();
            Fixture.Dispose();
        }
    }

    /// <summary>The person at the prompt: allows every request until told to hold, then leaves each waiting until it is withdrawn.</summary>
    internal sealed class Person : IApprovalChannel
    {
        internal bool Hold { get; set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) => AnswerAsync(cancellationToken);

        private async ValueTask<ApprovalAnswer> AnswerAsync(CancellationToken cancellationToken)
        {
            if (!Hold)
            {
                return ApprovalAnswer.ApprovedOnce;
            }

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
