using Avalonia;
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
/// Always allow on the app's claim card answers nothing by itself (F.78b): it turns the same card to a question whose confirm
/// button arms a second later, in Allow once's place, with Cancel to go back; Escape cancels it and Return confirms it once armed.
/// </summary>
public sealed class AlwaysPromptWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ApprovalPrompt Offering(bool offers = true) =>
        ApprovalPrompt.For("Claude Code", new EntryName("work", "github"), "password", "deploy the app", "claude-code") with { OffersAlways = offers };

    [Fact]
    public Task Always_allow_asks_to_confirm_and_its_confirm_button_arms_a_second_later() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();
            await screen.ArmAsync(window, "AllowOnce");

            Assert.Equal("Don't ask again for Claude Code and this key. Change in Settings \u203a Agents.", ToolTip.GetTip(window.FindControl<Button>("AlwaysAllow")!));
            Click(window, "AlwaysAllow");
            WindowInput.Drain();

            Assert.False(asking.IsCompleted);
            Assert.Equal("Always give Claude Code password?", Drawn(window, "TitleText"));
            Assert.Equal("keypaste won't ask again for this key. Change it in Settings \u203a Agents.", Text(window, "ConfirmText"));
            Assert.False(window.FindControl<Button>("ConfirmAlways")!.IsEffectivelyEnabled);
            Assert.False(window.FindControl<Button>("AllowOnce")!.IsEffectivelyVisible);
            Assert.False(window.FindControl<Button>("Deny")!.IsEffectivelyVisible);

            Key(window, Avalonia.Input.PhysicalKey.Enter);
            await Task.Delay(100, Token);
            Assert.False(asking.IsCompleted, "Return before the confirmation was armed answered it");

            await screen.ArmAsync(window, "ConfirmAlways");
            Key(window, Avalonia.Input.PhysicalKey.Enter);

            Assert.Equal(ApprovalAnswer.AlwaysAllowed, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Cancel_and_Escape_return_to_the_choices_and_Deny_answers_there() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();
            await screen.ArmAsync(window, "AllowOnce");

            Click(window, "AlwaysAllow");
            WindowInput.Drain();
            Click(window, "Back");
            WindowInput.Drain();

            Assert.True(window.FindControl<Button>("AlwaysAllow")!.IsEffectivelyVisible);
            Assert.Equal("Give Claude Code password?", Drawn(window, "TitleText"));

            await screen.ArmAsync(window, "AlwaysAllow");
            Click(window, "AlwaysAllow");
            WindowInput.Drain();
            Key(window, Avalonia.Input.PhysicalKey.Escape);

            Assert.False(asking.IsCompleted, "Escape on the confirmation answered the prompt");
            Assert.True(window.FindControl<Button>("Deny")!.IsEffectivelyVisible);
            Click(window, "Deny");

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Confirming_ends_the_footer_with_its_disarmed_confirm_and_a_prompt_offering_no_always_says_why_in_Details() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();
            await screen.ArmAsync(window, "AllowOnce");
            var once = Place(window, "AllowOnce");

            Click(window, "AlwaysAllow");
            WindowInput.Drain();
            DrawnFrame.Capture(window);

            var confirm = Place(window, "ConfirmAlways");
            Assert.Equal(once.Origin.X + once.Size.Width, confirm.Origin.X + confirm.Size.Width);
            Assert.False(window.FindControl<Button>("ConfirmAlways")!.IsEffectivelyEnabled);
            Click(window, "Back");
            WindowInput.Drain();
            Click(window, "Deny");
            await asking.WaitAsync(_wait, Token);

            var plain = new ApprovalWindow(new ApprovalViewModel(Offering(offers: false) with { Label = null }));
            plain.Show();
            WindowInput.Drain();

            Assert.False(plain.FindControl<Button>("AlwaysAllow")!.IsEffectivelyVisible);
            Assert.Equal(
                "Always allow: not offered, because this agent's bridge has no --client-label, so keypaste can't remember an answer for it.",
                Text(plain, "NoAlwaysText"));
            plain.Close();
        });

    private static (Point Origin, Size Size) Place(Window window, string name)
    {
        var button = window.FindControl<Button>(name)!;
        return (button.TranslatePoint(default, window)!.Value, button.Bounds.Size);
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

        internal Task<ApprovalAnswer> Ask(ApprovalPrompt prompt) => _channel.AskAsync(prompt, Token).AsTask();

        internal async Task<Window> ShownAsync()
        {
            await PromptedApp.UntilAsync(() => _windows.Count > 0);
            DrawnFrame.Capture(_windows[^1]);
            return _windows[^1];
        }

        internal async Task ArmAsync(Window window, string button)
        {
            Clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => window.FindControl<Button>(button)!.IsEffectivelyEnabled);
        }
    }
}
