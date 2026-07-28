using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Controls;

/// <summary>
/// A cell that shows dots, and shows a value while somebody is deliberately holding it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A harder case than <see cref="MaskedInput"/>, and the difference is worth stating.</b>
/// MaskedInput's answer to T-22 is that it holds no password at all, so there is nothing for a peer
/// to return. That answer is unavailable here: putting the characters on the screen is the entire
/// feature. What is claimed instead is narrower and checkable — the value exists in this control
/// only between a press and its release, it is drawn rather than handed to a control that publishes
/// text, and no accessibility path returns it.
/// </para>
/// <para>
/// <b><see cref="TextBlock"/> was ruled out, and not for the reason <c>TextBox</c> was.</b> Avalonia
/// 12.1.0 ships <c>TextBlockAutomationPeer</c>, whose name comes from the control's text — so a
/// TextBlock publishes a secret over AT-SPI as the automation <em>name</em> rather than through a
/// value pattern. That is a different property on the same bus, and T-22's original wording, which
/// reasons entirely about <c>IValueProvider</c>, does not cover it. <c>SelectableTextBlock</c> is
/// worse again: it adds a selection and a clipboard path nobody asked for.
/// </para>
/// <para>
/// <b>The value is not a styled property.</b> A <c>StyledProperty&lt;string&gt;</c> would put it in
/// the property store, where it is readable through <c>GetValue</c>, bindable, visible to a
/// diagnostics overlay, and retained for the control's lifetime rather than the hold's. It is a
/// private field, set on press and cleared on release, and this control draws it itself.
/// <see cref="SourceProperty"/> carries the <em>row</em>, which knows how to read a value; it never
/// carries the value.
/// </para>
/// <para>
/// <b>The honest limit, stated rather than papered over.</b> Drawing text needs a
/// <see cref="FormattedText"/>, which takes a <c>string</c>. So for the duration of the hold the
/// value exists in this process as a string the runtime will not let anyone wipe — the same limit
/// MaskedInput already records for its keystrokes, and T-18's territory unchanged. What the design
/// buys is that the string is not in a control that publishes it, not on the clipboard, and gone
/// when the finger comes up. A screenshot, a screen recording, a remote-desktop session and a
/// shoulder all still see it, and SECURITY.md says so.
/// </para>
/// </remarks>
internal sealed class RevealedValue : Control
{
    /// <summary>How many characters the hidden value has. All this control knows at rest.</summary>
    internal static readonly StyledProperty<int> MaskedLengthProperty =
        AvaloniaProperty.Register<RevealedValue, int>(nameof(MaskedLength));

    /// <summary>The row to ask for a value. Never the value itself.</summary>
    internal static readonly StyledProperty<IRevealSource?> SourceProperty =
        AvaloniaProperty.Register<RevealedValue, IRevealSource?>(nameof(Source));

    /// <summary>The typeface to draw with.</summary>
    internal static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<RevealedValue, FontFamily>(nameof(FontFamily), FontFamily.Default);

    /// <summary>The size to draw at.</summary>
    internal static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<RevealedValue, double>(nameof(FontSize), 13d);

    /// <summary>The brush to draw with.</summary>
    internal static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<RevealedValue, IBrush?>(nameof(Foreground));

    private string? _shown;

    static RevealedValue()
    {
        AffectsRender<RevealedValue>(MaskedLengthProperty, FontFamilyProperty, FontSizeProperty, ForegroundProperty);
        AffectsMeasure<RevealedValue>(MaskedLengthProperty, FontFamilyProperty, FontSizeProperty);
    }

    internal int MaskedLength
    {
        get => GetValue(MaskedLengthProperty);
        set => SetValue(MaskedLengthProperty, value);
    }

    internal IRevealSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    internal FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    internal double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    internal IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Whether a value is on screen right now.</summary>
    internal bool IsRevealed => _shown is not null;

    /// <summary>
    /// What is on screen: the value while held, otherwise dots.
    /// </summary>
    /// <remarks>
    /// The only read path this control has, and it exists so a test can assert what is drawn rather
    /// than assert that a method was called. <c>internal</c>, so it is reachable through
    /// <c>InternalsVisibleTo</c> and by nothing else.
    /// </remarks>
    internal string Rendered => _shown ?? new string('•', Math.Max(0, MaskedLength));

    /// <summary>Asks the row for its value and draws it.</summary>
    /// <remarks>Idempotent: a second press while already revealed does not ask twice.</remarks>
    internal void BeginReveal()
    {
        if (_shown is not null || Source is not { } source)
        {
            return;
        }

        _shown = source.Reveal();
        InvalidateVisual();
        InvalidateMeasure();
    }

    /// <summary>Stops drawing the value and tells the row the hold ended.</summary>
    /// <remarks>
    /// Tells the row <b>even when nothing was revealed</b>. A press that found a locked vault still
    /// took a turn at the view model's single reveal slot, and a release that skipped this would
    /// leave it held by a row showing dots.
    /// </remarks>
    internal void EndReveal()
    {
        _shown = null;
        Source?.Conceal();
        InvalidateVisual();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Layout();
        return new Size(text.Width, text.Height);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.DrawText(Layout(), default);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        BeginReveal();
        e.Pointer.Capture(this);
        e.Handled = true;

        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        EndReveal();
        e.Handled = true;

        base.OnPointerReleased(e);
    }

    /// <summary>
    /// A pointer that stopped being ours ends the hold.
    /// </summary>
    /// <remarks>
    /// Without this a drag off the control, a window losing focus mid-press, or a touch cancelled
    /// by the system would leave a value on screen with nothing left to take it down.
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        EndReveal();
        base.OnPointerCaptureLost(e);
    }

    /// <summary>
    /// Leaving the visual tree ends the hold.
    /// </summary>
    /// <remarks>
    /// The lock path: <c>ShellViewModel</c> replaces its content rather than hiding it, so a control
    /// showing a value at the moment the vault locks is detached rather than released.
    /// </remarks>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndReveal();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// A peer that contributes nothing to the automation tree.
    /// </summary>
    /// <remarks>
    /// Stronger than <see cref="MaskedInput"/>'s <c>ControlAutomationPeer</c>, and correctly so.
    /// MaskedInput needs a name and focus because somebody using a screen reader has to know where
    /// their password is going. A value cell needs neither: the button beside it is what carries
    /// the name, and it names the variable rather than what the variable holds.
    /// </remarks>
    protected override AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);

    private FormattedText Layout() =>
        new(
            Rendered,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            FontSize,
            Foreground);
}
