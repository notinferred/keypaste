using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Controls;

/// <summary>
/// A cell that shows dots, and shows a value from when somebody asks until they hide it, it times out,
/// they leave it or its window stops being in front of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A harder case than <see cref="MaskedInput"/>, and the difference is worth stating.</b>
/// MaskedInput's answer to T-22 is that it holds no password at all, so there is nothing for a peer
/// to return. That answer is unavailable here: putting the characters on the screen is the entire
/// feature. What is claimed instead is narrower and checkable: the value exists in this control only
/// while it is shown, it is drawn rather than handed to a control that publishes text, and no
/// accessibility path returns it.
/// </para>
/// <para>
/// <b><see cref="TextBlock"/> was ruled out, and not for the reason <c>TextBox</c> was.</b> Avalonia
/// 12.1.0 ships <c>TextBlockAutomationPeer</c>, whose name comes from the control's text, so a
/// TextBlock publishes a secret over AT-SPI as the automation <em>name</em> rather than through a
/// value pattern. That is a different property on the same bus, and T-22's original wording, which
/// reasons entirely about <c>IValueProvider</c>, does not cover it. <c>SelectableTextBlock</c> is
/// worse again: it adds a selection and a clipboard path nobody asked for.
/// </para>
/// <para>
/// <b>The value is not a styled property.</b> A <c>StyledProperty&lt;string&gt;</c> would put it in
/// the property store, where it is readable through <c>GetValue</c>, bindable, visible to a
/// diagnostics overlay, and retained for the control's lifetime rather than the reveal's. It is a
/// private field, cleared when the value is hidden, and this control draws it itself.
/// <see cref="SourceProperty"/> carries the <em>row</em>, which knows how to read a value; it never
/// carries the value.
/// </para>
/// <para>
/// <b>The honest limit, stated rather than papered over.</b> Drawing text needs a
/// <see cref="FormattedText"/>, which takes a <c>string</c>. So while it is shown the value exists in
/// this process as a string the runtime will not let anyone wipe: the same limit MaskedInput already
/// records for its keystrokes, and T-18's territory unchanged. What the design buys is that the string
/// is not in a control that publishes it and not on the clipboard. A screenshot, a screen recording, a
/// remote-desktop session and a shoulder all still see it, and SECURITY.md says so.
/// </para>
/// </remarks>
internal sealed class RevealedValue : Control
{
    /// <summary>How long a value stays shown: time to read or type a long value, and short enough that one forgotten does not sit on the screen.</summary>
    internal static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(30);

    /// <summary>The clock <see cref="ShownFor"/> runs on, inherited so a test can set it on a window.</summary>
    internal static readonly AttachedProperty<TimeProvider> ClockProperty =
        AvaloniaProperty.RegisterAttached<RevealedValue, Control, TimeProvider>("Clock", TimeProvider.System, inherits: true);

    /// <summary>How many characters the hidden value has. All this control knows at rest.</summary>
    internal static readonly StyledProperty<int> MaskedLengthProperty =
        AvaloniaProperty.Register<RevealedValue, int>(nameof(MaskedLength));

    /// <summary>The row to ask for a value. Never the value itself.</summary>
    internal static readonly StyledProperty<IRevealSource?> SourceProperty =
        AvaloniaProperty.Register<RevealedValue, IRevealSource?>(nameof(Source));

    /// <summary>The typeface to draw with.</summary>
    internal static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<RevealedValue, FontFamily>(nameof(FontFamily), FontFamily.Default);

    /// <summary>The size to draw at: a masked value stays at 14px or larger.</summary>
    internal static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<RevealedValue, double>(nameof(FontSize), 14d);

    /// <summary>The brush to draw with.</summary>
    internal static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<RevealedValue, IBrush?>(nameof(Foreground));

    /// <summary>What a shown value is drawn on, covering whatever sits under the control; none by default.</summary>
    internal static readonly StyledProperty<IBrush?> RevealBackgroundProperty =
        AvaloniaProperty.Register<RevealedValue, IBrush?>(nameof(RevealBackground));

    /// <summary>How much of the clipping ancestor's right side a shown value leaves clear, for a row's actions and the Hide among them.</summary>
    internal static readonly StyledProperty<double> KeepsClearProperty =
        AvaloniaProperty.Register<RevealedValue, double>(nameof(KeepsClear));

