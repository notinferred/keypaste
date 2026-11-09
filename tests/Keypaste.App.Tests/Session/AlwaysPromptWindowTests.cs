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
/// Always allow and Always block on the app's prompt answer nothing by themselves (F.78b): each turns the same window to a
/// question whose confirm button arms a second later, in the place the choices held, and Deny answers at every step.
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

            Click(window, "AlwaysAllow");
            WindowInput.Drain();

            Assert.False(asking.IsCompleted);
            Assert.Equal("Always give claude-code this item's password without asking you? Remove it on its page in Agents to be asked again.", Text(window, "CaptionText"));
            Assert.False(window.FindControl<Button>("ConfirmAlways")!.IsEffectivelyEnabled);
            Assert.False(window.FindControl<Button>("AllowOnce")!.IsEffectivelyVisible);

            await screen.ArmAsync(window, "ConfirmAlways");
            Click(window, "ConfirmAlways");

            Assert.Equal(ApprovalAnswer.AlwaysAllowed, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Back_returns_to_the_choices_and_Deny_answers_from_the_confirmation() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();
            await screen.ArmAsync(window, "AllowOnce");

            Click(window, "AlwaysBlock");
            WindowInput.Drain();
            Assert.Equal("Always block", window.FindControl<Button>("ConfirmAlways")!.Content);
            Click(window, "Back");
            WindowInput.Drain();

            Assert.True(window.FindControl<Button>("AlwaysBlock")!.IsEffectivelyVisible);
            Assert.Equal("An always answer is kept for claude-code on this computer until you remove it.", Text(window, "CaptionText"));

            Click(window, "AlwaysBlock");
            WindowInput.Drain();
            Click(window, "Deny");

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Confirming_moves_no_button_and_a_prompt_offering_no_always_shows_none() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Offering());
            var window = await screen.ShownAsync();
            await screen.ArmAsync(window, "AllowOnce");
            var deny = Place(window, "Deny");
            var second = Place(window, "AllowOnce");
            var third = Place(window, "AlwaysAllow");

            Click(window, "AlwaysAllow");
            WindowInput.Drain();
            DrawnFrame.Capture(window);

            Assert.Equal((deny, second, third), (Place(window, "Deny"), Place(window, "Back"), Place(window, "ConfirmAlways")));
            Click(window, "Deny");
            await asking.WaitAsync(_wait, Token);

            var plain = new ApprovalWindow(new ApprovalViewModel(Offering(offers: false) with { Label = null }));
            plain.Show();
            WindowInput.Drain();

            Assert.False(plain.FindControl<Button>("AlwaysAllow")!.IsEffectivelyVisible);
            Assert.False(plain.FindControl<Button>("AlwaysBlock")!.IsEffectivelyVisible);
            Assert.Equal("This agent's bridge has no --client-label, so keypaste can't remember an answer for it.", Text(plain, "CaptionText"));
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
