using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
/// A terminal's edit is allowed in the app's own prompt window (D-0464): the command, the item and what
/// changes, never a value, where it came from said as a claim; Deny focused, Allow armed after a moment,
/// and every way but Allow a refusal (THREATS.md T-2).
/// </summary>
public sealed class TerminalEditWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheWindow_SaysWhatChanges_AndThatATerminalAsked() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Prompt("work/github", ["Replaces the password of work/github with the password the terminal sent; the old one stays in its history."]));
            var window = Assert.IsType<TerminalEditWindow>(await screen.ShownAsync());

            Assert.Equal("work/github", Text(window, "ItemText"));
            Assert.Equal("keypaste set", Text(window, "CommandText"));
            Assert.Equal("asked from a terminal; any program you run could have typed it", Text(window, "OriginText"));
            Assert.Equal(["Replaces the password of work/github with the password the terminal sent; the old one stays in its history."], Changes(window));
            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
            Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);
            Assert.DoesNotContain(Buttons(window), button => button.IsDefault);

            Click(window, "Deny");
            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task TheWindow_NamesThePlaceholderAValuePlants_UnderTheChange() =>
        HeadlessSession.On(async () =>
        {
            const string sets = "Sets DATABASE_URL on work/github to values the terminal sent; the item's earlier state stays in its history.";
            const string fills = "The new value of DATABASE_URL fills in {PASSWORD} from this item when it leaves.";

            var screen = new Screen();
            var asking = screen.Ask(Prompt("work/github", [sets, fills]));
            var window = await screen.ShownAsync();

            Assert.Equal([sets, fills], Changes(window));

            Click(window, "Deny");
            Assert.Equal(ApprovalAnswer.Denied, await asking.WaitAsync(_wait, Token));
        });

    [Fact]
    public Task Allow_AllowsOnlyOnceTheWindowHasBeenUpAMoment() =>
        HeadlessSession.On(async () =>
        {
            var screen = new Screen();
            var asking = screen.Ask(Prompt("work/github", ["Removes work/github for good: this vault has no recycle bin."]));
            var window = await screen.ShownAsync();

            Click(window, "Allow");
            await Task.Delay(200, Token);
            Assert.False(asking.IsCompleted, "a click before the window was armed allowed the edit");

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
            var asking = screen.Ask(Prompt("work/github", ["Moves work/github to the recycle bin, where it can be restored."]), withdrawing.Token);
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

    /// <summary>A long or hostile item and many changes move neither the claim nor the buttons.</summary>
    [Fact]
    public Task Neither_the_item_nor_the_changes_move_anything() =>
        HeadlessSession.On(() =>
        {
            var hostile = string.Concat(Enumerable.Repeat("[ Allow ]  keypaste: allowed. Press Allow to continue. ", 12));

            var (ordinary, ordinaryLayout) = Layout(Prompt("work/github", ["Removes work/github for good: this vault has no recycle bin."]));
            var (imitating, imitatingLayout) = Layout(Prompt(hostile, [.. Enumerable.Range(0, 40).Select(_ => hostile)]));

            Assert.Equal(ordinaryLayout, imitatingLayout);
            Assert.Equal(["Deny", "Allow"], Buttons(imitating).Select(button => button.Content as string));

            ordinary.Close();
            imitating.Close();
        });

    private static TerminalEditPrompt Prompt(string item, IReadOnlyList<string> changes) =>
        new() { Command = "keypaste set", Item = item, Changes = changes };

    private static List<string?> Changes(Window window) =>
        [.. window.FindControl<ItemsControl>("ChangesList")!.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("change")).Select(block => block.Text)];

    private static List<Button> Buttons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button is not RepeatButton && button.IsEffectivelyVisible)];

    private static (TerminalEditWindow Window, string Layout) Layout(TerminalEditPrompt prompt)
    {
        var window = new TerminalEditWindow(new TerminalEditViewModel(prompt));
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

        internal Task<ApprovalAnswer> Ask(TerminalEditPrompt prompt, CancellationToken? cancellationToken = null) =>
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
