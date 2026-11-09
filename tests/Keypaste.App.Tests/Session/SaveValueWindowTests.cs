using Avalonia.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An agent's request to save a value in the app's own prompt window (F.79): focus on Cancel, no value entered for the
/// person, Save armed a second after a value is entered, and the value handed back only on Save.
/// </summary>
public sealed class SaveValueWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SavePrompt Prompt() => SavePrompt.For("Claude Code", "claude-code", "work/stripe", "STRIPE_KEY", "the new key") with { Creates = true };

    [Fact]
    public Task Save_hands_back_the_value_entered_and_Cancel_hands_back_none() =>
        HeadlessSession.On(async () =>
        {
            var clock = new ManualClock(AppClock.Start);
            var channel = new WindowApprovalChannel(clock, ApprovalLimits.Default.Window);
            List<Window> shown = [];
            channel.Shown += (_, window) => shown.Add(window);

            var saving = channel.AskToSaveAsync(Prompt(), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 1);
            var window = Assert.IsType<SaveValueWindow>(shown[0]);
            DrawnFrame.Capture(window);
            var prompt = (SaveValueViewModel)window.DataContext!;

            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
            Assert.Equal("Creates the item work/stripe and saves STRIPE_KEY there.", Text(window, "PlacementText"));
            Assert.False(window.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled);

            foreach (var key in "sk_live_new")
            {
                prompt.Value.Type(key);
            }

            clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => window.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled);
            Click(window, "AllowOnce");

            Assert.Equal(new SaveAnswer(ApprovalAnswer.ApprovedOnce, "sk_live_new"), await saving.WaitAsync(_wait, Token));

            var cancelling = channel.AskToSaveAsync(Prompt(), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 2);
            ((SaveValueViewModel)shown[1].DataContext!).Value.Type('x');
            Click(shown[1], "Deny");

            Assert.Equal(new SaveAnswer(ApprovalAnswer.Denied, null), await cancelling.WaitAsync(_wait, Token));
        });
}
