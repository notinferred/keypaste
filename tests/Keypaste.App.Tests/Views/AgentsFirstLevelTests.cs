using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.ViewModels;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// Agents' first level, what it shows with every disclosure and pane closed, holds at most twelve kinds of
/// control over every state it can be in, a control repeated on every row counted once (D-0455).
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
        "Button/AgentHistory", "Button/ShowPrompt", "Button/EndAllowance", "Button/EndAllAllowances", "Button/ConnectTool",
        "Expander/AgentSettings", "Button/ResetClients", "SelectableTextBlock/SessionHistory",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    /// One Connect pane is open at a time, and opening another closes the first; any number of Settings stay open
    /// through the screen's refresh and an agent attaching, which lists it first, as the same controls, so what has
    /// the focus in them keeps it.
    /// </summary>
    [Fact]
    public Task Connect_opens_one_pane_and_Settings_panels_survive_a_refresh() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync();
        var model = agents.Model;
        var cursor = model.Rows.Single(row => row.Key == "cursor");
        var gemini = model.Rows.Single(row => row.Key == "gemini-cli");
        var claude = model.Rows.Single(row => row.Key == "claude-code");

        Disclosure(agents.Window, claude).IsExpanded = true;
        Disclosure(agents.Window, AgentNames.Any, model).IsExpanded = true;
        agents.Settle();
        var panels = Panels(agents.Window);
        Assert.Equal(2, panels.Count);
        var controls = PanelControls(agents.Window);
        var focused = controls.Single(panel => panel.DataContext == claude.Settings).GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Advanced: patterns");
        focused.Focus();
        Assert.Same(focused, agents.Window.FocusManager?.GetFocusedElement());

        model.ConnectCommand.Execute(cursor);
        await model.Opening.WaitAsync(_wait, Token);
        model.ConnectCommand.Execute(gemini);
        await model.Opening.WaitAsync(_wait, Token);
        agents.Settle();

        Assert.Null(cursor.Connection);
        Assert.NotNull(gemini.Connection);
        Assert.Single(agents.Window.GetVisualDescendants().OfType<Button>(), button => button is { Name: "CloseConnection", IsEffectivelyVisible: true });

        await using var another = await agents.AttachAsync(new AttachClient("zed", "1.0", "zed"));
        model.Refresh();
        agents.Clock.Advance(TimeSpan.FromSeconds(2));
        agents.Settle();

        Assert.Equal("zed", model.Rows[0].Key);
        Assert.True(Disclosure(agents.Window, claude).IsExpanded);
        Assert.Equal(panels, Panels(agents.Window));
        Assert.Equal(controls, PanelControls(agents.Window));
        Assert.Same(focused, agents.Window.FocusManager?.GetFocusedElement());
        Assert.NotNull(gemini.Connection);
    });

    /// <summary>A waiting request's Show the prompt and an allowance's End stay the same buttons as they count down, so a press or the focus on one is not lost at a tick.</summary>
    [Fact]
    public Task Show_the_prompt_and_End_stay_the_same_buttons_as_they_count_down() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync();
        await agents.RequestAsync("password");
        agents.Person.Hold = true;
        var waiting = agents.RequestAsync("username");
        await UntilAsync(() => agents.Authority.Activity.Waiting.Count == 1);
        agents.Settle();
        var show = Shown(agents.Window, "ShowPrompt");
        var end = Shown(agents.Window, "EndAllowance");
        var left = Assert.Single(agents.Model.Waiting).LeftText;
        show.Focus();
        Assert.Same(show, agents.Window.FocusManager?.GetFocusedElement());

        agents.Clock.Advance(TimeSpan.FromSeconds(5));
        agents.Settle();

        Assert.NotEqual(left, Assert.Single(agents.Model.Waiting).LeftText);
        Assert.Same(show, Shown(agents.Window, "ShowPrompt"));
        Assert.Same(end, Shown(agents.Window, "EndAllowance"));
        Assert.Same(show, agents.Window.FocusManager?.GetFocusedElement());

        agents.Clock.Advance(TimeSpan.FromMinutes(1));
        await waiting.WaitAsync(_wait, Token);
    });

    /// <summary>
    /// In the app's narrowest window, an open Settings disclosure with its limits and patterns shown keeps every
    /// control inside the table that holds it, which clips whatever reaches past its edge.
    /// </summary>
    [Fact]
    public Task An_open_Settings_panel_fits_the_narrowest_window() => HeadlessSession.On(async () =>
    {
        await using var agents = await Agents.StartAsync(_narrowest);
        Assert.True(AccessEdit.TryCreate(null, "automatic", null, null, null, null, null, reset: false, out var edit, out var error), error);
        Assert.Equal(AuditDecision.Granted, (await agents.Authority.ChangeAccessAsync("claude-code", edit, Token)).Decision);
        var claude = agents.Model.Rows.Single(row => row.Key == "claude-code");

        Disclosure(agents.Window, claude).IsExpanded = true;
        agents.Settle();
        var settings = Assert.IsType<AgentSettingsViewModel>(claude.Settings);
        settings.ToggleAdvancedCommand.Execute(null);
        agents.Settle();

        Assert.True(settings.OffersLimits, "no limits to show");
        var disclosure = Disclosure(agents.Window, claude);
        var table = disclosure.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("table"));
        var outside = disclosure.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control is Button or TextBox or ComboBox or CalendarDatePicker && control.TemplatedParent is null && control.IsEffectivelyVisible)
            .Where(control => control.TranslatePoint(new Point(control.Bounds.Width, 0), table) is not { } edge || edge.X > table.Bounds.Width)
            .Select(control => $"{control.GetType().Name} '{Said(control)}'")
            .ToList();

        Assert.Contains(disclosure.GetVisualDescendants().OfType<ComboBox>(), box => AutomationProperties.GetName(box) == "Where the limit applies" && box.IsEffectivelyVisible);
        Assert.True(outside.Count == 0, $"cut off at {_narrowest} px: {string.Join("; ", outside)}");
    });

    /// <summary>
    /// Drives Agents through every state it can be in, calling <paramref name="look"/> in each with every
    /// disclosure and pane closed: tools found in each state, an agent of no tool, one with no name and Every other
    /// agent; an allowance with history; a request waiting; a change waiting; an unusable clients.toml; not serving.
    /// </summary>
    private static async Task EveryStateAsync(Action<Window> look)
    {
        await using var agents = await Agents.StartAsync();
        await using var zed = await agents.AttachAsync(new AttachClient("Zed", "1.0", null));
        agents.Settle();
        look(agents.Window);

        await agents.RequestAsync("password");
        agents.Settle();
        Assert.True(agents.Model.HasGrants && agents.Model.HasHistory, "no allowance or history to show");
        look(agents.Window);

        agents.Person.Hold = true;
        var waiting = agents.RequestAsync("username");
        await UntilAsync(() => agents.Authority.Activity.Waiting.Count == 1);
        agents.Settle();
        Assert.True(agents.Model.HasWaiting, "no request waiting");
        look(agents.Window);
        agents.Clock.Advance(TimeSpan.FromMinutes(1));
        await waiting.WaitAsync(_wait, Token);

        Assert.True(AccessEdit.TryCreate(null, "automatic", null, null, null, null, null, reset: false, out var edit, out var error), error);
        using var givingUp = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var changing = agents.Authority.ChangeAccessAsync(TempVault.Agent, edit, givingUp.Token).AsTask();
        await UntilAsync(() => agents.Authority.Activity.WaitingChanges.Count == 1);
        agents.Settle();
        Assert.True(agents.Model.HasWaiting, "no change waiting");
        look(agents.Window);
        await givingUp.CancelAsync();
        await Task.WhenAny(changing, Task.Delay(_wait, Token));

        File.WriteAllText(KeypasteHome.ClientsPath(agents.Fixture.Home), "[[client]]\nlabel = \"x\"\npolicy = \"ask\"\n");
        agents.Settle();
        Assert.True(agents.Model.HasClientsProblem, "clients.toml was read");
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

    private static string? Said(Control control) =>
        AutomationProperties.GetName(control) is { Length: > 0 } name ? name : (control as ContentControl)?.Content as string;

    private static Expander Disclosure(Window window, AgentRow row) =>
        window.GetVisualDescendants().OfType<Expander>().Single(expander => expander.DataContext == row);

    private static Expander Disclosure(Window window, string key, AgentActivityViewModel model) =>
        Disclosure(window, model.Rows.Single(row => row.Key == key));

    private static List<object?> Panels(Window window) => [.. PanelControls(window).Select(panel => panel.DataContext)];

    private static List<AgentSettingsView> PanelControls(Window window) =>
        [.. window.GetVisualDescendants().OfType<AgentSettingsView>().Where(panel => panel.IsEffectivelyVisible)];

    private static Button Shown(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name && button.IsEffectivelyVisible);

    /// <summary>Agents in a window over a serving authority, its tools found on a computer of the test's own.</summary>
    private sealed class Agents : IAsyncDisposable
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
            var model = new AgentActivityViewModel(authority, fixture.Home, clock, action => Dispatcher.UIThread.Post(action), tools.Connector(), openHistory: () => { });
#pragma warning restore CA2000
            var window = new Window { Width = width, Height = 900, Content = new AgentActivityView { DataContext = model } };
            window.Show();
            await model.Looking.WaitAsync(_wait, Token);

            var agents = new Agents(fixture, clock, person, authority, model, window);
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
                    TtlSeconds = 60,
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
    private sealed class Person : IApprovalChannel
    {
        internal bool Hold { get; set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) => AnswerAsync(cancellationToken);

        public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken) => AnswerAsync(cancellationToken);

        private async ValueTask<ApprovalAnswer> AnswerAsync(CancellationToken cancellationToken)
        {
            if (!Hold)
            {
                return ApprovalAnswer.Approved;
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
