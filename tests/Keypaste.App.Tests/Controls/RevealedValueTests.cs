using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// The masked value cell: dots at rest, characters while held, and nothing to the accessibility bus.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>UnlockFocusTests.The_control_shows_dots_and_never_the_characters</c>, and a
/// harder claim than that one. <c>MaskedInput</c> can say it holds no password at all; this control
/// exists to draw one. What it claims instead is that the value is there only between a press and
/// its release, and that no automation path returns it — both of which are asserted below rather
/// than asserted in a comment.
/// </para>
/// <para>
/// <b>The automation assertions are made while the value is on screen.</b> Checking a peer at rest
/// is the vacuous version: every implementation passes it, including one whose peer returns the
/// value the moment there is a value to return.
/// </para>
/// </remarks>
public sealed class RevealedValueTests
{
    internal const string Value = "SENTINEL-ENV-VALUE-7d5e08";

    [Fact]
    public Task At_rest_it_shows_dots_and_never_the_characters() => HeadlessSession.On(() =>
    {
        var cell = New(out _);

        Assert.Equal(new string('•', Value.Length), cell.Rendered);
        Assert.DoesNotContain(Value, cell.Rendered, StringComparison.Ordinal);
        Assert.False(cell.IsRevealed);
    });

    [Fact]
    public Task While_held_it_shows_the_value() => HeadlessSession.On(() =>
    {
        var cell = New(out _);

        cell.BeginReveal();

        Assert.True(cell.IsRevealed);
        Assert.Equal(Value, cell.Rendered);
    });

    [Fact]
    public Task Releasing_takes_it_away_again() => HeadlessSession.On(() =>
    {
        var cell = New(out var source);

        cell.BeginReveal();
        cell.EndReveal();

        Assert.False(cell.IsRevealed);
        Assert.Equal(new string('•', Value.Length), cell.Rendered);
        Assert.Equal(1, source.ConcealCount);
    });

    /// <summary>
    /// Leaving the visual tree ends the hold, which is what a lock does.
    /// </summary>
    /// <remarks>
    /// <c>ShellViewModel</c> replaces its content rather than hiding it, so a cell showing a value
    /// when the vault locks is detached rather than released. Without this the last thing drawn
    /// would be a secret, on a control nobody is holding any more.
    /// </remarks>
    [Fact]
    public Task Leaving_the_visual_tree_ends_the_hold() => HeadlessSession.On(() =>
    {
        var cell = New(out var source);
        var host = new ContentControl { Content = cell };
        var window = new Window { Content = host };
        window.Show();

        cell.BeginReveal();
        Assert.True(cell.IsRevealed);

        host.Content = null;

        Assert.False(cell.IsRevealed);
        Assert.Equal(1, source.ConcealCount);

        window.Close();
    });

    /// <summary>
    /// A press that could not read a value still gives the reveal slot back.
    /// </summary>
    /// <remarks>
    /// The locked-vault case. A release that skipped <see cref="IRevealSource.Conceal"/> because
    /// nothing had been revealed would leave the view model's single slot held by a row showing
    /// dots, and the next row would refuse to reveal at all.
    /// </remarks>
    [Fact]
    public Task A_reveal_that_read_nothing_still_ends_cleanly() => HeadlessSession.On(() =>
    {
        var cell = New(out var source);
        source.Value = null;

        cell.BeginReveal();
        Assert.False(cell.IsRevealed);

        cell.EndReveal();
        Assert.Equal(1, source.ConcealCount);
    });

    [Fact]
    public Task Holding_twice_asks_the_row_once() => HeadlessSession.On(() =>
    {
        var cell = New(out var source);

        cell.BeginReveal();
        cell.BeginReveal();

        Assert.Equal(1, source.RevealCount);
    });

    /// <summary>
    /// No accessibility path returns the value, while the value is on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// T-22 was written about <c>IValueProvider</c>, which is only half the surface. Avalonia 12.1.0
    /// ships <c>TextBlockAutomationPeer</c>, whose name comes from the control's text — so a
    /// <c>TextBlock</c> publishes a secret over AT-SPI as the automation <em>name</em>, a different
    /// property on the same bus. Both halves are checked here, and the peer type is checked too, so
    /// swapping in a TextBlock for convenience fails rather than quietly re-opening the hole.
    /// </para>
    /// </remarks>
    [Fact]
    public Task No_automation_property_carries_the_value_while_it_is_shown() => HeadlessSession.On(() =>
    {
        var cell = New(out _);
        cell.BeginReveal();
        Assert.Equal(Value, cell.Rendered);

        // The factory Avalonia itself uses, so this asks for the peer the accessibility bus would
        // get rather than for one the test built.
        var peer = ControlAutomationPeer.CreatePeerForElement(cell);

        Assert.IsType<NoneAutomationPeer>(peer);
        Assert.Null(peer.GetProvider<Avalonia.Automation.Provider.IValueProvider>());

        foreach (var text in new[]
        {
            peer.GetName(),
            peer.GetHelpText(),
            peer.GetItemStatus(),
            peer.GetLocalizedControlType(),
            peer.GetAutomationId(),
        })
        {
            Assert.DoesNotContain(Value, text ?? string.Empty, StringComparison.Ordinal);
        }
    });

    /// <summary>
    /// The attached automation name is empty, which a one-line XAML mistake would fill.
    /// </summary>
    /// <remarks>
    /// <c>AutomationProperties.Name="{Binding Value}"</c> in a template compiles, renders
    /// identically, publishes the secret, and is invisible in review. This is the only thing that
    /// would catch it.
    /// </remarks>
    [Fact]
    public Task The_attached_automation_name_is_not_set() => HeadlessSession.On(() =>
    {
        var cell = New(out _);
        cell.BeginReveal();

        Assert.Null(AutomationProperties.GetName(cell));
        Assert.Null(AutomationProperties.GetHelpText(cell));
    });

    /// <summary>
    /// The value is in no styled property, so nothing can bind to it or read it back.
    /// </summary>
    [Fact]
    public Task The_value_is_in_no_styled_property() => HeadlessSession.On(() =>
    {
        var cell = New(out _);
        cell.BeginReveal();

        foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(cell))
        {
            Assert.DoesNotContain(
                Value,
                cell.GetValue(property)?.ToString() ?? string.Empty,
                StringComparison.Ordinal);
        }
    });

    private static RevealedValue New(out FakeRevealSource source)
    {
        source = new FakeRevealSource { Value = Value };
        return new RevealedValue { Source = source, MaskedLength = Value.Length };
    }

    private sealed class FakeRevealSource : IRevealSource
    {
        internal string? Value { get; set; }

        internal int RevealCount { get; private set; }

        internal int ConcealCount { get; private set; }

        public int MaskedLength => Value?.Length ?? 0;

        public string? Reveal()
        {
            RevealCount++;
            return Value;
        }

        public void Conceal() => ConcealCount++;
    }
}
