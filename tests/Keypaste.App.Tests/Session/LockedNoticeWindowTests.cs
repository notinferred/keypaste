using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The notice an agent's request raises while the app is locked (D-0466) names the vault and holds no field; it does
/// not take focus, Close is its default and is focused once it is brought forward, and Open keypaste works only after
/// it has been up a moment.
/// </summary>
public sealed class LockedNoticeWindowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task The_notice_names_the_vault_and_holds_no_field() =>
        HeadlessSession.On(() =>
        {
            var notice = Notice(new ManualClock(AppClock.Start), () => { });
            notice.Show();
            WindowInput.Drain();

            Assert.False(notice.ShowActivated);
            Assert.True(notice.Topmost);
            Assert.Equal("An agent asked for vault.kdbx", Text(notice, "HeadText"));
            Assert.Equal("It's locked, so the request was refused", Text(notice, "SubText"));
            Assert.Equal(
                "Agents can use this vault only while it's unlocked. Unlock it in keypaste, then ask the agent to try again. You won't see this again until you've unlocked keypaste.",
                Text(notice, "NoticeText"));
            Assert.Empty(notice.GetVisualDescendants().OfType<TextBox>());
            Assert.Empty(notice.GetVisualDescendants().OfType<MaskedInput>());
            Assert.Equal("Open keypaste", AutomationProperties.GetName(notice.FindControl<Button>("Open")!));
            Assert.Equal(
                ["Open keypaste", "Close"],
                notice.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
                    .Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()));

            notice.Close();
        });

    [Fact]
    public Task Brought_forward_the_notice_focuses_Close_its_default() =>
        HeadlessSession.On(async () =>
        {
            var notice = Notice(new ManualClock(AppClock.Start), () => { });
            notice.Show();
            var close = notice.FindControl<Button>("Dismiss")!;

            Assert.True(close.IsDefault);
            Assert.True(close.IsCancel);

            notice.Activate();
            await PromptedApp.UntilAsync(() => close.IsFocused);

            notice.Close();
        });

    [Fact]
    public Task Open_keypaste_works_only_once_the_notice_has_been_up_a_moment() =>
        HeadlessSession.On(async () =>
        {
            var clock = new ManualClock(AppClock.Start);
            var opened = 0;
            var notice = Notice(clock, () => opened++);
            notice.Show();
            WindowInput.Drain();
            var open = notice.FindControl<Button>("Open")!;

            Click(notice, "Open");
            clock.Advance(PromptViewModel.ArmingDelay - TimeSpan.FromMilliseconds(1));
            await Task.Delay(200, Token);
            WindowInput.Drain();

            Assert.False(open.IsEffectivelyEnabled, "Open keypaste was armed before the notice had been up a moment");
            Assert.Equal(0, opened);
            Assert.True(notice.IsVisible, "a press before Open keypaste was armed closed the notice");

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await PromptedApp.UntilAsync(() => open.IsEffectivelyEnabled);
            Click(notice, "Open");

            Assert.Equal(1, opened);
            await PromptedApp.WithdrawnAsync(notice);
        });

    [Theory]
    [InlineData("close")]
    [InlineData("escape")]
    [InlineData("enter")]
    public Task Every_way_but_Open_keypaste_closes_the_notice_and_opens_nothing(string how) =>
        HeadlessSession.On(async () =>
        {
            var clock = new ManualClock(AppClock.Start);
            var opened = 0;
            var notice = Notice(clock, () => opened++);
            notice.Show();
            notice.Activate();
            await PromptedApp.UntilAsync(() => notice.FindControl<Button>("Dismiss")!.IsFocused);
            clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => notice.FindControl<Button>("Open")!.IsEffectivelyEnabled);

            switch (how)
            {
                case "close":
                    Click(notice, "Dismiss");
                    break;
                case "escape":
                    Key(notice, PhysicalKey.Escape);
                    break;
                default:
                    Key(notice, PhysicalKey.Enter);
                    break;
            }

            await PromptedApp.WithdrawnAsync(notice);
            Assert.Equal(0, opened);
        });

    /// <summary>The notice opens at the top-right of its screen's working area, clear of the main window centred below it, whose unlock form it would otherwise cover.</summary>
    [Fact]
    public Task The_notice_opens_top_right_clear_of_the_centred_main_window() =>
        HeadlessSession.On(() =>
        {
            var main = new MainWindow();
            main.Show();
            var notice = Notice(new ManualClock(AppClock.Start), () => { });
            notice.Show();
            WindowInput.Drain();

            var screens = notice.Screens;
            Assert.NotNull(screens);
            var screen = screens.ScreenFromWindow(notice) ?? screens.Primary;
            Assert.NotNull(screen);
            var area = screen.WorkingArea;
            var shown = Bounds(notice);
            var centred = Bounds(main);

            Assert.True(area.Contains(centred.Center), $"the main window at {centred} is not on the notice's screen {area}");
            Assert.False(shown.Intersects(centred), $"the notice at {shown} covers the main window at {centred}");
            Assert.True(area.Contains(shown), $"the notice at {shown} is not inside the working area {area}");
            Assert.True(shown.X > area.Center.X && shown.Bottom < area.Center.Y, $"the notice at {shown} is not at the top-right of {area}");

            notice.Close();
            main.Close();
        });

    private static PixelRect Bounds(Window window) =>
        new(window.Position, PixelSize.FromSize(window.Bounds.Size, window.DesktopScaling));

    private static LockedNoticeWindow Notice(TimeProvider clock, Action open) => new("vault.kdbx", open, clock);
}
