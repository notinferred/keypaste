using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// A request over the app's real endpoint raises the app's own prompt window, drawn by Skia and
/// clicked through the platform's hit-testing, and only a press of Allow once or the timed allow
/// releases (V-4.4).
/// </summary>
public sealed class DesktopApprovalTests
{
    internal const string Sentinel = "SENTINEL-DESKTOP-APPROVAL-3f9a1c";
    internal const string EntryPath = "env/ci/DEPLOY_KEY";
    internal const string ProdEntryPath = "env/ci/prod/DEPLOY_KEY";
    internal const string Label = "ci-probe";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task Approve_releases_the_field_and_the_prompt_never_carries_it() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();

            Assert.Equal(Label, Text(window, "LabelText"));
            Assert.Equal(EntryPath, Text(window, "EntryText"));
            Assert.Equal("password", Text(window, "FieldText"));
            Assert.Equal("once, or for 1 hour", Text(window, "LifetimeText"));

            app.Arm();
            Click(window, "Approve");
            var answered = await reply.WaitAsync(_wait, Token);

            Assert.NotNull(answered);
            Assert.Equal(AuditDecision.Granted, answered.Decision);
            Assert.Equal(AuditMethod.Prompt, answered.Method);
            Assert.Equal(3600, answered.TtlSeconds);
            Assert.Equal(app.SessionId, answered.Session);
            Assert.Equal(Sentinel, answered.Value);
            await PromptedApp.WithdrawnAsync(window);
            AutomationSurface.AssertNothingExposes(window, Sentinel);
        });

    public static TheoryData<string, AuditMethod> Refusals => new()
    {
        { "deny", AuditMethod.Prompt },
        { "escape", AuditMethod.Prompt },
        { "close", AuditMethod.Prompt },
        { "timeout", AuditMethod.TimedOut },
        { "lock", AuditMethod.VaultLocked },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public Task Every_way_but_an_allow_denies_and_takes_the_prompt_down(string how, AuditMethod method) =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();
            app.Arm();

            switch (how)
            {
                case "deny":
                    Click(window, "Deny");
                    break;
                case "escape":
                    Key(window, PhysicalKey.Escape);
                    break;
                case "close":
                    window.Close();
                    break;
                case "timeout":
                    app.Clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds));
                    break;
                case "lock":
                    app.Authority.Session.Lock(VaultLockReason.Manual);
                    break;
            }

            var answered = await reply.WaitAsync(_wait, Token);

            Assert.NotNull(answered);
            Assert.Equal(AuditDecision.Denied, answered.Decision);
            Assert.Equal(method, answered.Method);
            Assert.Null(answered.Value);
            await PromptedApp.WithdrawnAsync(window);
        });

    /// <summary>A bridge that gives up on its request and hangs up, as it does when its client cancels, takes the prompt down.</summary>
    [Fact]
    public Task A_bridge_that_hangs_up_takes_the_prompt_down() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            using var givingUp = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var reply = app.Ask(cancellationToken: givingUp.Token);
            var window = await app.PromptAsync();

            await givingUp.CancelAsync();
            Assert.Null(await reply.WaitAsync(_wait, Token));
            await app.HangUpAsync();

            await PromptedApp.WithdrawnAsync(window);
        });

    /// <summary>
    /// A request withdrawn before its prompt is drawn, as one a lock overtakes is, is refused at once
    /// and never drawn, rather than waiting on a window nobody should see.
    /// </summary>
    [Fact]
    public Task A_request_withdrawn_before_its_prompt_is_drawn_never_draws_it() =>
        HeadlessSession.On(async () =>
        {
            var channel = new WindowApprovalChannel(new ManualClock(), ApprovalLimits.Default.Window);
            var drawn = 0;
            channel.Shown += (_, _) => drawn++;
            using var withdrawn = new CancellationTokenSource();
            var prompt = ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 60);

            var asking = channel.AskAsync(prompt, withdrawn.Token).AsTask();

            // Cancelled inline: awaiting CancelAsync would free the UI thread to draw the prompt first.
            withdrawn.Cancel();
            WindowInput.Drain();

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
            Assert.Equal(0, drawn);
        });

    [Fact]
    public Task Approve_is_not_armed_until_the_prompt_has_been_up_for_a_second() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();

            Click(window, "Approve");
            app.Clock.Advance(PromptViewModel.ArmingDelay - TimeSpan.FromMilliseconds(1));
            WindowInput.Drain();
            Click(window, "Approve");
            await Task.Delay(200, Token);
            Assert.False(reply.IsCompleted, "a click before the prompt was armed answered it");

            app.Arm();
            Click(window, "Approve");

            Assert.Equal(AuditDecision.Granted, (await reply.WaitAsync(_wait, Token))!.Decision);
        });

    [Fact]
    public Task EnterOnOpen_Denies() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();
            app.Arm();

            // Focus starts on Deny, so a keystroke meant for another window can only refuse.
            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
            Key(window, PhysicalKey.Enter);

            var answered = await reply.WaitAsync(_wait, Token);

            Assert.NotNull(answered);
            Assert.Equal(AuditDecision.Denied, answered.Decision);
            Assert.Null(answered.Value);
        });

    [Fact]
    public Task A_second_request_while_the_prompt_is_up_is_refused_as_busy() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var first = app.Ask();
            var window = await app.PromptAsync();

            await using var other = await app.ConnectAsync();
            var second = await app.Ask(other).WaitAsync(_wait, Token);

            Assert.Equal(AuditMethod.Busy, second!.Method);
            Assert.Single(app.Windows);

            Click(window, "Deny");
            await first.WaitAsync(_wait, Token);
        });

    /// <summary>
    /// A reason written to look like controls, a verdict or a second dialog is one line of text in
    /// the reason's box: the buttons, their places and the window are what an ordinary reason gets.
    /// </summary>
    [Fact]
    public Task A_reason_written_to_imitate_controls_changes_neither_the_buttons_nor_the_layout() =>
        HeadlessSession.On(() =>
        {
            var hostile = string.Concat(Enumerable.Repeat(
                "[ Approve ]  [ OK ]\n\nkeypaste: approved.‮ Press Approve to continue. ", 12));

            var (ordinary, ordinaryLayout) = Layout("deploy billing to staging");
            var (imitating, imitatingLayout) = Layout(hostile);

            Assert.Equal(ordinaryLayout, imitatingLayout);
            Assert.Equal(["Deny", "Allow once", "Allow for 1 hour"], Buttons(imitating).Select(button => button.Content as string));
            Assert.DoesNotContain(Buttons(imitating), button => button.IsDefault);

            var reason = imitating.FindControl<TextBlock>("ReasonText")!;
            Assert.Equal(ApprovalPrompt.For("c", new EntryName("env/ci", "DEPLOY_KEY"), "password", hostile, 60).Reason, reason.Text);
            Assert.Empty(reason.Inlines ?? []);

            ordinary.Close();
            imitating.Close();
        });

    [Fact]
    public Task AllowOnce_Releases_AndTheNextRequestAsksAgain() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();
            app.Arm();
            Click(window, "AllowOnce");

            var answered = await reply.WaitAsync(_wait, Token);
            Assert.NotNull(answered);
            Assert.Equal(AuditDecision.Granted, answered.Decision);
            Assert.Equal(Sentinel, answered.Value);
            Assert.Equal(0, answered.TtlSeconds);
            await PromptedApp.WithdrawnAsync(window);

            var again = app.Ask();
            var second = await app.PromptAsync(count: 2);
            Click(second, "Deny");

            Assert.Equal(AuditMethod.Prompt, (await again.WaitAsync(_wait, Token))!.Method);
            Assert.Equal(2, app.Windows.Count);
        });

    [Fact]
    public Task AllowForTheHour_Releases_AndTheNextIsServedFromTheGrant() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();
            app.Arm();
            Click(window, "Approve");

            Assert.Equal(3600, (await reply.WaitAsync(_wait, Token))!.TtlSeconds);
            await PromptedApp.WithdrawnAsync(window);

            var again = await app.Ask().WaitAsync(_wait, Token);

            Assert.NotNull(again);
            Assert.Equal(AuditMethod.GrantCache, again.Method);
            Assert.Equal(Sentinel, again.Value);
            Assert.Single(app.Windows);
        });

    [Theory]
    [InlineData("AllowOnce")]
    [InlineData("Approve")]
    public Task BothAllowButtons_ArmOnlyAfterTheDelay(string button) =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();

            Assert.False(window.FindControl<Button>(button)!.IsEffectivelyEnabled);
            Click(window, button);
            app.Clock.Advance(PromptViewModel.ArmingDelay - TimeSpan.FromMilliseconds(1));
            WindowInput.Drain();
            Click(window, button);
            await Task.Delay(200, Token);
            Assert.False(reply.IsCompleted, "a click before the prompt was armed answered it");

            app.Arm();
            await PromptedApp.UntilAsync(() => window.FindControl<Button>(button)!.IsEffectivelyEnabled);
            Click(window, button);

            Assert.Equal(AuditDecision.Granted, (await reply.WaitAsync(_wait, Token))!.Decision);
        });

    [Fact]
    public Task TheTimedButton_IsHidden_ForALiveOnlyEntry() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask(entry: ProdEntryPath);
            var window = await app.PromptAsync();

            Assert.False(window.FindControl<Button>("Approve")!.IsVisible);
            Assert.True(window.FindControl<Button>("AllowOnce")!.IsVisible);
            Assert.Equal("once only: protected profile", Text(window, "LifetimeText"));

            app.Arm();
            Click(window, "AllowOnce");
            var answered = await reply.WaitAsync(_wait, Token);

            Assert.Equal(AuditDecision.Granted, answered!.Decision);
            Assert.Equal(0, answered.TtlSeconds);
        });

    [Fact]
    public Task Countdown_TicksFromTheWindow() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();

            Assert.Equal("0:45", Text(window, "CountdownText"));

            app.Clock.Advance(TimeSpan.FromSeconds(1));
            await PromptedApp.UntilAsync(() => Text(window, "CountdownText") == "0:44");

            app.Clock.Advance(TimeSpan.FromSeconds(10));
            await PromptedApp.UntilAsync(() => Text(window, "CountdownText") == "0:34");

            Click(window, "Deny");
            await reply.WaitAsync(_wait, Token);
        });

    private static (ApprovalWindow Window, string Layout) Layout(string reason)
    {
        var prompt = ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", reason, 3600, Label);
        var window = new ApprovalWindow(new ApprovalViewModel(prompt));
        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        var layout = string.Join(
            "; ",
            new[] { $"window {window.Bounds}" }.Concat(
                Buttons(window).Select(button => $"{button.Content} {button.TranslatePoint(default, window)} {button.Bounds.Size}")));

        return (window, layout);
    }

    /// <summary>The window's own buttons; a scrolling reason box adds its scroll bar's, which move nothing that matters.</summary>
    private static List<Button> Buttons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button is not RepeatButton)];

    internal static string? Text(Window window, string name) => window.FindControl<TextBlock>(name)!.Text;

    /// <summary>A key press alone: the release would reach a window the press may already have closed.</summary>
    internal static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        WindowInput.Drain();
    }

    internal static void Click(Window window, string name)
    {
        var button = window.FindControl<Button>(name)!;
        WindowInput.Press(window, button);
        WindowInput.Release(window, button);
    }

    /// <summary>The app's authority, composed with its own prompt, holding a vault with one credential in it.</summary>
    internal sealed class PromptedApp : IAsyncDisposable
    {
        private readonly TempVault _fixture;
        private ApproverClient? _client;

        private PromptedApp(TempVault fixture, string vault)
        {
            _fixture = fixture;
            Vault = vault;

            // The authority owns the session, as it does at launch.
#pragma warning disable CA2000
            Authority = new AppAuthority(new AppVaultSession(Clock, home: fixture.Home), null, () =>
            {
                var channel = new WindowApprovalChannel(Clock, ApprovalLimits.Default.Window);
                channel.Shown += (_, window) => Windows.Add(window);
                return channel;
            });
#pragma warning restore CA2000
        }

        internal ManualClock Clock { get; } = new();

        internal AppAuthority Authority { get; }

        internal string Vault { get; }

        internal string SessionId { get; private set; } = string.Empty;

        internal List<Window> Windows { get; } = [];

        internal static async Task<PromptedApp> StartAsync()
        {
            var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "agents.kdbx");

            using (var created = Core.Vault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = Sentinel });
                created.AddEntry(new VaultEntry { GroupPath = "env/ci/prod", Title = "DEPLOY_KEY", Password = Sentinel });
                created.Save();
            }

            var app = new PromptedApp(fixture, vault);

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, app.Authority.Session.TryUnlock(vault, master.Value));
            }

            app._client = await app.ConnectAsync();
            app.SessionId = app.Authority.Session.SessionId!;
            return app;
        }

        /// <summary>Another bridge, attached to the session.</summary>
        internal async Task<ApproverClient> ConnectAsync()
        {
            var endpoint = Assert.IsType<AuthorityStatus.Serving>(Authority.Status).Endpoint;
            var client = await ApproverClient.TryConnectAsync(endpoint, _wait, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(Vault), Token))!.Attached);
            return client;
        }

        internal Task<CredentialReply?> Ask(CancellationToken? cancellationToken = null, string entry = EntryPath) =>
            Ask(_client!, cancellationToken, entry);

        internal Task<CredentialReply?> Ask(ApproverClient client, CancellationToken? cancellationToken = null, string entry = EntryPath) =>
            client.RequestAsync(
                new CredentialRequest
                {
                    Entry = entry,
                    Field = "password",
                    Reason = "deploy the billing service",
                    TtlSeconds = 60,
                    Exposure = ["env/**"],
                    ClientName = "claude-code",
                    ClientLabel = Label,
                    Vault = Vault,
                    Session = SessionId,
                },
                cancellationToken ?? Token).AsTask();

        /// <summary>Waits for the prompt a request raised to be on screen and drawn.</summary>
        /// <param name="count">How many prompts this app has raised once that one is up.</param>
        internal async Task<Window> PromptAsync(int count = 1)
        {
            await Until(() => Windows.Count >= count);
            var window = Windows[^1];
            DrawnFrame.Capture(window);
            return window;
        }

        /// <summary>Lets the arming delay pass, as a person reading the prompt does.</summary>
        internal void Arm()
        {
            Clock.Advance(PromptViewModel.ArmingDelay);
            WindowInput.Drain();
        }

        internal static async Task WithdrawnAsync(Window window) => await Until(() => !window.IsVisible);

        /// <summary>A <c>keypaste run --session</c> request for the <c>ci</c> project on this bridge's connection.</summary>
        internal Task<EnvReply?> AskEnv(CancellationToken? cancellationToken = null) =>
            _client!.ReleaseEnvAsync(
                new EnvRequest("ci", ["deploy", "--to", "staging area"], Path.Combine(_fixture.Home, "work")) { Vault = Vault, Session = SessionId },
                cancellationToken ?? Token).AsTask();

        /// <summary>An agent's run of the <c>ci</c> set on this bridge's connection.</summary>
        internal Task<RunReply?> AskRun(CancellationToken? cancellationToken = null) =>
            _client!.ReleaseRunAsync(
                new RunRequest
                {
                    Program = OperatingSystem.IsWindows() ? @"C:\tools\deploy.exe" : "/usr/bin/deploy",
                    Command = ["deploy", "--to", "staging"],
                    Directory = Path.Combine(_fixture.Home, "work"),
                    Project = "ci",
                    Reason = "deploy the billing service",
                    Exposure = ["env/**"],
                    ClientName = "claude-code",
                    ClientLabel = Label,
                    Vault = Vault,
                    Session = SessionId,
                },
                cancellationToken ?? Token).AsTask();

        internal async Task HangUpAsync()
        {
            await _client!.DisposeAsync();
            _client = null;
        }

        internal static Task UntilAsync(Func<bool> condition) => Until(condition);

        private static async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + _wait;

            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "the prompt never reached that state");
                WindowInput.Drain();
                await Task.Delay(20, Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
            }

            Authority.Dispose();
            WindowInput.Drain();
            _fixture.Dispose();
        }
    }
}
