using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
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
using Keypaste.Core.Tests;
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
    internal const string TaggedEntryPath = "services/Stripe";
    internal const string Label = "ci-probe";
    internal const string ApiKeySentinel = "SENTINEL-DESKTOP-OPENAI-7d2e4b";
    internal const string OpenAiPath = "api/OpenAI";

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

            await app.ArmAsync();
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
            await app.ArmAsync();

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
            var channel = new WindowApprovalChannel(new ManualClock(AppClock.Start), ApprovalLimits.Default.Window);
            var drawn = 0;
            channel.Shown += (_, _) => drawn++;
            using var withdrawn = new CancellationTokenSource();
            var prompt = ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 60);

            var asking = channel.AskAsync(prompt, withdrawn.Token).AsTask();
            withdrawn.Cancel();
            WindowInput.Drain();

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
            Assert.Equal(0, drawn);
        });

    /// <summary>
    /// A withdrawal requested before the prompt is drawn stops the draw while its callback still
    /// waits on the thread pool, where <c>CancelAsync</c>, which the gate withdraws with, runs it (F.20).
    /// </summary>
    [Fact]
    public Task A_withdrawal_requested_before_the_draw_stops_it_before_its_callback_runs() =>
        HeadlessSession.On(async () =>
        {
            var channel = new WindowApprovalChannel(new ManualClock(), ApprovalLimits.Default.Window);
            var drawn = 0;
            channel.Shown += (_, _) => drawn++;
            using var withdrawn = new CancellationTokenSource();
            var prompt = ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 60);
            var token = Token;

            var asking = channel.AskAsync(prompt, withdrawn.Token).AsTask();

            // Registered after the channel's callback, so it runs first and holds the channel's back.
            using var held = new ManualResetEventSlim();
            using var holding = withdrawn.Token.Register(() => held.Wait(_wait, token));
            var cancelling = withdrawn.CancelAsync();
            WindowInput.Drain();
            held.Set();
            await cancelling;
            WindowInput.Drain();

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
            Assert.Equal(0, drawn);
        });

    /// <summary>
    /// A bridge that hangs up while its prompt waits to be drawn keeps it off the screen, though the
    /// listener's <c>CancelAsync</c> leaves the tokens linked below the exchange's live until the
    /// thread pool runs its callbacks (F.32).
    /// </summary>
    [Fact]
    public Task A_bridge_that_hangs_up_before_the_draw_keeps_the_prompt_off_the_screen() =>
        HeadlessSession.On(async () =>
        {
            var clock = new ManualClock();
            var channel = new WindowApprovalChannel(clock, ApprovalLimits.Default.Window);
            var drawn = 0;
            channel.Shown += (_, _) => drawn++;
            var recorded = new HeldPrompt(channel, Token);
            using var gate = new ApprovalGate(recorded, clock, ApprovalLimits.Default);
            var handler = new HeldAtTheGate(gate, Token);
            var pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            using var listener = new ApproverListener(pipe, handler);
            using var stop = new CancellationTokenSource();
            var listening = listener.RunAsync(stop.Token);

            try
            {
                for (var i = 0; i < 100; i++)
                {
                    var asked = HangUpWhileTheShowJobIsQueued(pipe, handler);
                    var gateToken = recorded.Next().Gate;

                    // The UI thread runs its queue as soon as the listener withdraws, while the withdrawal is held above the gate's token.
                    Assert.True(SpinWait.SpinUntil(() => asked.Exchange.IsCancellationRequested, _wait), "the listener never withdrew the request");
                    WindowInput.Drain();
                    Assert.False(gateToken.IsCancellationRequested, _holdRanLate);
                    asked.Drained.Set();

                    Assert.Equal(ApprovalAnswer.Cancelled, await asked.Answer.WaitAsync(_wait, Token));
                    WindowInput.Drain();
                }
            }
            finally
            {
                await stop.CancelAsync();
                await listening;
            }

            Assert.Equal(0, drawn);
        });

    /// <summary>Sends one request and hangs up once the gate has queued its prompt, without letting the UI thread run.</summary>
    private static HeldAtTheGate.Asked HangUpWhileTheShowJobIsQueued(string pipe, HeldAtTheGate handler)
    {
        var request = new CredentialRequest
        {
            Entry = EntryPath,
            Field = "password",
            Reason = "deploy the billing service",
            TtlSeconds = 60,
            Exposure = ["env/**"],
            ClientName = "claude-code",
        };

        using var peer = new Peer(pipe);
        peer.Send(ApproverProtocol.Encode(request));
        return handler.Next();
    }

    public static TheoryData<string> PromptedKinds => new() { "credential", "env", "run" };

    /// <summary>
    /// Why a hold proved nothing: it waits only if it runs before the links below the token it holds,
    /// which .NET runs newest first without documenting the order.
    /// </summary>
    private const string _holdRanLate = "the withdrawal reached the gate's token before the UI thread ran its queue, so the hold ran after the links it had to precede";

    /// <summary>
    /// The app's own composition carries a bridge's hang-up to the prompt's show job along every path
    /// that asks a person: through <c>SessionAuthority</c> and <c>ApproverHandler</c> for a field, and
    /// through the env resolver for an env set and an agent's run.
    /// </summary>
    [Theory]
    [MemberData(nameof(PromptedKinds))]
    public Task A_hang_up_reaches_the_prompt_along_the_path_that_asked(string kind) =>
        HeadlessSession.On(async () =>
        {
            HeldPrompt? held = null;
            await using var app = await PromptedApp.StartAsync(prompt => held = new HeldPrompt(prompt, Token));
            var lifetime = app.Authority.Session.Lifetime!.Ended;
            var holding = held!;
            holding.Holding = withdrawals => withdrawals.FirstOrDefault(token => token != lifetime);
            HeldPrompt.Asked asked;

            using (var peer = app.Attach())
            {
                peer.Send(app.Frame(kind));
                asked = holding.Next();
            }

            // The UI thread runs its queue as soon as the hang-up reaches what the show job reads, while it is held above the gate's token.
            Assert.True(SpinWait.SpinUntil(() => asked.Held.IsCancellationRequested, _wait), "the show job reads nothing the hang-up cancels");
            WindowInput.Drain();
            Assert.False(asked.Gate.IsCancellationRequested, _holdRanLate);
            asked.Drained.SetResult();

            Assert.Equal(ApprovalAnswer.Denied, await asked.Answer.WaitAsync(_wait, Token));
            WindowInput.Drain();
            Assert.Empty(app.Windows);
        });

    /// <summary>
    /// A lock that ends the lifetime off the UI thread, as the idle timer's does, keeps a prompt not
    /// yet drawn off the screen, though the tokens linked below the lifetime's are cancelled only as
    /// its callbacks run.
    /// </summary>
    [Theory]
    [MemberData(nameof(PromptedKinds))]
    public Task A_lock_off_the_UI_thread_keeps_a_queued_prompt_off_the_screen(string kind) =>
        HeadlessSession.On(async () =>
        {
            HeldPrompt? held = null;
            await using var app = await PromptedApp.StartAsync(prompt => held = new HeldPrompt(prompt, Token));
            var lifetime = app.Authority.Session.Lifetime!.Ended;
            var holding = held!;
            holding.Holding = _ => lifetime;

            var released = app.ReleasedAsync(kind);
            var asked = holding.Next();
            var locking = Task.Run(() => app.Authority.Session.Lock(VaultLockReason.Idle), Token);

            // The UI thread runs its queue as soon as the lock ends the lifetime, while the withdrawal is held above the gate's token.
            Assert.True(SpinWait.SpinUntil(() => lifetime.IsCancellationRequested, _wait), "the lock never ended the lifetime");
            WindowInput.Drain();
            Assert.False(asked.Gate.IsCancellationRequested, _holdRanLate);
            asked.Drained.SetResult();

            await locking.WaitAsync(_wait, Token);
            Assert.False(await released.WaitAsync(_wait, Token));
            WindowInput.Drain();
            Assert.Empty(app.Windows);
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

            await app.ArmAsync();
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
            await app.ArmAsync();

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
            await app.ArmAsync();
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
    public Task A_custom_field_is_named_on_the_prompt_and_a_grant_for_it_serves_no_other_field() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask(entry: OpenAiPath, exposure: "api/**", field: "OPENAI_API_KEY");
            var window = await app.PromptAsync();

            Assert.Equal(OpenAiPath, Text(window, "EntryText"));
            Assert.Equal("OPENAI_API_KEY", Text(window, "FieldText"));
            await app.ArmAsync();
            Click(window, "AllowOnce");

            var once = await reply.WaitAsync(_wait, Token);
            Assert.Equal(ApiKeySentinel, once!.Value);
            await PromptedApp.WithdrawnAsync(window);

            var timed = app.Ask(entry: OpenAiPath, exposure: "api/**", field: "OPENAI_API_KEY");
            var second = await app.PromptAsync(count: 2);
            await app.ArmAsync();
            Click(second, "Approve");
            Assert.Equal(3600, (await timed.WaitAsync(_wait, Token))!.TtlSeconds);
            await PromptedApp.WithdrawnAsync(second);

            var password = app.Ask(entry: OpenAiPath, exposure: "api/**");
            var third = await app.PromptAsync(count: 3);
            Assert.Equal("password", Text(third, "FieldText"));
            Click(third, "Deny");

            var refused = await password.WaitAsync(_wait, Token);
            Assert.Equal(AuditDecision.Denied, refused!.Decision);
            Assert.Null(refused.Value);
            AutomationSurface.AssertNothingExposes(third, ApiKeySentinel);
        });

    [Fact]
    public Task AllowForTheHour_Releases_AndTheNextIsServedFromTheGrant() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask();
            var window = await app.PromptAsync();
            await app.ArmAsync();
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

            await app.ArmAsync();
            Click(window, button);

            Assert.Equal(AuditDecision.Granted, (await reply.WaitAsync(_wait, Token))!.Decision);
        });

    [Fact]
    public Task TheTimedButton_IsHidden_ForALiveOnlyEntry() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask(entry: TaggedEntryPath, exposure: "services/**");
            var window = await app.PromptAsync();

            Assert.False(window.FindControl<Button>("Approve")!.IsVisible);
            Assert.True(window.FindControl<Button>("AllowOnce")!.IsVisible);
            Assert.Equal("once only: protected profile", Text(window, "LifetimeText"));

            await app.ArmAsync();
            Click(window, "AllowOnce");
            var answered = await reply.WaitAsync(_wait, Token);

            Assert.Equal(AuditDecision.Granted, answered!.Decision);
            Assert.Equal(0, answered.TtlSeconds);
        });

    /// <summary>An entry under <c>env/…/prod</c> with no protecting tag of its own is an ordinary entry, offered the timed allow (D-0416).</summary>
    [Fact]
    public Task An_untagged_entry_under_a_prod_path_is_offered_the_timed_allow() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.Ask(entry: ProdEntryPath);
            var window = await app.PromptAsync();

            Assert.True(window.FindControl<Button>("Approve")!.IsVisible);
            Assert.Equal("once, or for 1 hour", Text(window, "LifetimeText"));

            await app.ArmAsync();
            Click(window, "Approve");
            var answered = await reply.WaitAsync(_wait, Token);

            Assert.Equal(AuditDecision.Granted, answered!.Decision);
            Assert.Equal(3600, answered.TtlSeconds);
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

    /// <summary>Puts every credential request to the gate, and holds the withdrawal a hang-up starts until the test lets it go.</summary>
    private sealed class HeldAtTheGate(ApprovalGate gate, CancellationToken testToken) : IApproverHandler
    {
        private readonly ConcurrentQueue<Asked> _asked = new();

        internal Asked Next()
        {
            Asked? asked = null;
            Assert.True(SpinWait.SpinUntil(() => _asked.TryDequeue(out asked), _wait), "the request never reached the gate");
            return asked!;
        }

        public async ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken)
        {
            var prompt = ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 60);

            // The gate links its token to the listener's and queues the show job before this returns.
            var answer = gate.AskAsync(connectionId, prompt, cancellationToken).AsTask();

            // Registered after the gate's link, so it runs first and holds the withdrawal back from the gate's token.
            using var drained = new ManualResetEventSlim();
            using var holding = cancellationToken.Register(() => drained.Wait(_wait, testToken));
            _asked.Enqueue(new Asked(drained, answer, cancellationToken));

            await answer;
            return new CredentialReply { Decision = AuditDecision.Denied, Method = AuditMethod.Cancelled, Reason = "withdrawn", TtlSeconds = 0 };
        }

        public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AttachReply.To("held"));

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new NamesReply(false, [], "not listed", true));

        public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EnvReply(EnvResolved.Refused(request.Project, EnvOutcome.NoSession), "not released"));

        public void Disconnected(string connectionId)
        {
        }

        /// <summary>A request at the gate: the hold on its withdrawal, the gate's answer, and the token the listener gave the handler.</summary>
        internal sealed record Asked(ManualResetEventSlim Drained, Task<ApprovalAnswer> Answer, CancellationToken Exchange);
    }

    /// <summary>
    /// The app's prompt, recording the token the gate gives each request, with one of the withdrawals
    /// its show job reads, if the test chooses one, held back from that token until the test lets it go.
    /// </summary>
    private sealed class HeldPrompt(IApprovalChannel prompt, CancellationToken testToken) : IApprovalChannel
    {
        private readonly ConcurrentQueue<Asked> _asked = new();

        /// <summary>Picks the token to hold from the withdrawals the show job reads.</summary>
        internal Func<IReadOnlyList<CancellationToken>, CancellationToken> Holding { get; set; } = _ => default;

        internal Asked Next()
        {
            Asked? asked = null;
            Assert.True(SpinWait.SpinUntil(() => _asked.TryDequeue(out asked), _wait), "the request never reached the prompt");
            return asked!;
        }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt request, CancellationToken cancellationToken) =>
            HeldAsync(prompt.AskAsync(request, cancellationToken).AsTask(), cancellationToken);

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt request, CancellationToken cancellationToken) =>
            HeldAsync(prompt.AskAsync(request, cancellationToken).AsTask(), cancellationToken);

        public ValueTask<ApprovalAnswer> AskAsync(RunPrompt request, CancellationToken cancellationToken) =>
            HeldAsync(prompt.AskAsync(request, cancellationToken).AsTask(), cancellationToken);

        private async ValueTask<ApprovalAnswer> HeldAsync(Task<ApprovalAnswer> answer, CancellationToken gate)
        {
            // The prompt has queued its show job and every link above the gate's token is registered, so this runs before them.
            var held = Holding(Withdrawals.Current);
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var holding = held.Register(() => drained.Task.Wait((int)_wait.TotalMilliseconds, testToken));
            _asked.Enqueue(new Asked(drained, answer, held, gate));

            return await answer;
        }

        /// <summary>A request at the prompt: the hold, the prompt's answer, the token held and the gate's token.</summary>
        internal sealed record Asked(TaskCompletionSource Drained, Task<ApprovalAnswer> Answer, CancellationToken Held, CancellationToken Gate);
    }

    /// <summary>A bridge's end of a pipe, driven a frame at a time without awaiting, so the UI thread runs nothing meanwhile; disposing it hangs up.</summary>
    internal sealed class Peer : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;

        internal Peer(string endpoint)
        {
            _pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            _pipe.Connect((int)_wait.TotalMilliseconds);
        }

        internal void Send(byte[] message)
        {
            _pipe.Write([.. message, (byte)'\n']);
            _pipe.Flush();
        }

        internal byte[] Receive()
        {
            var line = new List<byte>();

            for (var next = _pipe.ReadByte(); next != -1 && next != '\n'; next = _pipe.ReadByte())
            {
                line.Add((byte)next);
            }

            return [.. line];
        }

        public void Dispose() => _pipe.Dispose();
    }

    /// <summary>The app's authority, composed with its own prompt, holding a vault with one credential in it.</summary>
    internal sealed class PromptedApp : IAsyncDisposable
    {
        private readonly TempVault _fixture;
        private ApproverClient? _client;

        private PromptedApp(TempVault fixture, string vault, Func<IApprovalChannel, IApprovalChannel>? around)
        {
            _fixture = fixture;
            Vault = vault;

            // The authority owns the session, as it does at launch.
#pragma warning disable CA2000
            Authority = new AppAuthority(new AppVaultSession(Clock, home: fixture.Home), null, () =>
            {
                var channel = new WindowApprovalChannel(Clock, ApprovalLimits.Default.Window);
                channel.Shown += (_, window) => Windows.Add(window);
                return around is null ? channel : around(channel);
            });
#pragma warning restore CA2000
        }

        internal ManualClock Clock { get; } = new(AppClock.Start);

        internal AppAuthority Authority { get; }

        internal string Vault { get; }

        internal string SessionId { get; private set; } = string.Empty;

        internal List<Window> Windows { get; } = [];

        /// <param name="around">What the gate asks instead of the app's prompt, given that prompt.</param>
        internal static async Task<PromptedApp> StartAsync(Func<IApprovalChannel, IApprovalChannel>? around = null)
        {
            var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "agents.kdbx");

            using (var created = Core.Vault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = Sentinel });
                Assert.True(created.SetFields(new EntryName("env/ci", "DEPLOY_KEY"), [new FieldWrite("DEPLOY_KEY", Sentinel)]));
                created.AddTag(new EntryName("env/ci", "DEPLOY_KEY"), "env:ci");
                created.AddEntry(new VaultEntry { GroupPath = "env/ci/prod", Title = "DEPLOY_KEY", Password = Sentinel });
                created.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = Sentinel });
                created.AddTag(new EntryName("services", "Stripe"), "env:ci:prod");
                created.AddEntry(new VaultEntry { GroupPath = "api", Title = "OpenAI", Password = Sentinel });
                Assert.True(created.SetFields(new EntryName("api", "OpenAI"), [new FieldWrite("OPENAI_API_KEY", ApiKeySentinel)]));
                created.Save();
            }

            var app = new PromptedApp(fixture, vault, around);

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

        internal Task<CredentialReply?> Ask(CancellationToken? cancellationToken = null, string entry = EntryPath, string exposure = "env/**", string field = "password") =>
            Ask(_client!, cancellationToken, entry, exposure, field);

        internal Task<CredentialReply?> Ask(ApproverClient client, CancellationToken? cancellationToken = null, string entry = EntryPath, string exposure = "env/**", string field = "password") =>
            client.RequestAsync(Credential(entry, exposure, field), cancellationToken ?? Token).AsTask();

        internal CredentialRequest Credential(string entry = EntryPath, string exposure = "env/**", string field = "password") => new()
        {
            Entry = entry,
            Field = field,
            Reason = "deploy the billing service",
            TtlSeconds = 60,
            Exposure = [exposure],
            ClientName = "claude-code",
            ClientLabel = Label,
            Vault = Vault,
            Session = SessionId,
        };

        /// <summary>Waits for the prompt a request raised to be on screen and drawn.</summary>
        /// <param name="count">How many prompts this app has raised once that one is up.</param>
        internal async Task<Window> PromptAsync(int count = 1)
        {
            await Until(() => Windows.Count >= count);
            var window = Windows[^1];
            DrawnFrame.Capture(window);
            return window;
        }

        /// <summary>Lets the arming delay pass, as a person reading the prompt does, and waits for the prompt to arm.</summary>
        /// <remarks>The delay's continuation runs on the thread pool, so the arming is posted some time after the clock moves.</remarks>
        internal async Task ArmAsync()
        {
            Clock.Advance(PromptViewModel.ArmingDelay);
            await Until(() => Windows[^1].FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled);
        }

        internal static async Task WithdrawnAsync(Window window) => await Until(() => !window.IsVisible);

        /// <summary>A <c>keypaste run --session</c> request for the <c>ci</c> project on this bridge's connection.</summary>
        internal Task<EnvReply?> AskEnv(CancellationToken? cancellationToken = null) =>
            _client!.ReleaseEnvAsync(Env(), cancellationToken ?? Token).AsTask();

        internal EnvRequest Env() =>
            new("ci", ["deploy", "--to", "staging area"], Path.Combine(_fixture.Home, "work")) { Vault = Vault, Session = SessionId };

        /// <summary>An agent's run of the <c>ci</c> set on this bridge's connection.</summary>
        internal Task<RunReply?> AskRun(CancellationToken? cancellationToken = null) =>
            _client!.ReleaseRunAsync(Run(), cancellationToken ?? Token).AsTask();

        internal RunRequest Run() => new()
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
        };

        /// <summary>The frame a bridge sends for one kind of prompted request: <c>credential</c>, <c>env</c> or <c>run</c>.</summary>
        internal byte[] Frame(string kind) => kind switch
        {
            "credential" => ApproverProtocol.Encode(Credential()),
            "env" => ApproverProtocol.Encode(Env()),
            _ => ApproverProtocol.Encode(Run()),
        };

        /// <summary>Asks for one kind of prompted request on the attached bridge, and says whether anything was released.</summary>
        internal async Task<bool> ReleasedAsync(string kind) => kind switch
        {
            "credential" => (await Ask())?.Value is not null,
            "env" => (await AskEnv())?.Set.Outcome == EnvOutcome.Resolved,
            _ => (await AskRun())?.Set.Outcome == EnvOutcome.Resolved,
        };

        /// <summary>Another bridge, attached without awaiting anything, so the UI thread runs nothing meanwhile.</summary>
        internal Peer Attach()
        {
            var peer = new Peer(Assert.IsType<AuthorityStatus.Serving>(Authority.Status).Endpoint);
            peer.Send(ApproverProtocol.Encode(new AttachRequest(Vault)));
            Assert.True(ApproverProtocol.TryDecode(peer.Receive(), out AttachReply? attached) && attached.Attached, "the peer was not attached");
            return peer;
        }

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
