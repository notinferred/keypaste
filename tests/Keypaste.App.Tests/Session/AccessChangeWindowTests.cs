using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// A change that would give an agent more is confirmed in the app's own prompt window: the agent, what
/// it gains and where the request says it came from, said as a claim; Deny focused, Allow armed after a
/// moment, and every way but Allow a refusal (D-0326, THREATS.md T-2).
/// </summary>
public sealed class AccessChangeWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheWindow_SaysWhoGainsWhat_AndWhereTheRequestClaimsItCameFrom() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Prompt(AccessOrigin.Terminal, "so deploys stop asking"));
            var window = Assert.IsType<AccessChangeWindow>(await screen.ShownAsync());

            Assert.Equal("ci-probe", Text(window, "AgentText"));
            Assert.Equal("asked from a terminal; any program you run could have typed it", Text(window, "OriginText"));
            Assert.Equal(
                ["may run commands, each still asked about", "by default: Automatic: released without asking, recorded in history"],
                Gains(window));
            Assert.Equal("so deploys stop asking", Text(window, "ReasonText"));
            Assert.Equal("The request says it is needed because", Text(window, "ReasonCaptionText"));
            Assert.Equal("That sentence was not written by keypaste. Treat it as a claim.", Text(window, "AttributionText"));
            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
            Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);
            Assert.DoesNotContain(Buttons(window), button => button.IsDefault);

            Click(window, "Deny");
            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    /// <summary>The window says when the agent gets what it allows: from its next request, or once the settings keypaste does not wholly understand are fixed.</summary>
    [Theory]
    [InlineData(false, "Allowing saves this in the vault, and the agent gets it from its next request. Allow only a change you expected.")]
    [InlineData(true, "Allowing saves this in the vault, but keypaste does not wholly understand this agent's saved settings, so the agent gets no more than the defaults until they are fixed. Allow only a change you expected.")]
    public Task TheWindow_SaysWhenTheAgentGetsTheChange(bool beneathDefaults, string said) =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Prompt(AccessOrigin.Settings, string.Empty) with { StaysBeneathDefaults = beneathDefaults });
            var window = await screen.ShownAsync();

            Assert.Equal(said, Text(window, "ExplainerText"));

            Click(window, "Deny");
            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    /// <summary>
    /// A change with no reason says where the person chose it, or that none was given, in the reason's
    /// box, with no caption calling it the request's reason and no line saying keypaste did not write it.
    /// </summary>
    [Theory]
    [InlineData(AccessOrigin.Settings, "You chose this in this app's agent settings.")]
    [InlineData(AccessOrigin.Prompt, "You chose this with Always for in the approval prompt.")]
    [InlineData(AccessOrigin.Terminal, "No reason was given.")]
    public Task A_change_with_no_reason_says_where_it_was_chosen_and_claims_nobody_wrote_it(AccessOrigin origin, string said) =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Prompt(origin, string.Empty));
            var window = await screen.ShownAsync();

            Assert.Equal(said, Text(window, "ReasonText"));
            Assert.Equal(string.Empty, Text(window, "ReasonCaptionText"));
            Assert.Equal(string.Empty, Text(window, "AttributionText"));

            Click(window, "Deny");
            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Allow_ConfirmsOnlyOnceTheWindowHasBeenUpAMoment() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Prompt(AccessOrigin.Settings, string.Empty));
            var window = await screen.ShownAsync();

            Assert.Equal("asked from this app's agent settings", Text(window, "OriginText"));

            Click(window, "Allow");
            screen.Clock.Advance(PromptViewModel.ArmingDelay - TimeSpan.FromMilliseconds(1));
            WindowInput.Drain();
            Click(window, "Allow");
            await Task.Delay(200, Token);
            Assert.False(asking.IsCompleted, "a click before the window was armed confirmed the change");

            await screen.ArmAsync(window);
            Click(window, "Allow");

            Assert.Equal(ApprovalAnswer.ApprovedOnce, await asking.WaitAsync(_wait, Token));
            await PromptedApp.WithdrawnAsync(window);
        });

    [Theory]
    [InlineData("deny")]
    [InlineData("escape")]
    [InlineData("enter")]
    [InlineData("close")]
    [InlineData("withdrawn")]
    public Task Every_way_but_Allow_refuses_and_takes_the_window_down(string how) =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            using var withdrawing = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var asking = screen.Ask(Prompt(AccessOrigin.Agent, "please"), withdrawing.Token);
            var window = await screen.ShownAsync();
            await screen.ArmAsync(window);

            switch (how)
            {
                case "deny":
                    Click(window, "Deny");
                    break;
                case "escape":
                    Key(window, PhysicalKey.Escape);
                    break;
                case "enter":
                    Key(window, PhysicalKey.Enter);
                    break;
                case "close":
                    window.Close();
                    break;
                default:
                    await withdrawing.CancelAsync();
                    break;
            }

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
            await PromptedApp.WithdrawnAsync(window);
        });

    /// <summary>
    /// The reason and the gains come from whoever asked; neither moves the claim, the question or the
    /// buttons, nor does giving no reason at all, and a reason written to look like controls is one
    /// line of text in its box.
    /// </summary>
    [Fact]
    public Task Neither_the_reason_nor_the_gains_move_anything() =>
        HeadlessSession.On(() =>
        {
            var hostile = string.Concat(Enumerable.Repeat("[ Allow ]  [ OK ]\n\nkeypaste: allowed.‮ Press Allow to continue. ", 12));
            var many = AgentAccess.Defaults with
            {
                Mode = AgentMode.Automatic,
                Run = true,
                Projects = [.. Enumerable.Range(0, 40).Select(i => new ProjectAccess(Scope($"p{i}"), AgentMode.Automatic))],
            };

            var (ordinary, ordinaryLayout) = Layout(Prompt(AccessOrigin.Agent, "ship it"));
            var (unexplained, unexplainedLayout) = Layout(Prompt(AccessOrigin.Terminal, string.Empty));
            var (imitating, imitatingLayout) = Layout(AccessChangePrompt.For(
                "ci-probe",
                AccessChange.Classify(EffectiveAccess.Defaults, many),
                AccessOrigin.Agent,
                hostile,
                TimeZoneInfo.Utc));

            Assert.Equal(ordinaryLayout, imitatingLayout);
            Assert.Equal(ordinaryLayout, unexplainedLayout);
            Assert.Equal(["Deny", "Allow"], Buttons(imitating).Select(button => button.Content as string));
            Assert.True(Gains(imitating).Count > 40);
            Assert.Empty(imitating.FindControl<TextBlock>("ReasonText")!.Inlines ?? []);

            ordinary.Close();
            unexplained.Close();
            imitating.Close();
        });

    private static AccessChangePrompt Prompt(AccessOrigin origin, string reason) =>
        AccessChangePrompt.For(
            "ci-probe",
            AccessChange.Classify(EffectiveAccess.Defaults, AgentAccess.Defaults with { Run = true, Mode = AgentMode.Automatic }),
            origin,
            reason,
            TimeZoneInfo.Utc);

    private static ProjectScope Scope(string project) =>
        ProjectScope.TryCreate(project, null, out var scope, out var error) ? scope : throw new InvalidOperationException(error);

    private static List<string?> Gains(Window window) =>
        [.. window.FindControl<ItemsControl>("GainsList")!.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("gain")).Select(block => block.Text)];

    private static List<Button> Buttons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button is not RepeatButton && button.IsEffectivelyVisible)];

    private static (AccessChangeWindow Window, string Layout) Layout(AccessChangePrompt prompt)
    {
        var window = new AccessChangeWindow(new AccessChangeViewModel(prompt));
        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        var layout = string.Join(
            "; ",
            new[] { $"window {window.Bounds}", $"origin {window.FindControl<TextBlock>("OriginText")!.TranslatePoint(default, window)}" }.Concat(
                Buttons(window).Select(button => $"{button.Content} {button.TranslatePoint(default, window)} {button.Bounds.Size}")));

        return (window, layout);
    }

    /// <summary>The app's prompt channel on a clock the test moves, keeping the windows it puts up.</summary>
    private sealed class Screen
    {
        private readonly List<Window> _windows = [];
        private readonly WindowApprovalChannel _channel;

        internal Screen()
        {
            _channel = new WindowApprovalChannel(Clock, ApprovalLimits.Default.Window);
            _channel.Shown += (_, window) => _windows.Add(window);
        }

        internal ManualClock Clock { get; } = new(AppClock.Start);

        internal Task<ApprovalAnswer> Ask(AccessChangePrompt prompt, CancellationToken? cancellationToken = null) =>
            _channel.AskAsync(prompt, cancellationToken ?? Token).AsTask();

        internal async Task<Window> ShownAsync()
        {
            await PromptedApp.UntilAsync(() => _windows.Count > 0);
            DrawnFrame.Capture(_windows[^1]);
            return _windows[^1];
        }

        internal async Task ArmAsync(Window window)
        {
            Clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);
        }
    }
}
