using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// The Reveal toggle: one activation of any kind shows its cell's value, the next hides it, it says which it
/// will do with no value in what it says, and each way out of a reveal hides the value and leaves no timer.
/// </summary>
public sealed class RevealButtonTests
{
    private const string _value = "SENTINEL-REVEAL-BUTTON-41c9";

    [Fact]
    public Task A_click_shows_the_value_and_a_second_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();

        WindowInput.Press(screen.Window, screen.Button);
        Assert.False(screen.Cell.IsRevealed);
        WindowInput.Release(screen.Window, screen.Button);
        Assert.Equal(_value, screen.Cell.Rendered);

        WindowInput.Press(screen.Window, screen.Button);
        WindowInput.Release(screen.Window, screen.Button);
        Assert.Equal(new string('•', _value.Length), screen.Cell.Rendered);
        Assert.False(screen.Cell.IsRevealed);
    });

    [Theory]
    [InlineData(PhysicalKey.Space)]
    [InlineData(PhysicalKey.Enter)]
    public Task A_key_toggles_the_value_and_focus_stays_on_the_button(PhysicalKey key) => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Button.Focus();

        screen.Tap(key);
        Assert.True(screen.Cell.IsRevealed);
        Assert.True(screen.Button.IsFocused);

        screen.Tap(key);
        Assert.False(screen.Cell.IsRevealed);
        Assert.True(screen.Button.IsFocused);
    });

    [Fact]
    public Task A_screen_readers_single_activation_toggles_the_value() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        var invoke = ControlAutomationPeer.CreatePeerForElement(screen.Button).GetProvider<IInvokeProvider>();
        Assert.NotNull(invoke);

        invoke.Invoke();
        WindowInput.Drain();
        Assert.True(screen.Cell.IsRevealed);

        invoke.Invoke();
        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task It_says_what_it_will_do_and_no_value() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        var peer = ControlAutomationPeer.CreatePeerForElement(screen.Button);
        Assert.Equal("Reveal the key", peer.GetName());
        Assert.Equal("Reveal for 30 seconds", ToolTip.GetTip(screen.Button));

        screen.Button.Focus();
        screen.Tap(PhysicalKey.Space);

        Assert.Equal("Hide the key", peer.GetName());
        Assert.Equal("Hide", ToolTip.GetTip(screen.Button));
        AutomationSurface.AssertNothingExposes(screen.Window, _value);

        screen.Cell.EndReveal();
        Assert.Equal("Reveal the key", peer.GetName());
    });

    [Fact]
    public Task Its_word_and_icon_change_as_well_so_state_is_not_colour_alone() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Button.ShowsWord = true;
        WindowInput.Drain();
        var panel = Assert.IsType<StackPanel>(screen.Button.Content);
        var icon = Assert.IsType<KpIcon>(panel.Children[0]);
        var word = Assert.IsType<TextBlock>(panel.Children[1]);
        Assert.Equal(("eye", "Reveal"), (icon.Icon, word.Text));

        screen.Cell.BeginReveal();
        Assert.Equal(("eye-off", "Hide"), (icon.Icon, word.Text));
    });

    [Fact]
    public Task A_button_outside_the_cells_template_shows_the_cell_asking_its_row() => HeadlessSession.On(() =>
    {
        var source = new Source();
        var cell = new RevealedValue { Source = source, MaskedLength = _value.Length, FontSize = 14 };
        var other = new RevealedValue { Source = new Source(), MaskedLength = _value.Length, FontSize = 14 };
        var button = new RevealButton { TargetSource = source, Subject = "the key" };
        var window = new Window
        {
            Width = 400,
            Height = 200,
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Child = other },
                    new Border { Child = cell },
                    new StackPanel { Children = { button } },
                },
            },
        };
        window.Show();
        WindowInput.Drain();
        button.Focus();

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        WindowInput.Drain();
        Assert.True(cell.IsRevealed);
        Assert.False(other.IsRevealed);
        Assert.Equal("Hide the key", AutomationProperties.GetName(button));

        other.BeginReveal();
        Assert.False(cell.IsRevealed);
        Assert.True(other.IsRevealed);
        Assert.Equal("Reveal the key", AutomationProperties.GetName(button));
        window.Close();
    });

    [Fact]
    public Task Only_one_value_shows_at_a_time() => HeadlessSession.On(() =>
    {
        using var first = new Screen();
        using var second = new Screen();

        first.Cell.BeginReveal();
        second.Cell.BeginReveal();

        Assert.False(first.Cell.IsRevealed);
        Assert.True(second.Cell.IsRevealed);
        Assert.Same(second.Cell, RevealedValue.Shown);
    });

    [Fact]
    public Task It_hides_itself_when_its_time_is_up_and_not_before() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Cell.BeginReveal();

        screen.Clock.Advance(RevealedValue.ShownFor - TimeSpan.FromSeconds(1));
        WindowInput.Drain();
        Assert.True(screen.Cell.IsRevealed);

        screen.Clock.Advance(TimeSpan.FromSeconds(1));
        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);
    });

    [Fact]
    public Task A_time_up_from_an_earlier_reveal_does_not_hide_the_next() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Cell.BeginReveal();
        screen.Clock.Advance(TimeSpan.FromSeconds(20));
        screen.Cell.EndReveal();
        screen.Cell.BeginReveal();

        screen.Clock.Advance(TimeSpan.FromSeconds(20));
        WindowInput.Drain();
        Assert.True(screen.Cell.IsRevealed);

        screen.Clock.Advance(TimeSpan.FromSeconds(10));
        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);
    });

    [Theory]
    [InlineData("hidden by hand")]
    [InlineData("left the tree")]
    [InlineData("another row")]
    public Task No_timer_is_left_running_after_a_reveal_ends(string how) => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        var before = screen.Clock.PendingTimers;
        screen.Cell.BeginReveal();
        Assert.Equal(before + 1, screen.Clock.PendingTimers);

        switch (how)
        {
            case "hidden by hand":
                screen.Cell.EndReveal();
                break;
            case "left the tree":
                screen.Window.Content = null;
                break;
            default:
                screen.Cell.Source = new Source();
                break;
        }

        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);
        Assert.Equal(before, screen.Clock.PendingTimers);
    });

    [Fact]
    public Task The_window_losing_focus_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Cell.BeginReveal();

        WindowInput.Deactivate(screen.Window);

        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task Minimizing_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Cell.BeginReveal();

        screen.Window.WindowState = WindowState.Minimized;
        WindowInput.Drain();

        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task Hiding_the_window_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Cell.BeginReveal();

        screen.Window.Hide();
        WindowInput.Drain();

        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task A_panel_hiding_the_cell_hides_the_value_for_when_it_comes_back() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        var panel = Assert.IsType<StackPanel>(screen.Window.Content);
        screen.Cell.BeginReveal();

        panel.IsVisible = false;
        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);

        panel.IsVisible = true;
        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);
    });

    /// <summary>A list reusing a row's controls for another row: one row's value must never stay under the next one's name.</summary>
    [Fact]
    public Task A_template_recycled_onto_another_row_hides_the_value() => HeadlessSession.On(() =>
    {
        var row = new Source();
        var next = new Source();
        var items = new ItemsControl
        {
            ItemsSource = new[] { row },
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Source>((source, _) => new RevealedValue { Source = source, MaskedLength = _value.Length }),
        };
        var window = new Window { Content = items };
        window.Show();
        WindowInput.Drain();
        var cell = items.GetVisualDescendants().OfType<RevealedValue>().Single();
        cell.BeginReveal();
        Assert.True(cell.IsRevealed);

        cell.Source = next;
        Assert.False(cell.IsRevealed);
        Assert.Equal(1, row.ConcealCount);
        Assert.Null(RevealedValue.Shown);
        window.Close();
    });

    private sealed class Screen : IDisposable
    {
        internal Screen()
        {
            Cell = new RevealedValue { Source = new Source(), MaskedLength = _value.Length, FontSize = 14 };
            Button = new RevealButton { Target = Cell, Subject = "the key" };
            Other = new Button { Content = "Elsewhere" };
            Window = new Window
            {
                Width = 400,
                Height = 200,
                Content = new StackPanel { Children = { Cell, Button, Other } },
            };
            Window.SetValue(RevealedValue.ClockProperty, Clock);
            Window.Show();
            WindowInput.Drain();
        }

        internal ManualClock Clock { get; } = new();

        internal Window Window { get; }

        internal RevealedValue Cell { get; }

        internal RevealButton Button { get; }

        internal Button Other { get; }

        internal void Tap(PhysicalKey key)
        {
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Window.KeyReleaseQwerty(key, RawInputModifiers.None);
            WindowInput.Drain();
        }

        public void Dispose() => Window.Close();
    }

    private sealed class Source : IRevealSource
    {
        internal int ConcealCount { get; private set; }

        public int MaskedLength => _value.Length;

        public string? Reveal() => _value;

        public void Conceal() => ConcealCount++;
    }
}
