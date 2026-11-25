using Avalonia.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Always allow on the app's claim card answers in one step, as Allow once does: a click answers at once, and a key presses
/// it only once the prompt is armed. A prompt that offers no always shows no button and says why in Details.
/// </summary>
public sealed class AlwaysPromptWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ApprovalPrompt Offering(bool offers = true) =>
        ApprovalPrompt.For("Claude Code", new EntryName("work", "github"), "password", "deploy the app", "claude-code") with { OffersAlways = offers };

    [Fact]
    public Task A_click_on_Always_allow_answers_at_once_with_no_confirmation() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();

            Assert.Equal("Don't ask again for Claude Code and this key. Change it in Agents › Claude Code.", ToolTip.GetTip(window.FindControl<Button>("AlwaysAllow")!));
            Assert.Equal("Give Claude Code the password for github?", Drawn(window, "TitleText"));
            Assert.False(IsArmed(window));
            Click(window, "AlwaysAllow");

            Assert.Equal(ApprovalAnswer.AlwaysAllowed, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Space_on_a_focused_Always_allow_presses_it_only_once_the_prompt_is_armed() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();

            window.FindControl<Button>("AlwaysAllow")!.Focus();
            Tap(window, Avalonia.Input.PhysicalKey.Space);
            Key(window, Avalonia.Input.PhysicalKey.Enter);
            await Task.Delay(100, Token);
            Assert.False(asking.IsCompleted, "a key before the prompt was armed answered it");

            await screen.ArmAsync(window);
            window.FindControl<Button>("AlwaysAllow")!.Focus();
            Tap(window, Avalonia.Input.PhysicalKey.Space);

            Assert.Equal(ApprovalAnswer.AlwaysAllowed, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Escape_denies_and_a_prompt_offering_no_always_says_why_in_Details() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();

            Key(window, Avalonia.Input.PhysicalKey.Escape);
            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));

            var plain = new ApprovalWindow(new ApprovalViewModel(Offering(offers: false) with { Label = null }));
            plain.Show();
            WindowInput.Drain();

            Assert.False(plain.FindControl<Button>("AlwaysAllow")!.IsEffectivelyVisible);
            Assert.Equal(
                "Always allow: not offered, because this agent's bridge has no --client-label, so keypaste can't remember an answer for it.",
                Text(plain, "NoAlwaysText"));
            plain.Close();
        });

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

        internal Task<ApprovalAnswer> Ask(ApprovalPrompt prompt) => _channel.AskAsync(prompt, Token).AsTask();

        internal async Task<Window> ShownAsync()
        {
            await PromptedApp.UntilAsync(() => _windows.Count > 0 && IsInFront(_windows[^1]));
            DrawnFrame.Capture(_windows[^1]);
            return _windows[^1];
        }

        internal async Task ArmAsync(Window window)
        {
            Clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => IsArmed(window));
        }
    }
}
