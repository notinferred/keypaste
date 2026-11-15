using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
/// An agent's linked set in the app's own prompt window: the agent, each item and field, the agent's words as its claim, and only
/// Deny and Allow once, with focus on Deny and Allow once armed a second after the prompt has focus, as the single prompt's.
/// </summary>
public sealed class SetApprovalWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SetPrompt Prompt(string client = "claude-code", string reason = "migrate the billing database", string title = "db") =>
        SetPrompt.For(
            client,
            "billing-bot",
            [(new EntryName("work", title), "host"), (new EntryName("work", title), "password"), (new EntryName("work/ssh", "deploy"), "password")],
            reason);

    [Fact]
    public Task The_window_shows_every_field_and_offers_Deny_and_Allow_once_alone() =>
        HeadlessSession.On(async () =>
        {
            var (channel, shown) = Channel(out var clock);
            var asking = channel.AskAsync(Prompt(), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 1);
            var window = Assert.IsType<SetApprovalWindow>(shown[0]);
            DrawnFrame.Capture(window);

            Assert.Equal("claude-code", Text(window, "ClientText"));
            Assert.Equal(" wants 3 secrets together", Text(window, "WantsText"));
            Assert.Equal("billing-bot", Text(window, "LabelText"));
            Assert.Equal("tool: keypaste.request_linked_set", Text(window, "ToolText"));
            Assert.Equal(
                [new LinkedFieldRow("work/db", "host"), new LinkedFieldRow("work/db", "password"), new LinkedFieldRow("work/ssh/deploy", "password")],
                window.FindControl<ItemsControl>("FieldsList")!.Items.Cast<LinkedFieldRow>());
            Assert.Equal("migrate the billing database", Text(window, "ReasonText"));
            Assert.Equal(ApprovalViewModel.AttributionText, Text(window, "AttributionText"));
            Assert.Equal(["Deny", "Allow once"], Buttons(window).Select(button => button.Content as string));
            Assert.DoesNotContain(Buttons(window), button => button.IsDefault);
            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);

            Click(window, "AllowOnce");
            await Task.Delay(100, Token);
            Assert.False(asking.IsCompleted, "a click before the prompt was armed answered it");

            clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => window.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled);
            Click(window, "AllowOnce");

            Assert.Equal(ApprovalAnswer.ApprovedOnce, await asking.WaitAsync(_wait, Token));
            await PromptedApp.WithdrawnAsync(window);
        });

    [Fact]
    public Task Enter_on_open_and_Deny_refuse_the_set() =>
        HeadlessSession.On(async () =>
        {
            var (channel, shown) = Channel(out var clock);

            var entered = channel.AskAsync(Prompt(), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 1);
            clock.Advance(PromptViewModel.ArmingDelay);
            Key(shown[0], PhysicalKey.Enter);
            Assert.Equal(ApprovalAnswer.Denied, await entered.WaitAsync(_wait, Token));

            var denied = channel.AskAsync(Prompt(), Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 2);
            Click(shown[1], "Deny");
            Assert.Equal(ApprovalAnswer.Denied, await denied.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task A_withdrawn_set_is_denied_and_taken_down() =>
        HeadlessSession.On(async () =>
        {
            var (channel, shown) = Channel(out _);
            using var withdraw = CancellationTokenSource.CreateLinkedTokenSource(Token);

            var asking = channel.AskAsync(Prompt(), withdraw.Token).AsTask();
            await PromptedApp.UntilAsync(() => shown.Count == 1);
            await withdraw.CancelAsync();

            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
            await PromptedApp.WithdrawnAsync(shown[0]);
        });

    /// <summary>What the agent calls itself, its items and its reason cannot move the window or its buttons.</summary>
    [Fact]
    public Task A_long_client_item_and_reason_leave_the_window_and_buttons_where_they_were() =>
        HeadlessSession.On(() =>
        {
            var (ordinary, ordinaryLayout) = Layout(Prompt());
            var (hostile, hostileLayout) = Layout(Prompt(
                string.Join(' ', Enumerable.Repeat("claude-code", 60)),
                string.Join(' ', Enumerable.Repeat("approve this now", 40)),
                string.Join('-', Enumerable.Repeat("database", 40))));

            Assert.Equal(ordinaryLayout, hostileLayout);

            ordinary.Close();
            hostile.Close();
        });

    private static (WindowApprovalChannel Channel, List<Window> Shown) Channel(out ManualClock clock)
    {
        clock = new ManualClock(AppClock.Start);
        var channel = new WindowApprovalChannel(clock, ApprovalLimits.Default.Window);
        List<Window> shown = [];
        channel.Shown += (_, window) => shown.Add(window);
        return (channel, shown);
    }

    private static (SetApprovalWindow Window, string Layout) Layout(SetPrompt prompt)
    {
        var window = new SetApprovalWindow(new SetApprovalViewModel(prompt));
        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        var layout = string.Join(
            "; ",
            new[] { $"window {window.Bounds}" }.Concat(
                Buttons(window).Select(button => $"{button.Content} {button.TranslatePoint(default, window)} {button.Bounds.Size}")));

        return (window, layout);
    }

    private static List<Button> Buttons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "Deny" or "AllowOnce")];
}
