using Avalonia.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An agent's change to an item in the app's own prompt window (T-57): the agent, the item and the change in keypaste's words,
/// focus on Deny, Allow armed a second after the prompt is up, and Deny changing nothing.
/// </summary>
public sealed class ItemChangeWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ItemChangePrompt Prompt(ItemChangeKind kind) =>
        ItemChangePrompt.For("Claude Code", "claude-code", new EntryName("cloud", "aws-prod"), new ItemChange(kind, new DateOnly(2026, 12, 1)), "the key is due for rotation");

    [Fact]
    public Task Allow_is_armed_a_second_after_the_prompt_is_up_and_Deny_changes_nothing() =>
        HeadlessSession.On(async () =>
        {
            var clock = new ManualClock(AppClock.Start);
            var channel = new WindowApprovalChannel(clock, ApprovalLimits.Default.Window);
            List<Window> shown = [];
            channel.Shown += (_, window) => shown.Add(window);

            var allowing = channel.AskAsync(Prompt(ItemChangeKind.MarkExpired), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 1);
            var window = Assert.IsType<ItemChangeWindow>(shown[0]);
            DrawnFrame.Capture(window);

            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
            Assert.Equal("Mark cloud/aws-prod as expired on 2026-12-01", Text(window, "ChangeText"));
            Assert.Equal("Claude Code", Text(window, "ClientText"));
            Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);

            clock.Advance(Keypaste.App.ViewModels.PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);
            Click(window, "Allow");

            Assert.Equal(ApprovalAnswer.ApprovedOnce, await allowing.WaitAsync(_wait, Token));

            var denying = channel.AskAsync(Prompt(ItemChangeKind.Delete), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 2);
            Assert.Equal("Delete for good", shown[1].FindControl<Button>("Allow")!.Content);
            Click(shown[1], "Deny");

            Assert.Equal(ApprovalAnswer.Denied, await denying.WaitAsync(_wait, Token));
        });
}