    /// <summary>Whether a shown value wraps within the width it has, taller instead of cut, for a cell whose width is fixed at rest.</summary>
    internal static readonly StyledProperty<bool> WrapsShownValueProperty =
        AvaloniaProperty.Register<RevealedValue, bool>(nameof(WrapsShownValue));

    private const double _revealInset = 6;
    private const double _revealEdge = 16;

    private string? _shown;
    private IRevealSource? _reading;
    private Window? _window;
    private ITimer? _timeout;
    private long _reveals;

    public RevealedValue() => FlowDirection = FlowDirection.LeftToRight;

    static RevealedValue()
    {
        AffectsRender<RevealedValue>(MaskedLengthProperty, FontFamilyProperty, FontSizeProperty, ForegroundProperty, RevealBackgroundProperty);
        AffectsMeasure<RevealedValue>(MaskedLengthProperty, FontFamilyProperty, FontSizeProperty, WrapsShownValueProperty);
    }

    /// <summary>Whichever cell is showing a value now: the app shows one at a time.</summary>
    internal static RevealedValue? Shown { get; private set; }

    /// <summary><see cref="Shown"/> changed. Carries nothing, so nothing that listens can learn a value from it.</summary>
    internal static event Action? ShownChanged;

    /// <summary>Hides whatever is shown, for a vault whose contents just moved under it.</summary>
    internal static void HideShown() => Shown?.EndReveal();

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

    internal double KeepsClear
    {
        get => GetValue(KeepsClearProperty);
        set => SetValue(KeepsClearProperty, value);
    }

    /// <summary>How wide a shown value may be drawn from the cell's left edge.</summary>
    internal double ShownRoom => Room(Bounds.Width);

    internal bool WrapsShownValue
    {
        get => GetValue(WrapsShownValueProperty);
        set => SetValue(WrapsShownValueProperty, value);
    }

    internal IBrush? RevealBackground
    {
        get => GetValue(RevealBackgroundProperty);
        set => SetValue(RevealBackgroundProperty, value);
    }

    /// <summary>Whether a value is on screen right now.</summary>
    internal bool IsRevealed => _shown is not null;

    /// <summary>
    /// What is on screen: the value while shown, otherwise dots.
    /// </summary>
    /// <remarks>
    /// The only read path this control has, and it exists so a test can assert what is drawn rather
    /// than assert that a method was called. <c>internal</c>, so it is reachable through
    /// <c>InternalsVisibleTo</c> and by nothing else.
    /// </remarks>
    internal string Rendered => _shown ?? new string('•', Math.Max(0, MaskedLength));

    /// <summary>Shows the value, or hides it if it is shown.</summary>
    internal void ToggleReveal()
    {
        if (IsRevealed)
        {
            EndReveal();
        }
        else
        {
            BeginReveal();
        }
    }

    /// <summary>Asks the row for its value and draws it in place of any other cell's, and again whenever the row says its value moved on.</summary>
    /// <remarks>Idempotent: a second ask while already revealed does not ask twice. A row with nothing to give hands its slot straight back.</remarks>
    internal void BeginReveal()
    {
        if (_shown is not null || Source is not { } source)
        {
            return;
        }

        Shown?.EndReveal();
        _shown = source.Reveal();

        if (_shown is null)
        {
            source.Conceal();
            return;
        }

        Read(source);
        WatchForLeaving();
        Shown = this;
        ShownChanged?.Invoke();
        InvalidateVisual();
        InvalidateMeasure();
    }

    /// <summary>Stops drawing the value and tells the row, even when nothing was shown, since a row may still hold the view model's single slot.</summary>
    internal void EndReveal()
    {
        StopShowing();
        Source?.Conceal();
        InvalidateVisual();
        InvalidateMeasure();
    }

    /// <summary>
    /// A new row to ask hides the old row's value.
    /// </summary>
    /// <remarks>
    /// Rows rebuilt or a template recycled onto another row arrive here as a new source, and without this
    /// one row's value would stay drawn under the next row's name. The old row is the one told, since it
    /// is the one holding the view model's reveal slot.
    /// </remarks>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property != SourceProperty)
        {
            return;
        }

        StopShowing();
        (change.OldValue as IRevealSource)?.Conceal();
        InvalidateVisual();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Layout();

        if (_shown is not null && WrapsShownValue && availableSize.Width is > 0 and < double.PositiveInfinity && text.Width > availableSize.Width)
        {
            text.MaxTextWidth = availableSize.Width;
        }

