using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// The Reveal button shows its cell's value only while it is held, by pointer or by key, and says
/// the same thing to the accessibility bus held or not (D-0300, D-0232).
/// </summary>
public sealed class RevealButtonTests
{
    private const string _value = "SENTINEL-REVEAL-BUTTON-41c9";

    [Fact]
    public Task Holding_the_button_shows_the_value_and_letting_go_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();

        WindowInput.Press(screen.Window, screen.Button);
        Assert.Equal(_value, screen.Cell.Rendered);

        WindowInput.Release(screen.Window, screen.Button);
        Assert.Equal(new string('•', _value.Length), screen.Cell.Rendered);
        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task Space_held_shows_the_value_until_it_is_released() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Button.Focus();

        screen.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        WindowInput.Drain();
        Assert.True(screen.Cell.IsRevealed);

        screen.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        WindowInput.Drain();
        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task Losing_focus_mid_hold_hides_the_value() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        screen.Button.Focus();

        screen.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        WindowInput.Drain();
        screen.Other.Focus();
        WindowInput.Drain();

        Assert.False(screen.Cell.IsRevealed);
    });

    [Fact]
    public Task Holding_it_exposes_no_value_and_keeps_its_name() => HeadlessSession.On(() =>
    {
        using var screen = new Screen();
        var atRest = ControlAutomationPeer.CreatePeerForElement(screen.Button).GetName();

        WindowInput.Press(screen.Window, screen.Button);
        Assert.True(screen.Cell.IsRevealed);

        AutomationSurface.AssertNothingExposes(screen.Window, _value);
        Assert.Equal(atRest, ControlAutomationPeer.CreatePeerForElement(screen.Button).GetName());

        WindowInput.Release(screen.Window, screen.Button);
    });

    private sealed class Screen : IDisposable
    {
        internal Screen()
        {
            Cell = new RevealedValue { Source = new Source(), MaskedLength = _value.Length, FontSize = 14 };
            Button = new RevealButton { Content = "Reveal", Target = Cell };
            Other = new Button { Content = "Elsewhere" };
            Window = new Window
            {
                Width = 400,
                Height = 200,
                Content = new StackPanel { Children = { Cell, Button, Other } },
            };
            Window.Show();
            WindowInput.Drain();
        }

        internal Window Window { get; }

        internal RevealedValue Cell { get; }

        internal RevealButton Button { get; }

        internal Button Other { get; }

        public void Dispose() => Window.Close();
    }

    private sealed class Source : IRevealSource
    {
        public int MaskedLength => _value.Length;

        public string? Reveal() => _value;

        public void Conceal()
        {
        }
    }
}