        return new Size(text.Width, text.Height);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Hit testing follows what is drawn, so the whole cell answers with its tooltip and not only the dots.
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);
        var text = Layout();

        if (_shown is null)
        {
            // Inside a mirrored cell the mask keeps to the cell's start, as its note expects; only the letters stay left to right.
            context.DrawText(text, HasMirrorTransform ? new Point(Math.Max(0, bounds.Width - text.Width), 0) : default);
            return;
        }

        // A shown value may run on over whatever lies to its right, on its own face, as far as the
        // clipping ancestor allows; past that it is cut with an ellipsis.
        var room = Room(bounds.Width);

        if (WrapsShownValue)
        {
            if (text.WidthIncludingTrailingWhitespace > bounds.Width + 1)
            {
                text.MaxTextWidth = Math.Max(1, bounds.Width);
            }
        }
        else if (text.WidthIncludingTrailingWhitespace > room + 1)
        {
            text.MaxLineCount = 1;
            text.MaxTextWidth = room;
            text.Trimming = TextTrimming.CharacterEllipsis;
        }

        var width = Math.Max(bounds.Width, Math.Min(room, text.WidthIncludingTrailingWhitespace + _revealInset));
        var face = new Rect(-_revealInset, -2, width + _revealInset, bounds.Height + 4);

        using (context.PushClip(face))
        {
            if (RevealBackground is { } background)
            {
                context.DrawRectangle(background, null, face, 4, 4);
            }

            context.DrawText(text, default);
        }
    }

    private void Read(IRevealSource source)
    {
        _reading = source;
        source.Changed += OnReadingChanged;
    }

    /// <summary>Everything that ends a reveal comes through here, so none of it can leave a watcher or a timer behind.</summary>
    private void StopShowing()
    {
        _shown = null;
        _reveals++;

        if (_reading is { } read)
        {
            read.Changed -= OnReadingChanged;
            _reading = null;
        }

        if (_window is { } window)
        {
            window.Deactivated -= OnWindowLeft;
            window.PropertyChanged -= OnWindowChanged;
            _window = null;
        }

        _timeout?.Dispose();
        _timeout = null;
        LayoutUpdated -= OnLayoutUpdated;

        if (ReferenceEquals(Shown, this))
        {
            Shown = null;
            ShownChanged?.Invoke();
        }
    }

    private void WatchForLeaving()
    {
        var reveal = _reveals;
        LayoutUpdated += OnLayoutUpdated;
        _timeout = GetValue(ClockProperty).CreateTimer(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (reveal == _reveals)
                {
                    EndReveal();
                }
            }),
            null,
            ShownFor,
            Timeout.InfiniteTimeSpan);

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.Deactivated += OnWindowLeft;
            window.PropertyChanged += OnWindowChanged;
        }
    }

    private void OnWindowLeft(object? sender, EventArgs e) => EndReveal();

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (sender is Window window
            && ((change.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)
                || (change.Property == IsVisibleProperty && !window.IsVisible)))
        {
            EndReveal();
        }
    }

    // A panel that hides a cell, such as the item pane going into its edit form, leaves it attached.
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsEffectivelyVisible)
        {
            EndReveal();
        }
    }

    private void OnReadingChanged(object? sender, EventArgs e)
    {
        if (_reading is not { } source)
        {
            return;
        }

        _shown = source.Reveal();

        if (_shown is null)
        {
            EndReveal();
            return;
        }

        InvalidateVisual();
        InvalidateMeasure();
    }

    /// <summary>How wide a shown value may be drawn: the control's own width, or with a face, up to the clipping ancestor's edge.</summary>
    private double Room(double own)
    {
        if (RevealBackground is null)
        {
            return Math.Max(0, own);
        }

        var clip = this.GetVisualAncestors().FirstOrDefault(ancestor => ancestor.ClipToBounds);

        if (clip is null || this.TranslatePoint(default, clip) is not { } origin)
        {
            return Math.Max(0, own);
        }

        return Math.Max(own, clip.Bounds.Width - origin.X - _revealEdge - KeepsClear);
    }

    /// <summary>
    /// Leaving the visual tree hides the value.
    /// </summary>
    /// <remarks>
    /// The lock path: <c>ShellViewModel</c> replaces its content rather than hiding it, so a control
    /// showing a value at the moment the vault locks is detached rather than told.
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
